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

    /// <summary>
    /// Indexes terms by their shape. Used as pre-unification filter.
    /// </summary>
    internal class ShapeTrie
    {
        /// <summary>
        /// The root of the shape trie.
        /// </summary>
        private ShapeNode root = null;

        /// <summary>
        /// A map from terms to entries in the trie.
        /// </summary>
        private Map<Term, Entry> entryMap = new Map<Term, Entry>(Term.Compare);

        /// <summary>
        /// The kind of terms stored in this trie.
        /// </summary>
        public Term Pattern
        {
            get;
            private set;
        }

        public ShapeTrie(Term pattern)
        {
            Contract.Requires(pattern != null && pattern.Groundness != Groundness.Type);
            Pattern = pattern;
            root = CreateDontCareNodes();

            //// If there are no don't care nodes, then create a dummy node.
            if (root == null)
            {
                root = new ShapeNode();
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine("Trie {0} : {1}", Pattern.Debug_GetSmallTermString(), entryMap.Count);
            root.Debug_Print(1);
            Console.WriteLine();
        }

        /// <summary>
        /// Attempts to insert a term t.
        /// Returns true if the term t satisfies the pattern and was inserted into the index. 
        /// </summary>
        public bool Insert(Term t)
        {
            Contract.Requires(t != null && t.Groundness != Groundness.Type);
            Contract.Requires(t.Owner == Pattern.Owner);

            if (entryMap.ContainsKey(t))
            {
                //// If t was already added then done.
                return true;
            }
            else if (!Unifier.IsUnifiable(Pattern, t, false))
            {
                //// Terms t must unify with the pattern for insertion to succeed.
                //// Pattern is already standardized apart from t.
                return false;
            }

            var entry = new Entry(t);
            entryMap.Add(t, entry);
            var nodeStack = new Stack<ShapeNode>();
            nodeStack.Push(root);
            t.Compute<Unit>(
                (x, s) =>
                {
                    return Unfold(x, nodeStack.Peek().Insert(entry, x), nodeStack);
                },
                (x, ch, s) =>
                {
                    nodeStack.Pop().IncrementEntryCount();
                    return default(Unit);
                });            

            return true;
        }

        private static IEnumerable<Term> Unfold(Term t, ShapeNode[] children, Stack<ShapeNode> stack)
        {
            if (children == null)
            {
                yield break;
            }

            for (int i = 0; i < children.Length; ++i)
            {
                stack.Push(children[i]);
                yield return t.Args[i];
            }
        }

        /// <summary>
        /// Creates nodes in the trie to capture all the don't care (unbound) locations in the pattern.
        /// </summary>
        private ShapeNode CreateDontCareNodes()
        {
            int i;
            bool isExpanded;
            ShapeNode n;
            ShapeNode[] children;
            return Pattern.Compute<ShapeNode>(
                (x, s) => x.Groundness == Groundness.Variable ? x.Args : null,
                (x, ch, s) =>
                {
                    if (x.Symbol.Arity == 0)
                    {
                        if (Executer.IsUnboundPatternVariable(x))
                        {
                            return new ShapeNode(true);
                        }
                        else
                        {
                            return null;
                        }
                    }

                    Contract.Assert(x.Symbol.IsDataConstructor);
                    isExpanded = false;
                    foreach (var m in ch)
                    {
                        if (m != null)
                        {
                            isExpanded = true;
                            break;
                        }
                    }

                    if (!isExpanded)
                    {
                        return null;
                    }

                    children = new ShapeNode[x.Symbol.Arity];
                    i = 0;
                    foreach (var m in ch)
                    {
                        children[i++] = m;
                    }

                    n = new ShapeNode();
                    n.AddRefinement((UserSymbol)x.Symbol, children);
                    return n;
                });            
        }

        private Entry GetEntry(Term t)
        {
            Entry e;
            if (!entryMap.TryFindValue(t, out e))
            {
                e = new Entry(t);
                entryMap.Add(t, e);
            }

            return e;
        }
    
        private class ShapeNode
        {
            /// <summary>
            /// The set of all entries with a variable-like term in this location.
            /// Or, if this is a don't care node, then all entries satifying this path 
            /// are placed here.
            /// </summary>
            private Set<Entry> varAndDCEntries = null;

            /// <summary>
            /// Maps a ground subterm to all the entries with that subterm in this location.
            /// If domain contains a complex term, then this is the only term in the domain of the map.
            /// </summary>
            private Map<Term, Set<Entry>> projections = null;

            /// <summary>
            /// Means that there are entries with a subterm in this location constructed with UserSymbol.
            /// Maps to an array of nodes that refine the match based on the shape of this subterm.
            /// </summary>
            private Map<UserSymbol, ShapeNode[]> refinements = null;

            /// <summary>
            /// The number of entries represented by this node.
            /// </summary>
            public uint NEntries
            {
                get;
                private set;
            }

            public bool IsDontCareNode
            {
                get;
                private set;
            }

            public ShapeNode(bool isDontCareNode = false)
            {
                IsDontCareNode = isDontCareNode;
            }

            public void AddRefinement(UserSymbol s, ShapeNode[] children)
            {
                Contract.Requires(s != null && children != null);
                Contract.Assert(refinements == null || !refinements.ContainsKey(s));
                if (refinements == null)
                {
                    refinements = new Map<UserSymbol, ShapeNode[]>(Symbol.Compare);
                }

                refinements.Add(s, children);
            }

            public void IncrementEntryCount()
            {
                ++NEntries;
            }

            public void Debug_Print(int indent)            
            {
                var indentString = indent == 0 ? string.Empty : new string(' ', 3 * indent);
                Console.WriteLine();
                if (IsDontCareNode)
                {
                    Console.WriteLine("{0}** Begin shape node (Don't Care): {1}", indentString, NEntries);
                }
                else
                {
                    Console.WriteLine("{0}** Begin shape node: {1}", indentString, NEntries);
                }

                if (varAndDCEntries != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("{0}Don't care and variable-like entries: {1}", indentString, varAndDCEntries.Count);
                    Console.WriteLine("{0}{{", indentString);
                    foreach (var e in varAndDCEntries)
                    {
                        Console.WriteLine("{0}{1}", indentString, e.Term.Debug_GetSmallTermString());
                    }
                    Console.WriteLine("{0}}}", indentString);
                }

                if (projections != null)
                {
                    foreach (var kv in projections)
                    {
                        Console.WriteLine();
                        Console.WriteLine("{0}Projection {1}: {2}", indentString, kv.Key.Debug_GetSmallTermString(), kv.Value.Count);
                        Console.WriteLine("{0}{{", indentString);
                        foreach (var e in kv.Value)
                        {
                            Console.WriteLine("{0}{1}", indentString, e.Term.Debug_GetSmallTermString());
                        }
                        Console.WriteLine("{0}}}", indentString);
                    }
                }

                if (refinements != null)
                {
                    foreach (var kv in refinements)
                    {
                        Console.WriteLine();
                        for (int i = 0; i < kv.Key.Arity; ++i)
                        {
                            if (kv.Value[i] == null)
                            {
                                Console.WriteLine("{0}Refinement {1}[{2}] (Empty)", indentString, kv.Key.FullName, i);
                            }
                            else
                            {
                                Console.WriteLine("{0}Refinement {1}[{2}]", indentString, kv.Key.FullName, i);
                                kv.Value[i].Debug_Print(indent + 1);
                            }
                        }
                    }
                }

                Console.WriteLine("{0}** End shape node", indentString);
            }

            /// <summary>
            /// Inserts the entry e based on its subterm t at this node. If Insert returns a non-null
            /// array of ShapeNodes, then e is not inserted here, but inserted based on the subterms of t.
            /// </summary>
            public ShapeNode[] Insert(Entry e, Term t)
            {
                if (IsDontCareNode || IsVariableLike(t))
                {
                    if (varAndDCEntries == null)
                    {
                        varAndDCEntries = new Set<Entry>(Entry.Compare);
                    }

                    varAndDCEntries.Add(e);
                    return null;
                }

                if (CanCreateComplexProjection(t))
                {
                    AddProjection(t, e);
                    return null;
                }
                else
                {
                    RefectorComplexProjections();
                }

                if (t.Groundness == Groundness.Ground && t.Symbol.Arity == 0)
                {
                    AddProjection(t, e);
                    return null;
                }

                Contract.Assert(t.Symbol.IsDataConstructor);
                return AddRefinement((UserSymbol)t.Symbol);
            }

            /// <summary>
            /// A term is variable-like if it is a variable or it is the application of an interpreted function.
            /// </summary>
            private static bool IsVariableLike(Term t)
            {
                if (t.Symbol.Kind == SymbolKind.BaseOpSymb)
                {
                    return true;
                }
                else if (t.Symbol.IsVariable)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            /// <summary>
            /// If there is a complex projection and new entry is inserted that breaks the complex projection
            /// invariants, then this node needs to be refactored.
            /// </summary>
            private void RefectorComplexProjections()
            {
                //// Not in a complex projection state.
                Term cp = null;
                if (refinements != null || projections == null || projections.Count != 1 || (cp = projections.GetSomeKey()).Symbol.Arity == 0)
                {
                    return;
                }

                //// Otherwise, need to expand on constructor and push complex projections down into refinements.
                Term t;
                ShapeNode n;
                var children = new ShapeNode[cp.Symbol.Arity];
                var entries = projections[cp];
                for (int i = 0; i < children.Length; ++i)
                {
                    t = cp.Args[i];
                    children[i] = n = new ShapeNode();
                    foreach (var e in entries)
                    {
                        n.AddProjection(t, e);
                        ++n.NEntries;
                    }
                }

                refinements = new Map<UserSymbol, ShapeNode[]>(Symbol.Compare);
                refinements.Add((UserSymbol)cp.Symbol, children);
                projections.Remove(cp);
            }

            /// <summary>
            /// If t is a complex and ground subterm, then it can be placed in the projection map if:
            /// (1) there are no refinements of this node, 
            /// (2) all current projections are the subterm t.
            /// </summary>
            private bool CanCreateComplexProjection(Term t)
            {
                if (refinements != null)
                {
                    return false;
                }
                else if (t.Groundness != Groundness.Ground || t.Symbol.Arity == 0)
                {
                    return false;
                }
                else if (projections != null && projections.Count > 1)
                {
                    return false;
                }

                return projections == null || projections.GetSomeKey() == t;
            }

            private void AddProjection(Term t, Entry e)
            {
                if (projections == null)
                {
                    projections = new Map<Term,Set<Entry>>(Term.Compare);
                }

                Set<Entry> entries;
                if (!projections.TryFindValue(t, out entries))
                {
                    entries = new Set<Entry>(Entry.Compare);
                    projections.Add(t, entries);
                }

                entries.Add(e);
            }

            private ShapeNode[] AddRefinement(UserSymbol s)
            {
                if (refinements == null)
                {
                    refinements = new Map<UserSymbol, ShapeNode[]>(Symbol.Compare);
                }

                ShapeNode[] children;
                if (!refinements.TryFindValue(s, out children))
                {
                    children = new ShapeNode[s.Arity];
                    refinements.Add(s, children);
                }

                for (int i = 0; i < s.Arity; ++i)
                {
                    //// Some components may be null if the children were only partially expanded
                    //// to create don't care nodes.
                    if (children[i] == null)
                    {
                        children[i] = new ShapeNode();
                    }
                }

                return children;
            }
        }

        private class Entry
        {
            public Term Term
            {
                get;
                private set;
            }

            public uint Mark
            {
                get;
                private set;
            }

            public Entry PreviousMatch
            {
                get;
                private set;
            }

            public Entry NextMatch
            {
                get;
                private set;
            }

            public Entry(Term t)
            {
                Contract.Requires(t != null);
                Term = t;
            }

            public static int Compare(Entry e1, Entry e2)
            {
                return Term.Compare(e1.Term, e2.Term);
            }
        }
    }
}
