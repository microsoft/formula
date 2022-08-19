namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Common;
    using Common.Rules;
    using Common.Terms;
    using Compiler;

    using Nodes;
    using Solver;

    public sealed class SolveResult
    {
        private int maxSols;
        private FactSet partialModel;
        private CancellationToken cancel;
        private List<List<string>> modelOutputs = null;
        private Model srcPartialModel;
        private Solver solver;

        public Env Env
        {
            get;
            private set;
        }

        public DateTime StopTime
        {
            get;
            private set;
        }

        public LiftedInt NumSolutions
        {
            get;
            private set;
        }

        public LiftedBool Solvable
        {
            get;
            private set;
        }

        public bool WasCancelled
        {
            get;
            private set;
        }

        public List<Flag> Flags
        {
            get;
            private set;
        }

        internal SolveResult(       
            Model srcPartialModel,
            FactSet partialModel, 
            int maxSols,
            Env env,
            CancellationToken cancel)
        {
            Contract.Requires(partialModel != null);
            Contract.Requires(maxSols > 0);
            Flags = new List<Flag>();
            this.partialModel = partialModel;
            this.cancel = cancel;
            this.maxSols = maxSols;
            this.srcPartialModel = srcPartialModel;
            this.Env = env;
        }

        internal void Start()
        {
            solver = new Solver(partialModel, srcPartialModel, Env, cancel);
            if (cancel.IsCancellationRequested)
            {
                WasCancelled = true;
            }

            Solvable = solver.Solve();
            StopTime = DateTime.Now;
        }

        public void GetOutputModel(int solNum)
        {
            solver.GetSolution(solNum);
        }

        /// <summary>
        /// Returns a task that builds a solution model. Returns null if there is no such solution.
        /// TODO: Support cancellation
        /// </summary>
        public Task<AST<Program>> GetOutputModel(
            int solNum,
            string outModelName,
            ProgramName outProgName,
            string aliasPrefix,
            CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outModelName));
            Contract.Requires(outProgName != null && !string.IsNullOrWhiteSpace(aliasPrefix));

            if ((solNum < NumSolutions) != true)
            {
                return null;
            }

            var facts = MkSolutionTerms(solNum, new TermIndex(partialModel.Index.SymbolTable));
            aliasPrefix = aliasPrefix.Trim();
            return Task.Factory.StartNew<AST<Program>>(() =>
            {
                var bldr = new Builder();
                var modelRef = MkModelDecl(outModelName, bldr);
                var aliases = new Map<Term, string>(Term.Compare);
                foreach (var t in facts)
                {
                    BuildFactBody(facts, t, bldr, modelRef, aliasPrefix, aliases, false);
                }

                int count;
                bldr.GetStackCount(out count);
                Contract.Assert(count == 0);
                bldr.Load(modelRef);
                bldr.Close();

                ImmutableArray<AST<Node>> asts;
                bldr.GetASTs(out asts);

                var prog = Factory.Instance.MkProgram(outProgName);
                return Factory.Instance.AddModule(prog, asts[0]);
            });
        }

        private Set<Term> MkSolutionTerms(int solNum, TermIndex index)
        {
            Contract.Requires(modelOutputs != null && solNum < modelOutputs.Count);
            int end;
            var facts = new Set<Term>(Term.Compare);
            foreach (var s in modelOutputs[solNum])
            {
                facts.Add(Parse(s, 0, index, out end));
            }

            return facts;
        }

        private void BuildFactBody(
            Set<Term> facts,
            Term t,
            Builder bldr,
            BuilderRef modelRef,
            string aliasPrefix,
            Map<Term, string> aliases,
            bool removeRenaming)
        {
            Contract.Assert(t.Symbol.Kind == SymbolKind.ConSymb || t.Symbol.Kind == SymbolKind.MapSymb);
            var myAlias = ToAliasName((UserSymbol)t.Symbol, removeRenaming, aliasPrefix, aliases.Count);
            bldr.PushId(myAlias);

            string alias;
            BaseCnstSymb bc;
            UserCnstSymb uc;
            var nsStack = new Stack<Namespace>();
            t.Compute<Unit>(
                (x, s) =>
                {
                    if (aliases.ContainsKey(x))
                    {
                        return null;
                    }
                    else
                    {
                        if (x.Symbol.Kind == SymbolKind.ConSymb || x.Symbol.Kind == SymbolKind.MapSymb)
                        {
                            nsStack.Push(((UserSymbol)x.Symbol).Namespace);
                        }

                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            {
                                bc = (BaseCnstSymb)x.Symbol;
                                switch (bc.CnstKind)
                                {
                                    case CnstKind.Numeric:
                                        bldr.PushCnst((Rational)bc.Raw);
                                        break;
                                    case CnstKind.String:
                                        bldr.PushCnst((string)bc.Raw);
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }

                                break;
                            }
                        case SymbolKind.UserCnstSymb:
                            {
                                uc = (UserCnstSymb)x.Symbol;
                                Contract.Assert(uc.IsNewConstant && !uc.IsSymbolicConstant);
                                bldr.PushId(ToIdString(uc, nsStack.Peek()));
                                break;
                            }
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            {
                                if (aliases.TryFindValue(x, out alias))
                                {
                                    bldr.PushId(alias);
                                }
                                else
                                {
                                    nsStack.Pop();
                                    bldr.PushId(x != t ? ToIdString((UserSymbol)x.Symbol, nsStack.Peek()) : ToIdString((UserSymbol)x.Symbol, removeRenaming));
                                    bldr.PushFuncTerm();
                                    for (int i = 0; i < x.Args.Length; ++i)
                                    {
                                        bldr.AddFuncTermArg();
                                    }
                                }

                                break;
                            }
                        default:
                            throw new NotImplementedException();
                    }

                    return default(Unit);
                });

            bldr.PushModelFact();
            bldr.Load(modelRef);
            bldr.AddModelFact(true);
            bldr.Pop();
            aliases.Add(t, myAlias);
        }

        private BuilderRef MkModelDecl(string modelName, Builder bldr)
        {
            BuilderRef result;
            var domLoc = (Location)partialModel.Model.Node.Domain.CompilerData;
            bldr.PushModRef(((Domain)domLoc.AST.Node).Name, null, ((Program)domLoc.AST.Root).Name.ToString());
            bldr.PushModel(modelName, false, ComposeKind.None);
            bldr.Store(out result);
            return result;
        }

        private static string ToEscapedName(string name)
        {
            var firstPrime = name.IndexOf('\'');
            if (firstPrime < 0 || firstPrime >= name.Length)
            {
                return name;
            }

            return string.Format("{0}_{1}Prime", name.Substring(0, firstPrime), name.Length - firstPrime);
        }

        private static string ToAliasName(UserSymbol symb, bool removeRenaming, string aliasPrefix, int id)
        {
            string name = string.Format("{0}__0x{1:x}", ToEscapedName(symb.Name), id);
            var ns = symb.Namespace;
            while (ns.Parent != null && (!removeRenaming || ns.Parent.Parent != null))
            {
                name = ToEscapedName(ns.Name) + "__" + name;
                ns = ns.Parent;
            }

            return string.Format("{0}__{1}", ToEscapedName(aliasPrefix), name);
        }

        private static string ToIdString(UserSymbol symb, bool removeRenaming)
        {
            //// If this is in the root namespace, then the string is always just the name.
            if (symb.Namespace.Parent == null)
            {
                return symb.Name;
            }

            //// Otherwise, drop the topmost renaming if requested.
            string name = symb.Name;
            var ns = symb.Namespace;
            while (ns.Parent != null && (!removeRenaming || ns.Parent.Parent != null))
            {
                name = ns.Name + "." + name;
                ns = ns.Parent;
            }

            return name;
        }

        private static string ToIdString(UserSymbol symb, Namespace parent)
        {
            //// If this is in the root namespace, then the string is always just the name.
            if (symb.Namespace.Parent == null)
            {
                return symb.Name;
            }

            //// Otherwise, factor out the namespace included in the parent.
            string name = symb.Name;
            var ns = symb.Namespace;
            while (ns != parent)
            {
                name = ns.Name + "." + name;
                ns = ns.Parent;
            }

            return name;
        }

        private static Term Parse(string t, int start, TermIndex index, out int end)
        {
            Contract.Requires(!string.IsNullOrEmpty(t) && start < t.Length);
            end = -1;
            bool wasAdded, result;
            var tstart = t[start];
            while (char.IsWhiteSpace(tstart))
            {
                ++start;
                tstart = t[start];
            }

            if (tstart == '\"')
            {
                end = start;
                do
                {
                    end = t.IndexOf('\"', end + 1);
                    Contract.Assert(end >= 0);
                } 
                while (t[end - 1] == '\\');

                if (end == start + 1)
                {
                    return index.MkCnst(string.Empty, out wasAdded);
                }
                else
                {
                    return index.MkCnst(t.Substring(start + 1, end - start - 1).Replace("\\\"", "\""), out wasAdded); 
                }
            }
            else if (char.IsDigit(tstart) || tstart == '+' || tstart == '-' || tstart == '.')
            {
                var end1 = t.IndexOf(',', start);
                var end2 = t.IndexOf(')', start);
                end = (end1 >= 0 && end2 >= 0) ? Math.Min(end1, end2) : Math.Max(end1, end2);
                Rational r;
                if (end < 0)
                {
                    result = Rational.TryParseDecimal(t.Substring(start).Trim(), out r);
                    Contract.Assert(result);
                    end = t.Length - 1;
                }
                else
                {
                    --end;
                    result = Rational.TryParseDecimal(t.Substring(start, end - start + 1).Trim(), out r);
                    Contract.Assert(result);
                }

                return index.MkCnst(r, out wasAdded);
            }
            else
            {
                Contract.Assert(char.IsLetter(tstart) || tstart == '_');
                UserSymbol us, other;

                var end1 = t.IndexOf(',', start);
                var end2 = t.IndexOf(')', start);
                var end3 = t.IndexOf('(', start);
                end = (end1 >= 0 && end2 >= 0) ? Math.Min(end1, end2) : Math.Max(end1, end2);
                end = (end >= 0 && end3 >= 0) ? Math.Min(end, end3) : Math.Max(end, end3);
                if (end < 0)
                {
                    us = index.SymbolTable.Resolve(t.Substring(start).Trim(), out other);
                    Contract.Assert(us != null && other == null && us.Kind == SymbolKind.UserCnstSymb);
                    end = t.Length - 1;
                    return index.MkApply(us, TermIndex.EmptyArgs, out wasAdded);
                }
                else if (end == end1 || end == end2)
                {
                    --end;
                    us = index.SymbolTable.Resolve(t.Substring(start, end - start + 1).Trim(), out other);
                    Contract.Assert(us != null && other == null && us.Kind == SymbolKind.UserCnstSymb);
                    return index.MkApply(us, TermIndex.EmptyArgs, out wasAdded);
                }
                else
                {
                    us = index.SymbolTable.Resolve(t.Substring(start, end - start).Trim(), out other);
                    Contract.Assert(us != null && other == null && us.IsDataConstructor);
                    var args = new Term[us.Arity];
                    for (int i = 0; i < us.Arity; ++i)
                    {
                        ++end;
                        args[i] = Parse(t, end, index, out end);
                        if (i < us.Arity - 1)
                        {
                            end = t.IndexOf(',', end + 1);
                        }
                        else
                        {
                            end = t.IndexOf(')', end + 1);
                        }

                        Contract.Assert(end >= 0);
                    }

                    return index.MkApply(us, args, out wasAdded);
                }
            }
        }
    }
}
