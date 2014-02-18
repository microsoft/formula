namespace Microsoft.Formula.Solver
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
    using Common;
    using Common.Extras;
    using Common.Rules;
    using Common.Terms;
    using Compiler;

    internal class Activation
    {
        public CoreRule Rule
        {
            get;
            private set;
        }

        public SymElement Binding1
        {
            get;
            private set;
        }

        public SymElement Binding2
        {
            get;
            private set;
        }

        public Activation(CoreRule rule)
        {
            Rule = rule;
            Binding1 = Binding2 = null;
        }

        public Activation(CoreRule rule, SymElement binding)
        {
            Rule = rule;
            Binding1 = binding;
            Binding2 = null;
        }

        public Activation(CoreRule rule, SymElement binding1, SymElement binding2)
        {
            Rule = rule;
            Binding1 = binding1;
            Binding2 = binding2;
        }

        public static int Compare(Activation a1, Activation a2)
        {
            var cmp = SymElement.Compare(a1.Binding1, a2.Binding1);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = SymElement.Compare(a1.Binding2, a2.Binding2);
            if (cmp != 0)
            {
                return cmp;
            }

            return a1.Rule.RuleId - a2.Rule.RuleId;
        }
    }
}
