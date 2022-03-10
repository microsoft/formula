namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Rules;
    using Common.Terms;

    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3ArithExpr = Microsoft.Z3.ArithExpr;

    /// <summary>
    /// And index of encodings from Formula to Z3 terms.
    /// </summary>
    internal partial class TermEncIndex
    {
        private Map<Term, Z3Expr> encodings = new Map<Term, Z3Expr>(Term.Compare); 

        public Solver Solver
        {
            get;
            private set;
        }

        public TermEncIndex(Solver solver)
        {
            Contract.Requires(solver != null);
            Solver = solver;
        }

        public Z3Expr GetVarEnc(Term v, Term type)
        {
            Contract.Requires(v != null && type != null && v.Symbol.IsVariable);
            Z3Expr varEnc;
            if (encodings.TryFindValue(v, out varEnc))
            {
                return varEnc;
            }

            var typEmb = Solver.TypeEmbedder.ChooseRepresentation(type);
            //varEnc = Solver.Context.MkFreshConst(((UserCnstSymb)v.Symbol).FullName, typEmb.Representation);
            varEnc = Solver.Context.MkConst(((UserCnstSymb)v.Symbol).FullName, typEmb.Representation);
            encodings.Add(v, varEnc);
            return varEnc;
        }

        // Checks whether Term t contains any temporary ConSymb elements for which no TypeEmbedding is available
        public bool CanGetEncoding(Term t)
        {
            Z3Expr enc;
            if (encodings.TryFindValue(t, out enc))
            {
                return true;
            }

            bool hasEncoding = true;

            t.Compute<Unit>(
                (x, s) =>
                {
                    if (x.Symbol.Kind == SymbolKind.ConSymb)
                    {
                        if (((ConSymb)x.Symbol).SortSymbol == null)
                        {
                            hasEncoding = false;
                            return null;
                        }
                        else
                        {
                            return x.Args;
                        }
                    }
                    else
                    {
                        return null;
                    }
                },
                (x, ch, s) =>
                {
                    return default(Unit);
                }
                );

            return hasEncoding;
        }

        /// <summary>
        /// Returns an encoding of this term, possibly after applying some normalizing rewrites. 
        /// </summary>
        public Z3Expr GetTerm(Term t, out Term normalizedTerm, SymExecuter facts = null)
        {
            Contract.Requires(t != null);
            normalizedTerm = Normalize(t);
            Z3Expr enc, encp;
            if (encodings.TryFindValue(normalizedTerm, out enc))
            {
                return enc;
            }

            int i;
            bool wasAdded;
            ITypeEmbedding typEmb;
            ConstructorEmbedding conEmb;
            return normalizedTerm.Compute<Z3Expr>(
                (x, s) =>
                {
                    if (encodings.ContainsKey(x))
                    {
                        return null;
                    }
                    else if (x.Groundness == Groundness.Ground &&
                             !x.Symbol.IsSymCount)
                    {
                        return null;
                    }
                    else
                    {
                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    if (encodings.TryFindValue(x, out encp))
                    {
                        return encp;
                    }
                    else if (x.Groundness == Groundness.Ground &&
                             !Term.IsSymbolicTerm(x))
                    {
                        typEmb = Solver.TypeEmbedder.ChooseRepresentation(x);
                        encp = Solver.TypeEmbedder.MkGround(x, typEmb);
                        encodings.Add(x, encp);
                        return encp;
                    }

                    //// x must be non-ground. Because variables are already encoded, then x should not be a variable
                    Contract.Assert(!x.Symbol.IsVariable);
                    if (x.Symbol.IsDataConstructor)
                    {
                        if (x.Symbol.Kind == SymbolKind.ConSymb)
                        {
                            conEmb = (ConstructorEmbedding)Solver.TypeEmbedder.GetEmbedding(
                                        Solver.Index.MkApply(((ConSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                        }
                        else
                        {
                            conEmb = (ConstructorEmbedding)Solver.TypeEmbedder.GetEmbedding(
                                        Solver.Index.MkApply(((MapSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                        }

                        i = 0;
                        var args = new Z3Expr[x.Symbol.Arity];
                        foreach (var a in ch)
                        {
                            typEmb = Solver.TypeEmbedder.GetEmbedding(conEmb.Z3Constructor.ConstructorDecl.Domain[i]);
                            args[i++] = typEmb.MkCoercion(a);
                        }

                        encp = conEmb.MkGround(x.Symbol, args);
                        encodings.Add(x, encp);
                        return encp;
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                    {
                        switch (((BaseOpSymb)x.Symbol).OpKind)
                        {
                            case OpKind.Add:
                                encp = Solver.TypeEmbedder.Context.MkAdd((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.Sub:
                                encp = Solver.TypeEmbedder.Context.MkSub((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.Mul:
                                encp = Solver.TypeEmbedder.Context.MkMul((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.Div:
                                encp = Solver.TypeEmbedder.Context.MkDiv((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case RelKind.Lt:
                                encp = Solver.TypeEmbedder.Context.MkLt((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case RelKind.Le:
                                encp = Solver.TypeEmbedder.Context.MkLe((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case RelKind.Gt:
                                encp = Solver.TypeEmbedder.Context.MkGt((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case RelKind.Ge:
                                encp = Solver.TypeEmbedder.Context.MkGe((Z3ArithExpr)ch.ElementAt(0), (Z3ArithExpr)ch.ElementAt(1));
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.SymCount:
                                Z3ArithExpr[] exprs = new Z3ArithExpr[x.Args.Length];
                                exprs[0] = (Z3ArithExpr)ch.ElementAt(0);
                                for (int i = 1; i < x.Args.Count(); i++)
                                {
                                    Z3BoolExpr boolExpr = facts.GetSideConstraints(x.Args[i]);
                                    exprs[i] = (Z3ArithExpr)facts.Solver.Context.MkITE(boolExpr,
                                                                                     facts.Solver.Context.MkInt(1),
                                                                                     facts.Solver.Context.MkInt(0));
                                }
                                encp = facts.Solver.Context.MkAdd(exprs);
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.SymAnd:
                                Term tempTerm;
                                var tValue = GetTerm(facts.Index.TrueValue, out tempTerm);
                                var fValue = GetTerm(facts.Index.FalseValue, out tempTerm);
                                encp = Solver.Context.MkITE(Solver.Context.MkEq(tValue, ch.ElementAt(0)),
                                    Solver.Context.MkITE(Solver.Context.MkEq(tValue, ch.ElementAt(1)), tValue, fValue), fValue);
                                encodings.Add(x, encp);
                                return encp;
                            case OpKind.SymAndAll:
                                var tEnc = GetTerm(facts.Index.TrueValue, out tempTerm);
                                var fEnc = GetTerm(facts.Index.FalseValue, out tempTerm);
                                Z3BoolExpr[] boolExprs = new Z3BoolExpr[ch.Count()];
                                for (int i = 0; i < ch.Count(); i++)
                                {
                                    boolExprs[i] = Solver.Context.MkEq(tEnc, ch.ElementAt(i));
                                }
                                Z3Expr currExpr = null;
                                for (int i = 0; i < ch.Count(); i++)
                                {
                                    if (currExpr == null)
                                    {
                                        currExpr = Solver.Context.MkITE(boolExprs[i], tEnc, fEnc);
                                    }
                                    else
                                    {
                                        currExpr = Solver.Context.MkITE(boolExprs[i], currExpr, fEnc);
                                    }
                                }
                                encodings.Add(x, currExpr);
                                return currExpr;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                });
        }

        public void Debug_Print()
        {
            foreach (var kv in encodings)
            {
                Console.WriteLine("Entry: {0}", kv.Key.Debug_GetSmallTermString());
                Console.WriteLine("   Representation: {0}", Solver.TypeEmbedder.GetEmbedding(kv.Value.Sort).Type.Debug_GetSmallTermString());
                Console.WriteLine("   Encoding: {0}", kv.Value);
            }
        }

        private Term Normalize(Term t)
        {
            return t;
        }
    }
}
