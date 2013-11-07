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
            if (!ParseGroundGoal(t, flags, out grndTerm))
            {
                return LiftedBool.Unknown;
            }

            return exe.IsDerived(grndTerm);
        }

        /// <summary>
        /// Enumerates all values that were derived by the query operation.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<AST<Node>> EnumerateDerivations()
        {
            Symbol s;
            foreach (var kv in exe.Fixpoint)
            {
                s = kv.Key.Symbol;
                if ((s.Kind == SymbolKind.UserCnstSymb || s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb) &&
                    ((UserSymbol)s).Name.StartsWith(SymbolTable.ManglePrefix))
                {
                    continue;
                }

                yield return Factory.Instance.ToAST(kv.Key);
            }
        }

        public IEnumerable<ProofTree> EnumerateProofs(string t, out List<Flag> flags, out LiftedBool truthValue)
        {
            flags = new List<Flag>();
            Term grndTerm;
            if (!ParseGroundGoal(t, flags, out grndTerm))
            {
                truthValue = LiftedBool.Unknown;
                return new ProofTree[0];
            }
            else if (!exe.IsDerived(grndTerm))
            {
                truthValue = false;
                return new ProofTree[0];
            }
            else if (!exe.KeepDerivations)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.BadSyntax.ToString("Cannot retrieve proofs; derivations were not kept."),
                    Constants.BadSyntax.Code));
                truthValue = true;
                return new ProofTree[0];
            }

            truthValue = true;
            return exe.GetDerivations(grndTerm);
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

        private bool ParseGroundGoal(string t, List<Flag> flags, out Term grndTerm)
        {
            ImmutableCollection<Flag> parseFlags;
            var ast = Factory.Instance.ParseDataTerm(t, out parseFlags, facts.Index.Env.Parameters);
            flags.AddRange(parseFlags);
            if (ast == null)
            {
                grndTerm = null;
                return false;
            }
            else if (WasCancelled)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ast.Node,
                    Constants.BadSyntax.ToString("Query operation was cancelled; derivation is unknown."),
                    Constants.BadSyntax.Code));
                grndTerm = null;
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
                grndTerm = null;
                return false;
            }

            grndTerm = facts.Expand(Factory.Instance.ToAST(simplified), flags);
            Contract.Assert(grndTerm == null || grndTerm.Groundness == Groundness.Ground);
            return grndTerm != null;
        }
    }
}
