namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Compiler;
    using Extras;
    using Terms;

    internal class Derivation
    {
        /// <summary>
        /// The Rule is null if ther derivation is due to a fact 
        /// </summary>
        public CoreRule Rule
        {
            get;
            private set;
        }

        public Term Binding1
        {
            get;
            private set;
        }

        public Term Binding2
        {
            get;
            private set;
        }

        /// <summary>
        /// Create a derivation for facts
        /// </summary>
        public Derivation(TermIndex index)
        {
            Rule = null;
            Binding1 = Binding2 = index.FalseValue;
        }

        /// <summary>
        /// Absent bindings should be given the value FALSE
        /// </summary>
        public Derivation(CoreRule rule, Term binding1, Term binding2)
        {
            Contract.Requires(rule != null && binding1 != null && binding2 != null);
            Rule = rule;
            Binding1 = binding1;
            Binding2 = binding2;
        }

        public static int Compare(Derivation d1, Derivation d2)
        {
            Contract.Requires(d1 != null && d2 != null);
            if (d1.Rule == null)
            {
                return d2.Rule == null ? 0 : -1;
            }
            else if (d2.Rule == null)
            {
                return 1;
            }

            var cmp = d1.Rule.RuleId - d2.Rule.RuleId;
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = Term.Compare(d1.Binding1, d2.Binding1);
            if (cmp != 0)
            {
                return cmp;
            }

            return Term.Compare(d1.Binding2, d2.Binding2);
        }
    }
}
