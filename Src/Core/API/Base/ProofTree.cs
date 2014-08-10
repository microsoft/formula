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
    using Common.Rules;
    using Common.Terms;
    using Compiler;
   
    public sealed class ProofTree
    {
        private Map<string, FactSet> factSets; 
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

        /// <summary>
        /// CoreRule is null if conclusion is a fact.
        /// </summary>
        internal CoreRule CoreRule
        {
            get;
            private set;
        }
      
        internal ProofTree(Term conclusion, CoreRule coreRule, Map<string, FactSet> factSets)
        {
            Contract.Requires(conclusion != null && factSets != null);
            this.factSets = factSets;
            Conclusion = conclusion;
            CoreRule = coreRule;
            Rule = coreRule == null ? Factory.Instance.MkId("fact", new Span(0, 0, 0, 0)).Node : coreRule.Node;
        }

        internal void AddSubproof(string id, ProofTree subproof)
        {
            Contract.Requires(!string.IsNullOrEmpty(id));
            premises.Add(id, subproof);
        }

        public void GetLocator()
        {
            GetLocator(this);
        }

        private static void GetLocator(ProofTree tree)
        {
            if (tree.CoreRule == null)
            {
                FactSet facts;
                if (!tree.factSets.TryFindValue(string.Empty, out facts))
                {
                    return;
                }

                Locator loc;
                if (facts.TryGetLocator(tree.Conclusion, out loc))
                {
                    Console.WriteLine("Found locator for term:");
                    Console.WriteLine(tree.Conclusion.Debug_GetSmallTermString());
                    Console.WriteLine(
                        "{0} ({1}, {2}): Arity {3}",
                        loc.Program,
                        loc.Span.StartLine,
                        loc.Span.StartCol,
                        loc.Arity);
                    for (int i = 0; i < loc.Arity; ++i)
                    {
                        Console.WriteLine(
                            "   [{4}]: {0} ({1}, {2}): Arity {3}",
                            loc[i].Program,
                            loc[i].Span.StartLine,
                            loc[i].Span.StartCol,
                            loc[i].Arity,
                            i);
                    }
                }

                return;
            }

            foreach (var prem in tree.premises.Values)
            {
                GetLocator(prem);
            }
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
