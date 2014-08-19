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

        public IEnumerable<AST<Node>> EnumerateDerivations(string t, out List<Flag> flags, bool isSorted = false)
        {
            flags = new List<Flag>();
            return EnumerateDerivationsUsingFlags(t, flags, isSorted);
        }

        internal void Start()
        {
            Contract.Assert(exe == null);
            exe = new Executer(facts, stats, KeepDerivations);
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
            Term goalTerm;
            if (!ParseGoalWithDontCares(t, flags, out goalTerm))
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
