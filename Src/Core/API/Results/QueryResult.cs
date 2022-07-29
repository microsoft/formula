namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Common;
    using Common.Rules;
    using Common.Terms;
    using Compiler;

    using Nodes;

    public sealed class QueryResult
    {
        private Executer exe = null;
        private CancellationToken cancel;
        private ExecuterStatistics stats;
        private FactSet facts;

        public bool KeepDerivations
        {
            get;
            private set;
        }

        public DateTime StopTime
        {
            get;
            private set;
        }

        public LiftedBool Conclusion
        {
            get;
            private set;
        }

        public bool WasCancelled
        {
            get;
            private set;
        }

        public AST<Model> Source
        {
            get
            {
                return facts.Model;
            }
        }

        internal QueryResult(
            FactSet facts, 
            ExecuterStatistics stats,
            bool keepDers,
            CancellationToken cancel)
        {
            Contract.Requires(facts != null);

            this.facts = facts;
            this.cancel = cancel;
            this.stats = stats;
            KeepDerivations = keepDers;
            Conclusion = LiftedBool.Unknown;
        }

        public LiftedBool IsDerivable(string t, out List<Flag> flags)
        {
            flags = new List<Flag>();
            Term grndTerm;
            if (!ParseGoalWithDontCares(t, flags, out grndTerm))
            {
                return LiftedBool.Unknown;
            }

            return exe.IsDerived(grndTerm);
        }

        public IEnumerable<ProofTree> EnumerateProofs(string t, out List<Flag> flags, int proofsPerTerm = 0)
        {
            flags = new List<Flag>();
            return EnumerateProofsUsingFlags(t, flags, proofsPerTerm);
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

        public IEnumerable<AST<Node>> EnumerateDerivations(string t, out List<Flag> flags, bool isSorted = false)
        {
            flags = new List<Flag>();
            return EnumerateDerivationsUsingFlags(t, flags, isSorted);
        }

        internal void Start()
        {
            Contract.Assert(exe == null);
            exe = new Executer(facts, stats, KeepDerivations, cancel);
            exe.Execute();
            StopTime = DateTime.Now;
            if (cancel.IsCancellationRequested)
            {
                WasCancelled = true;
            }

            UserSymbol requires;
            facts.Index.SymbolTable.ModuleSpace.TryGetSymbol(SymbolTable.RequiresName, out requires);
            Contract.Assert(requires != null);

            bool wasAdded;
            Conclusion = exe.IsDerived(facts.Index.MkApply(requires, TermIndex.EmptyArgs, out wasAdded));
        }

        private IEnumerable<ProofTree> EnumerateProofsUsingFlags(string t, List<Flag> flags, int proofsPerTerm)
        {
            if (!exe.KeepDerivations)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.BadSyntax.ToString("Proofs were not stored."),
                    Constants.BadSyntax.Code));
                yield break;
            }

            Term goalTerm;
            if (!ParseGoalWithDontCares(t, flags, out goalTerm))
            {
                yield break;
            }

            int count;
            foreach (var dt in exe.GetDerivedTerms(goalTerm))
            {
                count = 0;
                foreach (var p in exe.GetProofs(dt))
                {
                    ++count;
                    yield return p;

                    if (proofsPerTerm > 0 && count >= proofsPerTerm)
                    {
                        break;
                    }
                }
            }
        }

        private IEnumerable<AST<Node>> EnumerateDerivationsUsingFlags(string t, List<Flag> flags, bool isSorted)
        {
            int end = -1;
            Term goalTerm = Parse(t, 0, facts.Index, out end);
            if (end >= 0 && goalTerm.Groundness == Groundness.Variable)
            {

            }
            else if (!ParseGoalWithDontCares(t, flags, out goalTerm))
            {
                yield break;
            }

            Symbol s;
            if (isSorted)
            {
                var sorted = new Set<Term>(exe.TermIndex.LexicographicCompare);
                foreach (var dt in exe.GetDerivedTerms(goalTerm))
                {
                    s = dt.Symbol;
                    if ((s.Kind == SymbolKind.UserCnstSymb || s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb) &&
                        ((UserSymbol)s).Name.StartsWith(SymbolTable.ManglePrefix))
                    {
                        continue;
                    }

                    sorted.Add(dt);
                }

                foreach (var dt in sorted)
                {
                    yield return Factory.Instance.ToAST(dt);                   
                }
            }
            else
            {
                foreach (var dt in exe.GetDerivedTerms(goalTerm))
                {
                    s = dt.Symbol;
                    if ((s.Kind == SymbolKind.UserCnstSymb || s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb) &&
                        ((UserSymbol)s).Name.StartsWith(SymbolTable.ManglePrefix))
                    {
                        continue;
                    }

                    yield return Factory.Instance.ToAST(dt);                   
                }
            }
        }

        private bool ParseGoalWithDontCares(string t, List<Flag> flags, out Term goalTerm)
        {
            ImmutableCollection<Flag> parseFlags;
            var ast = Factory.Instance.ParseDataTerm(t, out parseFlags, facts.Index.Env.Parameters);
            flags.AddRange(parseFlags);
            if (ast == null)
            {
                goalTerm = null;
                return false;
            }
            else if (WasCancelled)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ast.Node,
                    Constants.BadSyntax.ToString("Query operation was cancelled; derivation is unknown."),
                    Constants.BadSyntax.Code));
                goalTerm = null;
                return false;
            }

            var simplified = Compiler.EliminateQuotations((Configuration)facts.Model.Node.Config.CompilerData, ast, flags);
            if (simplified.NodeKind != NodeKind.Id && simplified.NodeKind != NodeKind.Cnst && simplified.NodeKind != NodeKind.FuncTerm)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    simplified,
                    Constants.BadSyntax.ToString("Expected an identifier, constant, or function"),
                    Constants.BadSyntax.Code));
                goalTerm = null;
                return false;
            }

            goalTerm = facts.Expand(Factory.Instance.ToAST(simplified), flags);
            Contract.Assert(goalTerm == null || goalTerm.Groundness != Groundness.Type);
            return goalTerm != null;
        }
    }
}
