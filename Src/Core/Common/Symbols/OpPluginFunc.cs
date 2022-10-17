using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.Formula.API;
using Microsoft.Formula.API.Nodes;
using Microsoft.Formula.Common;
using Microsoft.Formula.Common.Rules;
using Microsoft.Formula.Common.Terms;
using Microsoft.Formula.Solver;

namespace Microsoft.Formula.Common.Terms;

public abstract class OpPluginFunc
{
    protected OpKind opKind;
    protected String name;
    protected int arity;

    private static object opKindLock = new object();
    private static int nextOpKind = System.Enum.GetValues(typeof(OpKind)).Cast<int>().Max();

    static OpKind allocateOpKind()
    {
        lock (opKindLock)
        {
            nextOpKind += 1;
            return (OpKind) nextOpKind;
        }
    }
    Func<TermIndex, Term[], IEnumerable<Tuple<RelKind, Term, Term>>> appConstrainer = null;
    Func<SymExecuter, Bindable[], Term> symEvaluator = null;

    public OpPluginFunc()
    {
        this.opKind = allocateOpKind();
        this.arity = this.GetArgTypes().Length;
    }

    public OpKind GetOpKind()
    {
        return opKind;
    }
    
    public BaseOpSymb GetBaseOpSymb()
    {
        return new BaseOpSymb(opKind, arity,
            Validator,
            GetUpApproxRef,
            GetDownApproxRef,
            Evaluator
        );
    }

    public abstract String GetName();
    public abstract BaseSortKind[] GetArgTypes();
    public abstract BaseSortKind GetReturnType();
    public abstract Rational Evaluate(Rational[] args);

    public double RationalToDouble(Rational r)
    {
        return ((double) r.Numerator)/(double) r.Denominator;
    }

    public long RationalToLong(Rational r)
    {
        return (long) (r.Numerator/r.Denominator);
    }
    private static Term MkBaseSort(TermIndex index, BaseSortKind sort)
    {
        bool wasAdded;
        return index.MkApply(index.SymbolTable.GetSortSymbol(sort), TermIndex.EmptyArgs, out wasAdded);
    }

    private static Rational[] TermsToRationals(Term[] terms)
    {
        Rational[] rationals = new Rational[terms.Length];
        for (int i = 0; i < terms.Length; i++)
        {
            rationals[i] = (Rational) ((BaseCnstSymb) terms[i].Symbol).Raw;
        }

        return rationals;
    }

    private static Rational[] ValuesToRationals(Bindable[] values)
    {
        Rational[] rationals = new Rational[values.Length];
        for (int i = 0; i < rationals.Length; i++)
        {
            Term t = values[i].Binding;
            if (t.Symbol.Kind != SymbolKind.BaseCnstSymb)
            {
                return null;
            }

            BaseCnstSymb s = (BaseCnstSymb) t.Symbol;
            if (s.CnstKind != CnstKind.Numeric)
            {
                return null;
            }

            rationals[i] = (Rational) s.Raw;
        }

        return rationals;
    }
    
    private Func<TermIndex, Term[], Term[]> GetUpApproxRef
    {
        get { return UpApproximate; }
    }

    private Func<TermIndex, Term[], Term[]> GetDownApproxRef
    {
        get { return DownApproximate;  }
    }

    private Term[] UpApproximate(TermIndex index, Term[] args)
    {
        bool allGround = true;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Groundness != Groundness.Ground)
            {
                allGround = false;
                break;
            }
        }

        if (allGround)
        {
            Rational[] rationals = TermsToRationals(args);
            bool wasAdded;
            return new Term[]
            {
                index.MkCnst(Evaluate(rationals), out wasAdded)
            };
        }
        else
        {
            return new Term[]
            {
                MkBaseSort(index, GetReturnType())
            };
        }
    }

    private Term[] DownApproximate(TermIndex index, Term[] args)
    {
        Contract.Requires(index != null && args != null && args.Length == 1);
        Term[] terms = new Term[arity];
        BaseSortKind[] funcTypes = GetArgTypes();
        
        for (int i = 0; i < terms.Length; i++)
        {
            terms[i] = MkBaseSort(index, funcTypes[i]);
        }

        return terms;
    }

//                    var newTerm = facts.TermIndex.MkCnst(new Rational(pos, BigInteger.One), out wasAdded);
    private Term Evaluator(Executer facts, Bindable[] values)
    {
        Rational[] rationals = ValuesToRationals(values);
        bool wasAdded;
        return facts.TermIndex.MkCnst(Evaluate(rationals), out wasAdded);
    }

    public bool allowComprehension(int arg)
    {
        return false;
    }
    
    public bool Validator(Node n, List<Flag> flags)
    {
        Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
        var ft = (FuncTerm)n;
        Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == this.opKind);
        
        if (ft.Args.Count != arity)
        {
            var flag = new Flag(
                SeverityKind.Error,
                ft,
                Constants.BadSyntax.ToString(string.Format("{0} got {1} arguments but needs {2}", name, ft.Args.Count, arity)),
                Constants.BadSyntax.Code);
            flags.Add(flag);
            return false;
        }

        int i = 0;
        foreach (var a in ft.Args)
        {
            if (a.NodeKind == NodeKind.Compr && !allowComprehension(i))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.BadSyntax.ToString(string.Format("comprehension not allowed in argument {1} of {0}", name, i + 1)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return false;
            }
            else if (a.NodeKind != NodeKind.Compr && allowComprehension(i))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.BadSyntax.ToString(string.Format("comprehension required in argument {1} of {0}", name, i + 1)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return false;
            }

            ++i;
        }

        return true;
    }
}