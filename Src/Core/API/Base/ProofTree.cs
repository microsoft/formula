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
        private const int Default_MaxLocationsPerProof = 100;

        private int maxLocationsPerProof = Default_MaxLocationsPerProof;
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

        public IEnumerable<String> RuleClasses
        {
            get
            {
                if (CoreRule == null || CoreRule.RuleClasses == null)
                {
                    yield break;
                }

                foreach (var cls in CoreRule.RuleClasses)
                {
                    yield return cls;
                }
            }
        }

        /// <summary>
        /// CoreRule is null if conclusion is a fact.
        /// </summary>
        internal CoreRule CoreRule
        {
            get;
            private set;
        }
      
        internal ProofTree(Term conclusion, CoreRule coreRule, Map<string, FactSet> factSets, RuleTable rules)
        {
            Contract.Requires(conclusion != null && factSets != null);
            this.factSets = factSets;
            Conclusion = conclusion;
            CoreRule = coreRule;
            Rule = coreRule == null ? Factory.Instance.MkId("fact", new Span(0, 0, 0, 0)).Node : coreRule.Node;
            SetMaxLocationsPerProof(rules);
        }

        internal void AddSubproof(Term boundVar, Term boundPattern, ProofTree subproof)
        {
            Contract.Requires(boundVar != null && boundPattern != null && subproof != null);
            premises.Add(boundVar, new Tuple<Term, ProofTree>(boundPattern, subproof));
        }

        public bool HasRuleClass(string cls)
        {
            if (cls == null || CoreRule == null || CoreRule.RuleClasses == null)
            {
                return false;
            }

            return CoreRule.RuleClasses.Contains(cls);
        }

        /// <summary>
        /// Computes a set of locators 
        /// </summary>
        /// <returns></returns>
        public LinkedList<Locator> ComputeLocators()
        {           
            if (CoreRule == null)
            {
                //// Then this is a fact.
                ModelFactLocator loc;
                var locs = new LinkedList<Locator>();
                //// Not implemented for renamed fact sets. Return some locator associated with some fact set.
                if (factSets.Count > 1 || 
                    !factSets.ContainsKey(string.Empty) ||
                    !factSets[string.Empty].TryGetLocator(Conclusion, out loc))
                {
                    using (var it = factSets.GetEnumerator())
                    {
                        it.MoveNext();
                        locs.AddLast(new NodeTermLocator(
                            it.Current.Value.Model.Node,
                            it.Current.Value.SourceProgram == null ? Locator.UnknownProgram : it.Current.Value.SourceProgram.Name,
                            Conclusion));
                    }
                }
                else
                {
                    locs.AddLast(loc);
                }

                return locs;
            }
            else if (premises.Count == 0)
            {
                var locs = new LinkedList<Locator>();
                locs.AddLast(new NodeTermLocator(
                    CoreRule.Node,
                    CoreRule.ProgramName == null ? Locator.UnknownProgram : CoreRule.ProgramName,
                    Conclusion));
                return locs;
            }
            else
            {
                Contract.Assert(CoreRule.Kind == CoreRule.RuleKind.Regular);
                FactSet sourceFacts;
                if (factSets.TryFindValue(string.Empty, out sourceFacts) && sourceFacts.Rules.IsSubRuleCopy(CoreRule))
                {
                    Contract.Assert(premises.Count == 1);
                    var inputProof = premises.First().Value.Item2;
                    return MkSubRuleLocators(
                        CoreRule.Node,
                        CoreRule.ProgramName == null ? Locator.UnknownProgram : CoreRule.ProgramName,
                        inputProof.Conclusion,
                        Conclusion,
                        inputProof.ComputeLocators());
                }

                //// Terms in the body for which locations are known. 
                var bodyTerm2Locs = new Map<Term, LinkedList<Locator>>(Term.Compare);
                //// The bindings of body variables implied by the binding variable and its pattern.
                var bodyVar2Bindings = new Map<Term, Term>(Term.Compare);
                //// Records to the set of variable bindings in this pass.
                var newVarBindings = new Stack<Term>();
                foreach (var kv in premises)
                {
                    MkBodyLocators(
                        kv.Value.Item2.ComputeLocators(), 
                        kv.Value.Item2.Conclusion, 
                        kv.Key, 
                        kv.Value.Item1, 
                        bodyTerm2Locs,
                        bodyVar2Bindings, 
                        newVarBindings);
                }

                if (CoreRule.AdditionalVarDefs != null)
                {
                    Set<Term> eqs;
                    while (newVarBindings.Count > 0)
                    {
                        var x = newVarBindings.Pop();
                        if (!CoreRule.AdditionalVarDefs.TryFindValue(x, out eqs) || eqs.Count == 0)
                        {
                            continue;
                        }

                        var xlocs = bodyTerm2Locs[x];
                        var xbinding = bodyVar2Bindings[x];
                        foreach (var eq in eqs)
                        {
                            MkBodyLocators(
                                xlocs,
                                xbinding,
                                x,
                                eq,
                                bodyTerm2Locs,
                                bodyVar2Bindings,
                                newVarBindings);
                        }
                    }
                }

                //// The bindings of variables or variable selectors implied by the conclusion and the head.
                //// A variable in the head may have multiple bindings due to renamings.
                var head2Bindings = new Map<Term, Set<Term>>(Term.Compare);
                MkHeadBindings(Conclusion, CoreRule.Head, head2Bindings, bodyTerm2Locs, bodyVar2Bindings);

                return MkRuleLocators(
                    NodeTermLocator.ChooseRepresentativeNode(CoreRule.Node, Conclusion),
                    CoreRule.ProgramName == null ? Locator.UnknownProgram : CoreRule.ProgramName,
                    CoreRule.Head,
                    head2Bindings,
                    bodyTerm2Locs);
            }
        }
       
        public void Debug_PrintTree()
        {
            Debug_PrintTree(0);
        }

        /// <summary>
        /// If a subrule returns output Sub(t1,...,tn) from input with inputLocations, then finds all locations
        /// of the terms t1, ..., tn.
        /// </summary>
        private LinkedList<Locator> MkSubRuleLocators(
            Node subRuleHead,
            ProgramName subRuleProgram,
            Term input, 
            Term output, 
            LinkedList<Locator> inputLocations)
        {
            var outputLocs = new LinkedList<Locator>();
            var placeStack = new LinkedList<Tuple<Term, int>>();
            placeStack.AddLast(new Tuple<Term, int>(input, -1));
            var matchCount = new MutableTuple<int>();
            matchCount.Item1 = 0;
            input.Compute<Unit>(
                (x, s) => MkSubRuleLocators_Unfold(x, output, placeStack, inputLocations, outputLocs, subRuleHead, subRuleProgram, matchCount),
                (x, ch, s) => MkSubRuleLocators_Fold(x, output, placeStack, matchCount));
            Contract.Assert(placeStack.Count == 0 && matchCount.Item1 == 0 && outputLocs.Count > 0);
            return outputLocs;
        }

        private IEnumerable<Term> MkSubRuleLocators_Unfold(
            Term t,
            Term output,
            LinkedList<Tuple<Term, int>> placeStack,
            LinkedList<Locator> inputLocations,
            LinkedList<Locator> outputLocations,
            Node subRuleHead,
            ProgramName subRuleProgram,
            MutableTuple<int> matchCount)
        {        
            ////  t may extend the match (possibly one or more times)
            if (t == output.Args[matchCount.Item1])
            {
                while (matchCount.Item1 < output.Symbol.Arity && t == output.Args[matchCount.Item1])
                {
                    ++matchCount.Item1;
                }
            }

            //// The match is complete.
            if (matchCount.Item1 >= output.Symbol.Arity)
            {
                int i = 0;
                var argLocs = new LinkedList<Locator>[output.Symbol.Arity];
                var prev = inputLocations;
                LinkedList<Locator> next;
                foreach (var place in placeStack)
                {
                    if (place.Item2 < 0)
                    {
                        next = prev;
                    }
                    else
                    {
                        next = new LinkedList<Locator>();
                        foreach (var l in prev)
                        {
                            next.AddLast(l[place.Item2]);
                        }
                    }

                    while (i < output.Symbol.Arity && output.Args[i] == place.Item1)
                    {                        
                        argLocs[i] = next;
                        ++i;
                    }

                    if (i >= output.Symbol.Arity)
                    {
                        break;
                    }

                    prev = next;
                }

                foreach (var alocs in Locator.MkPermutations(argLocs, maxLocationsPerProof))
                {
                    outputLocations.AddLast(new CompositeLocator(subRuleHead.Span, subRuleProgram, alocs));
                }

                yield break;
            }

            for (int i = 0; i < t.Symbol.Arity; ++i)
            {
                placeStack.AddLast(new Tuple<Term, int>(t.Args[i], i));
                yield return t.Args[i];
            }
        }

        private Unit MkSubRuleLocators_Fold(
            Term t,
            Term output,
            LinkedList<Tuple<Term, int>> placeStack,
            MutableTuple<int> matchCount)
        {
            placeStack.RemoveLast();
            while (matchCount.Item1 > 0 && t == output.Args[matchCount.Item1 - 1])
            {
                --matchCount.Item1;
            }

            return default(Unit);
        }

        private LinkedList<Locator> MkRuleLocators(
            Node node,
            ProgramName nodeProgram,
            Term head,
            Map<Term, Set<Term>> head2Bindings,
            Map<Term, LinkedList<Locator>> bodyTerm2Locs)
        {
            //// A subterm l of head is a "leaf" if either it is
            //// (1) in the domain of bodyTerm2Locs,
            //// (2) it is in the domain of head2Bindings,
            //// (3) it is ground.
            //// In case (1) the locators of leaf is bodyTerm2Locs[leaf]
            //// In cases (2) and (3) the locators is a NodeTermLocator of l with (a sub-node of) n.

            //// If a subterm of head is not a leaf, then its locators are composite locators 
            //// given by the products of its arguments.

            var nodeStack = new Stack<Node>();
            nodeStack.Push(node);
            return head.Compute<LinkedList<Locator>>(
                (x, s) => MkRuleLocators_Unfold(x, nodeStack, head2Bindings, bodyTerm2Locs),
                (x, ch, s) => MkRuleLocators_Fold(x, nodeStack, nodeProgram, ch, head2Bindings, bodyTerm2Locs));
        }

        private IEnumerable<Term> MkRuleLocators_Unfold(
            Term head,
            Stack<Node> nodeStack,
            Map<Term, Set<Term>> head2Bindings,
            Map<Term, LinkedList<Locator>> bodyTerm2Locs)
        {
            if (head.Symbol == head.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel))
            {
                nodeStack.Push(nodeStack.Peek());
                yield return head.Args[2];
            }
            else if (
                head2Bindings.ContainsKey(head) ||
                bodyTerm2Locs.ContainsKey(head) ||
                head.Groundness == Groundness.Ground)
            {
                yield break;
            }

            Contract.Assert(head.Symbol.IsDataConstructor);

            FuncTerm ftnode;
            var parentNode = nodeStack.Peek();
            if (parentNode.NodeKind == NodeKind.FuncTerm &&
                (ftnode = (FuncTerm)parentNode).Args.Count == head.Symbol.Arity)
            {
                int i = 0;
                foreach (var a in ftnode.Args)
                {
                    nodeStack.Push(a);
                    yield return head.Args[i];
                    ++i;
                }
            }
            else
            {
                for (int i = 0; i < head.Args.Length; ++i)
                {
                    nodeStack.Push(parentNode);
                    yield return head.Args[i];
                }
            }
        }

        private LinkedList<Locator> MkRuleLocators_Fold(
            Term head,
            Stack<Node> nodeStack,
            ProgramName nodeProgram,
            IEnumerable<LinkedList<Locator>> children,
            Map<Term, Set<Term>> head2Bindings,
            Map<Term, LinkedList<Locator>> bodyTerm2Locs)
        {
            Set<Term> terms;
            LinkedList<Locator> locs;
            var node = nodeStack.Pop();
            if (head.Symbol == head.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel))
            {
                return children.First();
            }
            else if (bodyTerm2Locs.TryFindValue(head, out locs))
            {
                return locs;
            }
            else if (head2Bindings.TryFindValue(head, out terms))
            {
                Contract.Assert(terms.Count > 0);
                locs = new LinkedList<Locator>();
                locs.AddLast(new NodeTermLocator(node, nodeProgram, terms.GetSomeElement()));
                return locs;
            }
            else if (head.Groundness == Groundness.Ground)
            {
                locs = new LinkedList<Locator>();
                locs.AddLast(new NodeTermLocator(node, nodeProgram, head));
                return locs;
            }
            else
            {
                locs = new LinkedList<Locator>();
                foreach (var childLocs in Locator.MkPermutations(children, maxLocationsPerProof))
                {
                    locs.AddLast(new CompositeLocator(node.Span, nodeProgram, childLocs));
                }

                Contract.Assert(locs.Count > 0);
                return locs;
            }
        }

        /// <summary>
        /// Assigns locations to subterms extracted by a bound pattern.
        /// </summary>
        private void MkBodyLocators(
            LinkedList<Locator> findLocs, 
            Term binding,
            Term boundVar, 
            Term boundPattern, 
            Map<Term, LinkedList<Locator>> term2Locs,
            Map<Term, Term> var2Bindings,
            Stack<Term> newVarBindings)
        {
            if (!var2Bindings.ContainsKey(boundVar))
            {
                var2Bindings[boundVar] = binding;
                newVarBindings.Push(boundVar);
            }

            LinkedList<Locator> termLocs;
            if (!term2Locs.TryFindValue(boundVar, out termLocs))
            {
                termLocs = new LinkedList<Locator>();
                term2Locs.Add(boundVar, termLocs);
            }

            foreach (var l in findLocs)
            {
                if (!termLocs.Contains(l))
                {
                    termLocs.AddLast(l);
                }
            }

            Stack<Tuple<Term, LinkedList<Locator>>> locStack = new Stack<Tuple<Term, LinkedList<Locator>>>();
            locStack.Push(new Tuple<Term, LinkedList<Locator>>(binding, findLocs));
            boundPattern.Compute<Unit>(
                (x, s) => MkBodyLocators_Unfold(x, locStack, term2Locs, var2Bindings, newVarBindings),
                (x, ch, s) =>
                {
                    locStack.Pop();
                    return default(Unit);
                });
        }

        private IEnumerable<Term> MkBodyLocators_Unfold(
            Term pattern,
            Stack<Tuple<Term, LinkedList<Locator>>> locStack,
            Map<Term, LinkedList<Locator>> term2Locs,
            Map<Term, Term> var2Bindings,
            Stack<Term> newVarBindings)
        {
            var parentTerm = locStack.Peek().Item1;
            if (pattern.Symbol.IsVariable && !var2Bindings.ContainsKey(pattern))
            {
                var2Bindings[pattern] = parentTerm;
                newVarBindings.Push(pattern);
            }

            LinkedList<Locator> termLocs;
            if (!term2Locs.TryFindValue(pattern, out termLocs))
            {
                termLocs = new LinkedList<Locator>();
                term2Locs.Add(pattern, termLocs);
            }

            var parentLocs = locStack.Peek().Item2;
            foreach (var l in parentLocs)
            {
                if (!termLocs.Contains(l))
                {
                    termLocs.AddLast(l);
                }
            }

            //// Only unfold this term further if it is constructed data.
            if (pattern.Symbol.Arity > 0 && !pattern.Symbol.IsDataConstructor)
            {
                yield break;
            }

            for (int i = 0; i < pattern.Args.Length; ++i)
            {
                termLocs = new LinkedList<Locator>();
                foreach (var l in parentLocs)
                {
                    termLocs.AddLast(l[i]);
                }

                locStack.Push(new Tuple<Term, LinkedList<Locator>>(parentTerm.Args[i], termLocs));
                yield return pattern.Args[i];
            }
        }

        private void MkHeadBindings(
            Term binding,
            Term head,
            Map<Term, Set<Term>> head2Bindings,
            Map<Term, LinkedList<Locator>> bodyTerm2Locs,
            Map<Term, Term> bodyVar2Bindings)
        {
            var bindingStack = new Stack<Term>();
            bindingStack.Push(binding);
            head.Compute<Unit>(
                (x, s) => MkHeadBindings_Unfold(x, bindingStack, head2Bindings, bodyTerm2Locs, bodyVar2Bindings),
                (x, ch, s) =>
                {
                    bindingStack.Pop();
                    return default(Unit);
                });
        }

        private IEnumerable<Term> MkHeadBindings_Unfold(
            Term pattern,
            Stack<Term> bindingStack,
            Map<Term, Set<Term>> head2Bindings,
            Map<Term, LinkedList<Locator>> bodyTerm2Locs, 
            Map<Term, Term> bodyVar2Bindings)
        {
            if (pattern.Groundness != Groundness.Variable)
            {
                yield break;
            }
            else if (pattern.Symbol == pattern.Owner.SelectorSymbol || pattern.Symbol.IsVariable)
            {
                Set<Term> bindings;
                if (!head2Bindings.TryFindValue(pattern, out bindings))
                {
                    bindings = new Set<Term>(Term.Compare);
                    head2Bindings.Add(pattern, bindings);
                }

                bindings.Add(bindingStack.Peek());

                if (pattern.Symbol == pattern.Owner.SelectorSymbol)
                {
                    var selLocs = MkHeadSelectorLocators(pattern, bodyTerm2Locs, bodyVar2Bindings);
                    if (selLocs != null)
                    {
                        LinkedList<Locator> locs;
                        if (!bodyTerm2Locs.TryFindValue(pattern, out locs))
                        {
                            bodyTerm2Locs.Add(pattern, selLocs.Item2);
                        }
                        else
                        {
                            foreach (var l in selLocs.Item2)
                            {
                                locs.AddLast(l);
                            }
                        }
                    }
                }

                yield break;
            }
            else if (pattern.Symbol == pattern.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel))
            {
                bindingStack.Push(bindingStack.Peek());
                yield return pattern.Args[2];
            }
            else
            {
                var bindingTop = bindingStack.Peek();
                for (int i = 0; i < pattern.Args.Length; ++i)
                {
                    bindingStack.Push(bindingTop.Args[i]);
                    yield return pattern.Args[i];
                }
            }
        }

        private Tuple<Term, LinkedList<Locator>> MkHeadSelectorLocators(
            Term selection, 
            Map<Term, LinkedList<Locator>> bodyTerm2Locs, 
            Map<Term, Term> bodyVar2Bindings)
        {
            if (selection.Symbol.IsVariable)
            {
                LinkedList<Locator> locs;
                if (!bodyTerm2Locs.TryFindValue(selection, out locs))
                {
                    return null;
                }

                Term binding;
                if (!bodyVar2Bindings.TryFindValue(selection, out binding))
                {
                    return null;
                }

                return new Tuple<Term, LinkedList<Locator>>(binding, locs);
            }

            Contract.Assert(selection.Symbol == selection.Owner.SelectorSymbol);
            var selectorArg = MkHeadSelectorLocators(selection.Args[0], bodyTerm2Locs, bodyVar2Bindings);
            if (selectorArg == null)
            {
                return null;
            }

            var selectorName = (string)((BaseCnstSymb)selection.Args[1].Symbol).Raw;
            int selIndex;
            bool result;
            if (selectorArg.Item1.Symbol.Kind == SymbolKind.ConSymb)
            {
                result = ((ConSymb)selectorArg.Item1.Symbol).GetLabelIndex(selectorName, out selIndex);
            }
            else
            {
                result = ((MapSymb)selectorArg.Item1.Symbol).GetLabelIndex(selectorName, out selIndex);
            }

            Contract.Assert(result);
            var selectedArg = selectorArg.Item1.Args[selIndex];
            var selectedLocs = new LinkedList<Locator>();
            foreach (var l in selectorArg.Item2)
            {
                selectedLocs.AddLast(l[selIndex]);
            }

            return new Tuple<Term, LinkedList<Locator>>(selectedArg, selectedLocs);
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

        private void SetMaxLocationsPerProof(RuleTable rules)
        {
            var source = rules.ModuleData == null ? null : rules.ModuleData.Source.AST;
            if (source == null)
            {
                return;
            }

            Configuration config = null;
            switch (source.Node.NodeKind)
            {
                case NodeKind.Model:
                    config = (Configuration)((Model)source.Node).Config.CompilerData;
                    break;
                case NodeKind.Domain:
                    config = (Configuration)((Domain)source.Node).Config.CompilerData;
                    break;
                case NodeKind.Transform:
                    config = (Configuration)((Transform)source.Node).Config.CompilerData;
                    break;
                default:
                    return;
            }

            Contract.Assert(config != null);
            Cnst maxLocs;
            if (config.TryGetSetting(Configuration.Proofs_MaxLocationsSetting, out maxLocs))
            {
                maxLocationsPerProof = (int)maxLocs.GetNumericValue().Numerator;
            }
        }
    }
}
