namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
   
    public sealed class ProofTree
    {
        private Map<string, ProofTree> premises = new Map<string, ProofTree>(string.CompareOrdinal);

        public Term Conclusion
        {
            get;
            private set;
        }

        public IEnumerable<KeyValuePair<string, ProofTree>> Premises
        {
            get { return premises; }
        }

        public Node Rule
        {
            get;
            private set;
        }

        internal ProofTree(Term conclusion, Node rule)
        {
            Contract.Requires(conclusion != null && rule != null);
            Conclusion = conclusion;
            Rule = rule;
        }

        internal void AddSubproof(string id, ProofTree subproof)
        {
            Contract.Requires(!string.IsNullOrEmpty(id));
            premises.Add(id, subproof);
        }

        public void Debug_PrintTree()
        {
            Debug_PrintTree(0);
        }

        private void Debug_PrintTree(int indent)
        {
            var indentStr = new string(' ', 3 * indent); 
            Console.WriteLine("{0}{1} :- ({2}, {3})", 
                indentStr, 
                Conclusion.Debug_GetSmallTermString(),
                Rule.Span.StartLine,
                Rule.Span.StartCol);

            foreach (var kv in premises)
            {
                Console.WriteLine("{0}  {1} equals ", indentStr, kv.Key);
                kv.Value.Debug_PrintTree(indent + 1);
            }

            Console.WriteLine("{0}.", indentStr);
        }
    }
}
