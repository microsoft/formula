namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Compiler;
    using Extras;
    using Terms;

    public sealed class ExecuterStatistics
    {
        private Map<int, RuleStatistics> ruleStats = 
            new Map<int, RuleStatistics>((x, y) => x - y);

        public IEnumerable<RuleStatistics> Rules
        {
            get { return ruleStats.Values; }
        }

        internal void Triggered(CoreRule rule, bool isNovelConclusion)
        {
            Contract.Requires(rule.Node != null && rule.Node.NodeKind == NodeKind.Rule);
            RuleStatistics ruleStat;
            if (!ruleStats.TryFindValue(rule.RuleId, out ruleStat))
            {
                ruleStat = new RuleStatistics((Rule)rule.Node);
                ruleStats.Add(rule.RuleId, ruleStat);
            }

            ruleStat.Triggered(isNovelConclusion);
        }

        public class RuleStatistics
        {
            public Rule Rule
            {
                get;
                private set;
            }

            public int NumTriggers
            {
                get;
                private set;
            }

            public int NumConclusions
            {
                get;
                private set;
            }

            public RuleStatistics(Rule rule)
            {
                Contract.Requires(rule != null);
                Rule = rule;
            }

            internal void Triggered(bool isNovelConclusion)
            {
                ++NumTriggers;
                if (isNovelConclusion)
                {
                    ++NumConclusions;
                }
            }
        }
    }
}
