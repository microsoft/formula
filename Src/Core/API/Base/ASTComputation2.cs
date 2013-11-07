namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Nodes;
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    internal class ASTComputation2<T> : ASTComputationBase
    {
        private Node startA;

        private Node startB;

        private Func<Node, Node, Tuple<IEnumerable<Node>, IEnumerable<Node>>> unfold;

        private Func<Node, Node, IEnumerable<T>, T> fold;

        private Stack<ComputationState> enumState = new Stack<ComputationState>();
       
        public ASTComputation2(
                            Node rootA,
                            Node rootB,
                            Func<Node, Node, Tuple<IEnumerable<Node>, IEnumerable<Node>>> unfold,
                            Func<Node, Node, IEnumerable<T>, T> fold,
                            IControlToken controlToken = null)
            : base((ControlToken)controlToken)
        {
            Contract.Requires(rootA != null && rootB != null);
            Contract.Requires(unfold != null && fold != null);
            startA = rootA;
            startB = rootB;
            this.unfold = unfold;
            this.fold = fold;
        }

        public T Compute()
        {
            T result = default(T);
            enumState.Push(new ComputationState(startA, startB, unfold(startA, startB)));
            Node nextA, nextB;
            bool next;
            while (enumState.Count > 0)
            {
                var top = enumState.Peek();
                next = top.GetNext(out nextA, out nextB);
                if (controlToken != null)
                {
                    controlToken.Unfolded();
                    if (controlToken.IsSuspended)
                    {
                        return default(T);
                    }
                }

                if (next)
                {
                    enumState.Push(new ComputationState(nextA, nextB, unfold(nextA, nextB)));
                }
                else
                {
                    result = fold(top.NA, top.NB, top.Results);
                    enumState.Pop();
                    if (enumState.Count > 0)
                    {
                        enumState.Peek().AddResult(result);
                    }
                }
            }

            return result;
        }

        private class ComputationState
        {
            private IEnumerator<Node> enumStateA;

            private IEnumerator<Node> enumStateB;

            private LinkedList<T> results = new LinkedList<T>();

            public Node NA
            {
                get;
                private set;
            }

            public Node NB
            {
                get;
                private set;
            }

            public IEnumerable<T> Results
            {
                get { return results; }
            }

            public ComputationState(
                    Node nA,
                    Node nB,
                    Tuple<IEnumerable<Node>, IEnumerable<Node>> unfold)
            {
                NA = nA;
                NB = nB;

                if (unfold == null)
                {
                    enumStateA = null;
                    enumStateB = null;
                }
                else
                {
                    enumStateA = unfold.Item1 != null ? unfold.Item1.GetEnumerator() : null;
                    enumStateB = unfold.Item2 != null ? unfold.Item2.GetEnumerator() : null;
                }
            }

            public bool GetNext(out Node nextA, out Node nextB)
            {
                if (enumStateA != null && enumStateA.MoveNext())
                {
                    nextA = enumStateA.Current;
                }
                else
                {
                    nextA = null;
                }

                if (enumStateB != null && enumStateB.MoveNext())
                {
                    nextB = enumStateB.Current;
                }
                else
                {
                    nextB = null;
                }

                return nextA != null || nextB != null;
            }

            public void AddResult(T r)
            {
                results.AddLast(r);
            }
        }
    }
}
