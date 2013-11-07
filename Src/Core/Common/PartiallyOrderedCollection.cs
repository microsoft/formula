namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Like a Comparison, except returns LiftInt.Unknown if two values are incomparable.
    /// </summary>
    delegate LiftedInt PartialOrder<T>(T a, T b);

    internal class PartiallyOrderedCollection<T>
    {
        private Comparison<T> comparison;
        private Map<T, Node> elements;

        /// <summary>
        /// The set of minima currently in the partial order.
        /// </summary>
        private Set<Node> minima;

        public PartialOrder<T> Order
        {
            get;
            private set;
        }

        public int Count
        {
            get { return elements.Count; }
        }

        public IEnumerable<T> Elements
        {
            get
            {
                foreach (var n in elements.Values)
                {
                    yield return n.Value;
                }
            }
        }

        /// <summary>
        /// The function order is a partial order on T values; its invocation is assumed to be expensive.
        /// The function comparison is only for cheaply testing equality of T values.
        /// </summary>
        public PartiallyOrderedCollection(PartialOrder<T> order, Comparison<T> comparison)
        {
            Contract.Requires(comparison != null && order != null);
            Order = order;
            this.comparison = comparison;
            elements = new Map<T, Node>(comparison);
            minima = new Set<Node>(Node.Compare);
        }

        private void Verify()
        {
            foreach (var kv in elements)
            {
                foreach (var m in kv.Value.LUBs)
                {
                    foreach (var mp in kv.Value.LUBs)
                    {
                        Contract.Assert(comparison(m.Value, mp.Value) == 0 || Order(m.Value, mp.Value) == LiftedInt.Unknown);
                    }
                }

                foreach (var m in kv.Value.GLBs)
                {
                    foreach (var mp in kv.Value.GLBs)
                    {
                        Contract.Assert(comparison(m.Value, mp.Value) == 0 || Order(m.Value, mp.Value) == LiftedInt.Unknown);
                    }
                }
            }
        }

        public void Add(T value)
        {
            if (elements.ContainsKey(value))
            {
                return;
            }

            Node n;
            LiftedInt cmp;
            bool isGLB;

            //// First construct the glbs
            var glbs = new Set<Node>(Node.Compare);
            var lubs = new Set<Node>(Node.Compare);
            var pending = new Set<Node>(Node.Compare);
            foreach (var m in minima)
            {
                cmp = Order(m.Value, value);
                if (cmp < 0 == true)
                {
                    pending.Add(m);
                }
                else if (cmp > 0 == true)
                {
                    //// If a minima m is larger than v, then m must be a lub of v
                    lubs.Add(m);
                }
            }

            while (pending.Count > 0)
            {
                n = pending.GetSomeElement();
                pending.Remove(n);

                Contract.Assert(!n.IsDownMarked);
                isGLB = true;
                foreach (var m in n.LUBs)
                {
                    cmp = Order(m.Value, value);
                    if (cmp < 0 == true)
                    {
                        isGLB = false;
                        if (!m.IsDownMarked)
                        {
                            m.MarkDownCone(pending);
                            pending.Add(m);
                        }
                    }
                }

                if (isGLB)
                {
                    glbs.Add(n);
                }
            }

            //// Second, create the new node. 
            var newNode = new Node(value, glbs, lubs, this);
            //// These lubs can only come from elements that were previously minimal.
            //// These elements are no longer minima
            foreach (var m in lubs)
            {
                m.GLBs.Add(newNode);
                minima.Remove(m);
            }

            foreach (var m in glbs)
            {
                foreach (var mp in m.LUBs)
                {
                    cmp = Order(mp.Value, value);
                    if (cmp > 0 == true)
                    {
                        lubs.Add(mp);
                        mp.GLBs.Remove(m);
                        mp.GLBs.Add(newNode);
                    }
                }

                m.LUBs.Add(newNode);
                foreach (var mp in lubs)
                {
                    m.LUBs.Remove(mp);
                }
            }

            if (newNode.GLBs.Count == 0)
            {
                minima.Add(newNode);
            }

            elements.Add(value, newNode);
            Verify();
        }

        public void Debug_Print()
        {
            Console.Write("Minima: ");
            foreach (var m in minima)
            {
                Console.Write("{0} ", m.Value);
            }

            Console.WriteLine();

            foreach (var kv in elements)
            {
                Console.WriteLine("Value: {0}", kv.Key);
                Console.Write("\t LUBs: ");
                foreach (var m in kv.Value.LUBs)
                {
                    Console.Write("{0} ", m.Value);
                }

                Console.WriteLine();
                Console.Write("\t GLBs: ");
                foreach (var m in kv.Value.GLBs)
                {
                    Console.Write("{0} ", m.Value);
                }

                Console.WriteLine();
            }
        }

        /// <summary>
        /// A node knows its current set of least upper bounds and greatest lower bounds.
        /// </summary>
        private class Node
        {            
            /// <summary>
            /// This node is down marked if downMark = Owner.Count + 1
            /// </summary>
            private int downMark;

            public Set<Node> GLBs
            {
                get;
                private set;
            }

            public Set<Node> LUBs
            {
                get;
                private set;
            }

            public PartiallyOrderedCollection<T> Owner
            {
                get;
                private set;
            }
          
            public T Value
            {
                get;
                private set;
            }

            public bool IsDownMarked
            {
                get
                {
                    return downMark == Owner.Count + 1;
                }
            }

            public Node(T value, Set<Node> glbs, Set<Node> lubs, PartiallyOrderedCollection<T> owner)
            {
                Value = value;
                Owner = owner;
                GLBs = glbs;
                LUBs = lubs;
            }

            /// <summary>
            /// Marks the downward cone of this node, but excluding this node, during the glb construction. 
            /// Removes all nodes in the downward cone from pending.
            /// 
            /// Mark is state dependent so it does not have to be reset.
            /// </summary>
            public void MarkDownCone(Set<Node> pending)
            {
                Contract.Requires(!IsDownMarked);

                foreach (var n in GLBs)
                {
                    MarkDownCone(n, pending);
                }
            }

            public static int Compare(Node a, Node b)
            {
                return a.Owner.comparison(a.Value, b.Value);
            }

            private static void MarkDownCone(Node n, Set<Node> pending)
            {
                if (n.IsDownMarked)
                {
                    return;
                }

                pending.Remove(n);
                n.downMark = n.Owner.Count + 1;
                foreach (var m in n.GLBs)
                {
                    MarkDownCone(m, pending);
                }
            }
        }
    }
}
