namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Threading;
    using System.Reflection;

    using API;
    using API.Nodes;
    using Common;
    using Common.Terms;
    using Common.Rules;
    using Common.Extras;

    internal class Optimizer
    {
        private enum ItemKind { Find, Compr, Constraint };
        private static readonly Set<Term> EmptySet = new Set<Term>(Term.Compare);

        private TermIndex index;
        private Set<Term> constraints;
        private Map<Term, Tuple<Term, Term>> findPatterns;

        public Optimizer(
            TermIndex index, 
            IEnumerable<Term> constraints,
            Map<Term, Tuple<Term, Term>> findPatterns)
        {
            Contract.Requires(index != null && findPatterns != null && constraints != null);
            this.index = index;
            this.findPatterns = findPatterns;
            this.constraints = new Set<Term>(Term.Compare, constraints);
        }

        public FindData[] Optimize(RuleTable rules, ConstraintSystem environment, CancellationToken cancel)
        {
            //// Step 1. Try to split the items into connected components. 
            var itemDats = new Map<Term, ItemData>(Term.Compare);
            var useLists = new Map<Term, Set<ItemData>>(Term.Compare);
            foreach (var kv in findPatterns)
            {
                itemDats.Add(kv.Key, RegisterUses(new ItemData(kv.Key, kv.Value.Item1), useLists));
            }

            foreach (var con in constraints)            
            {
                itemDats.Add(con, RegisterUses(new ItemData(con), useLists));
            }

            FindData f1, f2;
            Tuple<ItemData, LinkedList<ItemData>> partialRule;
            var components = ComputeComponents(itemDats, useLists);
            if (components.Length == 0)
            {
                return new FindData[] { rules.CompilePartialRule(default(FindData), default(FindData), new Set<Term>(Term.Compare), environment) };
            }

            var outputs = new FindData[components.Length];
            for (int i = 0; i < components.Length; ++i)
            {
                var ordering = OrderComponents(i, components[i], useLists);
                Contract.Assert(ordering != null && ordering.Count > 0);

                //// Debug_PrintOrderData(i, ordering);
                //// Console.WriteLine();

                using (var it = ordering.GetEnumerator())
                {
                    it.MoveNext();
                    partialRule = it.Current;
                    if (partialRule.Item1 != null)
                    {
                        f1 = new FindData(partialRule.Item1.Item, findPatterns[partialRule.Item1.Item].Item1, findPatterns[partialRule.Item1.Item].Item2);
                    }
                    else
                    {
                        f1 = default(FindData);
                    }

                    var constrs = new Set<Term>(Term.Compare);
                    foreach (var c in partialRule.Item2)
                    {
                        constrs.Add(c.Item);
                    }

                    if (ordering.Count == 1)
                    {
                        outputs[i] = rules.CompilePartialRule(f1, default(FindData), constrs, environment);
                        continue;
                    }

                    while (it.MoveNext())
                    {
                        partialRule = it.Current;
                        if (constrs == null)
                        {
                            constrs = new Set<Term>(Term.Compare);
                        }

                        if (partialRule.Item1 != null)
                        {
                            f2 = new FindData(partialRule.Item1.Item, findPatterns[partialRule.Item1.Item].Item1, findPatterns[partialRule.Item1.Item].Item2);
                        }
                        else
                        {
                            f2 = default(FindData);
                        }

                        foreach (var c in partialRule.Item2)
                        {
                            constrs.Add(c.Item);
                        }

                        f1 = rules.CompilePartialRule(f1, f2, constrs, environment);
                        outputs[i] = f1;
                        constrs = null;
                    }
                }
            }
            
            return outputs;
        }

        private ItemData RegisterUses(ItemData itemData, Map<Term, Set<ItemData>> useLists)
        {
            Set<ItemData> useList;
            foreach (var v in itemData.Variables)
            {
                if (!useLists.TryFindValue(v, out useList))
                {
                    useList = new Set<ItemData>(ItemData.Compare);
                    useLists.Add(v, useList);
                }

                useList.Add(itemData);
            }

            return itemData;
        }

        private Set<ItemData>[] ComputeComponents(Map<Term, ItemData> itemDats, Map<Term, Set<ItemData>> useLists)
        {
            ItemData top;
            var cmpId = -1;
            Set<ItemData> users;
            var dfsStack = new Stack<ItemData>();
            foreach (var kv in useLists)
            {
                foreach (var d in kv.Value)
                {
                    if (d.ComponentId == ItemData.NoComponentId)
                    {
                        if (dfsStack.Count == 0)
                        {
                            ++cmpId;
                        }

                        d.ComponentId = cmpId;
                        dfsStack.Push(d);
                    }
                }

                while (dfsStack.Count > 0)
                {
                    top = dfsStack.Pop();
                    foreach (var v in top.Variables)
                    {
                        users = useLists[v];
                        foreach (var d in users)
                        {
                            if (d.ComponentId == ItemData.NoComponentId)
                            {
                                d.ComponentId = cmpId;
                                dfsStack.Push(d);
                            }
                        }
                    }
                }
            }

            foreach (var kv in itemDats)
            {
                if (kv.Value.ComponentId == ItemData.NoComponentId)
                {
                    ++cmpId;
                    break;
                }
            }

            var components = new Set<ItemData>[cmpId + 1];
            for (int i = 0; i <= cmpId; ++i)
            {
                components[i] = new Set<ItemData>(ItemData.Compare);
            }
            
            foreach (var kv in itemDats)
            {
                if (kv.Value.ComponentId == ItemData.NoComponentId)
                {
                    kv.Value.ComponentId = cmpId;
                    components[cmpId].Add(kv.Value);
                }
                else
                {
                    components[kv.Value.ComponentId].Add(kv.Value);
                }
            }
            
            return components;
        }

        private LinkedList<Tuple<ItemData, LinkedList<ItemData>>> OrderComponents(int componentId, Set<ItemData> component, Map<Term, Set<ItemData>> useLists)
        {
            var cmpFinds = new LinkedList<ItemData>();
            var orderList = new LinkedList<Tuple<ItemData, LinkedList<ItemData>>>();
            foreach (var d in component)
            {
                if (d.Kind == ItemKind.Find)
                {
                    cmpFinds.AddLast(d);
                }
            }

            //// If the component has no find variables, then it corresponds to one
            //// rule with only constraints.
            if (cmpFinds.Count == 0)
            {
                var allConstraints = new LinkedList<ItemData>();
                foreach (var d in component)
                {
                    d.OrderId = 0;
                    allConstraints.AddLast(d);
                }

                orderList.AddLast(new Tuple<ItemData, LinkedList<ItemData>>(null, allConstraints));
                return orderList;
            }

            //// Remember the variables that are oriented by ground terms.
            Set<Term> grndCnstrOrnts = new Set<Term>(Term.Compare);
            foreach (var d in component)
            {
                d.AddGrndCnstrOrients(grndCnstrOrnts);
            }

            //// Otherwise greedily order the find variables.
            Set<Term> orntVars, newOrntVars;
            Set<ItemData> orntItems;
            var selVars = new Set<Term>(Term.Compare);
            var nextOrderId = 0;

            int bestFindOrientCount, findOrientCount;
            Set<Term> bestVarOrientSet;
            Set<Term> bestNewVarOrientSet;
            Set<ItemData> bestItemOrientSet;
            LinkedListNode<ItemData> bestItem;
            while (cmpFinds.Count > 0)
            {
                bestItem = null;
                bestVarOrientSet = null;
                bestFindOrientCount = -1;
                bestItemOrientSet = null;
                bestNewVarOrientSet = null;

                var n = cmpFinds.First;
                while (n != null)
                {
                    GetSelectionData(
                        componentId, 
                        n.Value, 
                        selVars, 
                        grndCnstrOrnts, 
                        useLists, 
                        out orntItems, 
                        out orntVars, 
                        out newOrntVars);

                    var m = cmpFinds.First;
                    findOrientCount = 0;
                    while (m != null)
                    {
                        if (m == n)
                        {
                            m = m.Next;
                            continue;
                        }

                        foreach (var v in m.Value.Variables)
                        {
                            if (orntVars.Contains(v))
                            {
                                ++findOrientCount;
                            }
                        }
                        
                        m = m.Next;
                    }

                    if (findOrientCount > bestFindOrientCount)
                    {
                        bestItem = n;
                        bestVarOrientSet = orntVars;
                        bestNewVarOrientSet = newOrntVars;
                        bestItemOrientSet = orntItems;
                        bestFindOrientCount = findOrientCount;
                    }
                    else if (findOrientCount == bestFindOrientCount && 
                            (bestItemOrientSet == null || orntItems.Count > bestItemOrientSet.Count))
                    {
                        bestItem = n;
                        bestVarOrientSet = orntVars;
                        bestNewVarOrientSet = newOrntVars;
                        bestItemOrientSet = orntItems;
                        bestFindOrientCount = findOrientCount;
                    }

                    n = n.Next;
                }

                Contract.Assert(bestItem != null);
                cmpFinds.Remove(bestItem);
                var orderedCons = new LinkedList<ItemData>();
                foreach (var d in bestItemOrientSet)
                {
                    if (d.Kind != ItemKind.Find)
                    {
                        d.OrderId = nextOrderId;
                        orderedCons.AddLast(d);
                    }
                }

                bestItem.Value.OrderId = nextOrderId;
                selVars = bestVarOrientSet;
                orderList.AddLast(new Tuple<ItemData, LinkedList<ItemData>>(bestItem.Value, orderedCons));
                ++nextOrderId;
            }

            return orderList;
        }

        private void GetSelectionData(
            int componentId,
            ItemData findItem, 
            Set<Term> selectedVars,             
            Set<Term> grndCnstrOrnts,
            Map<Term, Set<ItemData>> useLists,
            out Set<ItemData> orientedItems, 
            out Set<Term> orientedVars,
            out Set<Term> newOrientedVars)
        {
            Contract.Requires(findItem != null && findItem.Kind == ItemKind.Find);
            orientedItems = new Set<ItemData>(ItemData.Compare);
            newOrientedVars = new Set<Term>(Term.Compare);
            var findVar = findItem.Item;
            if (selectedVars.Contains(findVar))
            {
                //// No new items or vars will be oriented.
                orientedVars = selectedVars;
                return;
            }

            orientedVars = new Set<Term>(Term.Compare, selectedVars);
            var varStack = new Stack<Term>(grndCnstrOrnts);
            orientedVars.Add(findVar);
            varStack.Push(findVar);
            Set<ItemData> uses;
            while (varStack.Count > 0)
            {
                newOrientedVars.Add(varStack.Peek());
                uses = useLists[varStack.Pop()];
                foreach (var d in uses)
                {
                    if (d.OrderId != ItemData.NoOrderId ||
                        d.ComponentId != componentId ||
                        orientedItems.Contains(d))
                    {
                        continue;
                    }

                    if (d.IsTriggerable(orientedVars, varStack))
                    {
                        orientedItems.Add(d);
                    }
                }
            }
        }

        private void Debug_PrintOrderData(int componentId, LinkedList<Tuple<ItemData, LinkedList<ItemData>>> orderList)
        {
            Console.WriteLine("Ordering for component {0}", componentId);
            foreach (var t in orderList)
            {
                Console.Write("\t");
                if (t.Item1 != null)
                {
                    Console.Write(
                        "Find {0}[{1} : {2}]", 
                        t.Item1.Item.Debug_GetSmallTermString(),
                        findPatterns[t.Item1.Item].Item1.Debug_GetSmallTermString(),
                        findPatterns[t.Item1.Item].Item2.Debug_GetSmallTermString());
                }
                else
                {
                    Console.Write("Nothing to find");
                }

                foreach (var con in t.Item2)
                {
                    Console.Write(", {0}", con.Item.Debug_GetSmallTermString());
                }

                Console.WriteLine();
            }
        }

        private void Debug_PrintItems(IEnumerable<ItemData> dats)
        {
            foreach (var d in dats)
            {
                if (findPatterns.ContainsKey(d.Item))
                {
                    Console.WriteLine("Find {0}", d.Item.Debug_GetSmallTermString());
                }
                else
                {
                    Console.WriteLine("Constraint {0}", d.Item.Debug_GetSmallTermString());
                }

                if (d.EqData != null)
                {
                    d.EqData.Debug_Print();
                }
            }
        }

        /// <summary>
        /// Stores the data about this item.
        /// </summary>
        private class ItemData
        {
            public const int NoOrderId = -1;
            public const int NoComponentId = -1;
            private static readonly Set<Term> EmptySet = new Set<Term>(Term.Compare);

            /// <summary>
            /// The kind of item
            /// </summary>
            public ItemKind Kind
            {
                get;
                private set;
            }

            /// <summary>
            /// The item whose variable uses are recorded.
            /// </summary>
            public Term Item
            {
                get;
                private set;
            }

            /// <summary>
            /// The variables appearing in item
            /// </summary>
            public Set<Term> Variables
            {
                get;
                private set;
            }

            /// <summary>
            /// If this item represents an equality, then this is additional data about the equality.
            /// Otherwise null.
            /// </summary>
            public OrientationData EqData
            {
                get;
                private set;
            }

            /// <summary>
            /// The connected component of this item.
            /// </summary>
            public int ComponentId
            {
                get;
                set;
            }

            /// <summary>
            /// The order in which to evaluate this item.
            /// </summary>
            public int OrderId
            {
                get;
                set;
            }

            public ItemData(Term constraint)
            {
                ComponentId = NoComponentId;
                OrderId = NoOrderId;
                Kind = ItemKind.Constraint;
                Item = constraint;

                Variables = new Set<Term>(Term.Compare);
                //// A constraint can only orient variables if it is an equality.
                if (constraint.Symbol != constraint.Owner.SymbolTable.GetOpSymbol(RelKind.Eq))
                {
                    EqData = null;
                    foreach (var t in constraint.Enumerate(x => x.Groundness == Groundness.Variable ? x.Args : null))
                    {
                        if (t.Symbol.IsVariable)
                        {
                            Variables.Add(t);
                        }
                    }
                    return;
                }

                EqData = new OrientationData();
                FindOrientedVars(constraint.Args[0], Variables, EqData.LHSVars, EqData.LHSOriented);
                FindOrientedVars(constraint.Args[1], Variables, EqData.RHSVars, EqData.RHSOriented);
            }

            public ItemData(Term findVar, Term pattern)
            {
                ComponentId = NoComponentId;
                OrderId = NoOrderId;
                Item = findVar;
                Kind = ItemKind.Find;

                Variables = new Set<Term>(Term.Compare);
                Variables.Add(findVar);
                EqData = new OrientationData();
                EqData.LHSVars.Add(findVar);
                EqData.LHSOriented.Add(findVar);
                foreach (var t in pattern.Enumerate(x => x.Groundness == Groundness.Variable ? x.Args : null))
                {
                    if (t.Symbol.IsVariable)
                    {
                        EqData.RHSVars.Add(t);
                        EqData.RHSOriented.Add(t);
                        Variables.Add(t);
                    }
                }
            }

            /// <summary>
            /// W.R.T. orientations, comprehension variables are simulated as an equality whose LHS can never orient
            /// the variables on the RHS.
            /// </summary>
            public ItemData(Term comprVar, ComprehensionData comprData)
            {
                ComponentId = NoComponentId;
                Item = comprVar;
                OrderId = NoOrderId;
                Kind = ItemKind.Compr;

                Variables = new Set<Term>(Term.Compare, comprData.ReadVars.Keys);
                Variables.Add(comprVar);

                //// A compr var is oriented if its reads are oriented, but a compr var
                //// cannot orient its reads.
                EqData = new OrientationData();
                EqData.LHSVars.Add(comprVar);
                EqData.LHSOriented.Add(comprVar);
                foreach (var v in comprData.ReadVars.Keys)
                {
                    EqData.RHSVars.Add(v);
                }
            }

            /// <summary>
            /// The item is triggerable if enough of its variables are known to determine its value.
            /// If the item is triggerable, then extends orientedVars with additional oriented vars.
            /// Push any new oriented vars onto the stack.
            /// </summary>
            public bool IsTriggerable(Set<Term> orientedVars, Stack<Term> varStack)
            {
                if (EqData == null)
                {
                    return Variables.IsSubsetOf(orientedVars);
                }

                bool isOriented = false;
                Set<Term> eqOrientedVars = null;
                if (EqData.LHSVars.IsSubsetOf(orientedVars))
                {
                    isOriented = true;
                    foreach (var v in EqData.RHSVars)
                    {
                        if (orientedVars.Contains(v))
                        {
                            continue;
                        }
                        else if (EqData.RHSOriented.Contains(v))
                        {
                            eqOrientedVars = EqData.RHSOriented;
                            continue;
                        }
                        else
                        {
                            eqOrientedVars = null;
                            isOriented = false;
                            break;
                        }
                    }
                }

                if (!isOriented)
                {
                    if (EqData.RHSVars.IsSubsetOf(orientedVars))
                    {
                        isOriented = true;
                        foreach (var v in EqData.LHSVars)
                        {
                            if (orientedVars.Contains(v))
                            {
                                continue;
                            }
                            else if (EqData.LHSOriented.Contains(v))
                            {
                                eqOrientedVars = EqData.LHSOriented;
                                continue;
                            }
                            else
                            {
                                eqOrientedVars = null;
                                isOriented = false;
                                break;
                            }
                        }
                    }
                }

                if (isOriented && eqOrientedVars != null)
                {
                    foreach (var v in eqOrientedVars)
                    {
                        if (!orientedVars.Contains(v))
                        {
                            orientedVars.Add(v);
                            varStack.Push(v);
                        }
                    }
                }

                return isOriented;
            }
            
            /// <summary>
            /// Add the variables that can be oriented by a constraint, 
            /// and the orienting side does not contain variables.
            /// </summary>
            /// <param name="orntVars"></param>
            public void AddGrndCnstrOrients(Set<Term> orntVars)
            {
                if (Kind != ItemKind.Constraint || EqData == null)
                {
                    return;
                }

                if (EqData.LHSVars.Count == 0)
                {
                    orntVars.UnionWith(EqData.RHSOriented);
                }

                if (EqData.RHSVars.Count == 0)
                {
                    orntVars.UnionWith(EqData.LHSOriented);
                }
            }

            public static int Compare(ItemData d1, ItemData d2)
            {
                return Term.Compare(d1.Item, d2.Item);
            }

            private void FindOrientedVars(Term t, Set<Term> allVars, Set<Term> sideVars, Set<Term> orientedVars)
            {
                var isDataStack = new Stack<bool>();
                isDataStack.Push(true);
                t.Compute<Unit>(
                    (x, s) =>
                    {
                        if (x.Groundness != Groundness.Variable)
                        {
                            return null;
                        }

                        if (x.Symbol.IsVariable)
                        {
                            allVars.Add(x);
                            sideVars.Add(x);
                            if (isDataStack.Peek())
                            {
                                orientedVars.Add(x);
                            }

                            isDataStack.Push(isDataStack.Peek());
                            return null;
                        }
                        else if (isDataStack.Peek() && x.Symbol.IsDataConstructor)
                        {
                            isDataStack.Push(true);
                            return x.Args;
                        }
                        else
                        {
                            isDataStack.Push(false);
                            return x.Args;
                        }
                    },
                    (x, ch, s) =>
                    {
                        if (x.Groundness == Groundness.Variable)
                        {
                            isDataStack.Pop();
                        }

                        return default(Unit);
                    });
            }
        }

        /// <summary>
        /// Simulates an equality of the form: f(...) = g(...). Oriented vars are variables whose values are
        /// known once one side of the equality is known.
        /// </summary>
        private class OrientationData
        {
            /// <summary>
            /// The set of vars appearing on the LHS of the equality
            /// </summary>
            public Set<Term> LHSVars
            {
                get;
                private set;
            }

            /// <summary>
            /// The set of vars appearing on the RHS of the equality
            /// </summary>
            public Set<Term> RHSVars
            {
                get;
                private set;
            }

            /// <summary>
            /// The set of vars on the LHS that are determined upon determining the RHS.
            /// </summary>
            public Set<Term> LHSOriented
            {
                get;
                private set;
            }

            /// <summary>
            /// The set of vars on the RHS that are determined upon determining the LHS.
            /// </summary>
            public Set<Term> RHSOriented
            {
                get;
                private set;
            }

            public OrientationData()
            {
                LHSOriented = new Set<Term>(Term.Compare);
                RHSOriented = new Set<Term>(Term.Compare);
                RHSVars = new Set<Term>(Term.Compare);
                LHSVars = new Set<Term>(Term.Compare);
            }

            public void Debug_Print()
            {
                Console.Write("\tLHS vars:");
                foreach (var v in LHSVars)
                {
                    Console.Write(" {0}", v.Symbol.PrintableName);
                    if (LHSOriented.Contains(v))
                    {
                        Console.Write("*");
                    }
                }

                Console.WriteLine();
                Console.Write("\tRHS vars:");
                foreach (var v in RHSVars)
                {
                    Console.Write(" {0}", v.Symbol.PrintableName);
                    if (RHSOriented.Contains(v))
                    {
                        Console.Write("*");
                    }
                }

                Console.WriteLine();
            }
        }
    }
}
