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
        private Map<Term, Tuple<Term, ProofTree>> premises = new Map<Term, Tuple<Term, ProofTree>>(Term.Compare);

        public Term Conclusion
        {
            get;
            private set;
        }

        public IEnumerable<KeyValuePair<string, ProofTree>> Premises
        {
            get
            {
                foreach (var kv in premises)
                {
                    yield return new KeyValuePair<string, ProofTree>(((UserSymbol)kv.Key.Symbol).Name, kv.Value.Item2);
                }
            }
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

        internal void AddSubproof(Term boundVar, Term boundPattern, ProofTree subproof)
        {
            Contract.Requires(boundVar != null && boundPattern != null && subproof != null);
            premises.Add(boundVar, new Tuple<Term, ProofTree>(boundPattern, subproof));
        }

        /// <summary>
        /// Computes a set of locators 
        /// </summary>
        /// <returns></returns>
        public Set<Locator> ComputeLocators()
        {           
            if (CoreRule == null)
            {
                //// Then this is a fact.
                if (factSets.Count > 1 || !factSets.ContainsKey(string.Empty))
                {
                    //// Not implemented for renamed fact sets.
                    return new Set<Locator>(Locator.Compare);
                }

                Locator loc;
                var locs = new Set<Locator>(Locator.Compare);
                if (factSets[string.Empty].TryGetLocator(Conclusion, out loc))
                {
                    locs.Add(loc);
                }

                return locs;
            }
            else if (CoreRule.Kind == CoreRule.RuleKind.Sub)
            {
                throw new NotImplementedException();
            }
            else if (premises.Count == 0)
            {
                var locs = new Set<Locator>(Locator.Compare);
                locs.Add(MkFactRuleLocator(CoreRule.ProgramName == null ? Locator.UnknownProgram : CoreRule.ProgramName, CoreRule.Node, Conclusion));
                return locs;
            }
            else
            {
                var term2Locs = new Map<Term, Set<Locator>>(Term.Compare);
                var var2Bindings = new Map<Term, Term>(Term.Compare); 
                foreach (var kv in premises)
                {
                    MkSubLocators(
                        kv.Value.Item2.ComputeLocators(), 
                        kv.Value.Item2.Conclusion, 
                        kv.Key, 
                        kv.Value.Item1, 
                        term2Locs,
                        var2Bindings);
                }

                foreach (var kv in var2Bindings)
                {
                    Console.WriteLine("{0} -> {1}", kv.Key.Debug_GetSmallTermString(), kv.Value.Debug_GetSmallTermString());
                }
            }

            return null;
        }
       
        public void Debug_PrintTree()
        {
            Debug_PrintTree(0);
        }

        /// <summary>
        /// Assigns locations to subterms extracted by a bound pattern.
        /// </summary>
        private void MkSubLocators(
            Set<Locator> findLocs, 
            Term binding,
            Term boundVar, 
            Term boundPattern, 
            Map<Term, Set<Locator>> term2Locs,
            Map<Term, Term> var2Bindings)
        {
            if (findLocs == null || findLocs.Count == 0)
            {
                return;
            }

            var2Bindings[boundVar] = binding;
            Set<Locator> termLocs;
            if (!term2Locs.TryFindValue(boundVar, out termLocs))
            {
                termLocs = new Set<Locator>(Locator.Compare);
                term2Locs.Add(boundVar, termLocs);
            }

            foreach (var l in findLocs)
            {
                termLocs.Add(l);
            }

            Stack<Tuple<Term, Set<Locator>>> locStack = new Stack<Tuple<Term, Set<Locator>>>();
            locStack.Push(new Tuple<Term, Set<Locator>>(binding, findLocs));
            boundPattern.Compute<Unit>(
                (x, s) => UnfoldLocators(x, locStack, term2Locs, var2Bindings),
                (x, ch, s) =>
                {
                    locStack.Pop();
                    return default(Unit);
                });
        }

        private IEnumerable<Term> UnfoldLocators(
            Term pattern, 
            Stack<Tuple<Term, Set<Locator>>> locStack, 
            Map<Term, Set<Locator>> term2Locs,
            Map<Term, Term> var2Bindings)
        {
            var parentTerm = locStack.Peek().Item1;
            if (pattern.Symbol.IsVariable)
            {
                var2Bindings[pattern] = parentTerm;
            }

            Set<Locator> termLocs;
            if (!term2Locs.TryFindValue(pattern, out termLocs))
            {
                termLocs = new Set<Locator>(Locator.Compare);
                term2Locs.Add(pattern, termLocs);
            }

            var parentLocs = locStack.Peek().Item2;
            foreach (var l in parentLocs)
            {
                termLocs.Add(l);
            }

            for (int i = 0; i < pattern.Args.Length; ++i)
            {
                termLocs = new Set<Locator>(Locator.Compare);
                foreach (var l in parentLocs)
                {
                    termLocs.Add(l[i]);
                }

                locStack.Push(new Tuple<Term, Set<Locator>>(parentTerm.Args[i], termLocs));
                yield return pattern.Args[i];
            }
        }

        private void Debug_PrintTree(int indent)
        {
            var indentStr = new string(' ', 3 * indent); 
            Console.WriteLine("{0}{1} :- {2} ({3}, {4})", 
                indentStr, 
                Conclusion.Debug_GetSmallTermString(),
                (CoreRule == null || CoreRule.ProgramName == null) ? "?" : CoreRule.ProgramName.ToString(),
                Rule.Span.StartLine,
                Rule.Span.StartCol);

            foreach (var kv in premises)
            {
                Console.WriteLine("{0}  {1} equals ", indentStr, kv.Key.Debug_GetSmallTermString());
                kv.Value.Item2.Debug_PrintTree(indent + 1);
            }

            Console.WriteLine("{0}.", indentStr);
        }

        /// <summary>
        /// If n is node associated with a premiseless and conclusion is the result of this rule,
        /// then constructors a locator l s.t. shape(l) = shape(conclusion) using as much detail from n as possible.
        /// </summary>
        private static Locator MkFactRuleLocator(ProgramName program, Node n, Term conclusion)
        {
            //// If n is a rule, then use one of the heads.
            if (n.NodeKind == NodeKind.Rule)
            {
                n = ((Rule)n).Heads.First();
            }

            var nodeStack = new Stack<Node>();
            nodeStack.Push(n);
            int i;
            Locator thisLoc;
            return conclusion.Compute<Locator>(
                (x, s) => MkFactRuleLocatorUnfold(x, nodeStack),
                (x, ch, s) =>
                {
                    i = 0;
                    var chLocs = new Locator[x.Args.Length];
                    foreach (var l in ch)
                    {
                        chLocs[i] = l;
                        ++i;
                    }

                    thisLoc = new Locator(nodeStack.Peek().Span, program, chLocs);
                    nodeStack.Pop();
                    return thisLoc;
                });            
        }

        private static IEnumerable<Term> MkFactRuleLocatorUnfold(Term t, Stack<Node> nodeStack)
        {
            if (t.Args.Length == 0)
            {
                yield break;
            }

            var n = nodeStack.Peek();
            if (n.NodeKind == NodeKind.Rule)
            {
                n = ((Rule)n).Heads.First();
            }

            if (n.NodeKind != NodeKind.FuncTerm)
            {
                //// If n is not a funNode, then continue to associate all subterms of t with n.
                foreach (var a in t.Args)
                {
                    nodeStack.Push(n);
                    yield return a; 
                }
            }
            else
            {
                var funNode = n as FuncTerm;
                Contract.Assert(funNode.Args.Count == t.Args.Length);
                //// If n is a funNode, then associate the subterms of t with the subnodes of n.
                using (var nodeIt = funNode.Args.GetEnumerator())
                {
                    using (var tIt = t.Args.GetEnumerator())
                    {
                        while (nodeIt.MoveNext() && tIt.MoveNext())
                        {
                            nodeStack.Push(nodeIt.Current);
                            yield return tIt.Current;
                        }
                    }
                }
            }
        }
    }
}
