namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Nodes;

    internal class ASTComputationUpDown<S, T> : ASTComputationBase
    {
        private Node start;

        private Func<Node, S, IEnumerable<Tuple<Node, S>>> unfold;

        private Func<Node, S, IEnumerable<T>, T> fold;

        private Stack<ComputationState> enumState = new Stack<ComputationState>();

        public ASTComputationUpDown(
                            Node root,
                            Func<Node, S, IEnumerable<Tuple<Node, S>>> unfold,
                            Func<Node, S, IEnumerable<T>, T> fold,
                            IControlToken controlToken = null)
            : base((ControlToken)controlToken)
        {
            Contract.Requires(root != null && unfold != null && fold != null);
            start = root;
            this.unfold = unfold;
            this.fold = fold;
        }

        public T Resume()
        {
            Contract.Assert(controlToken != null);
            controlToken.Resume();

            T result = default(T);
            while (enumState.Count > 0)
            {
                var top = enumState.Peek();
                var next = top.GetNext();
                if (controlToken != null)
                {
                    controlToken.Unfolded();
                    if (controlToken.IsSuspended)
                    {
                        return default(T);
                    }
                }

                if (next != null)
                {
                    enumState.Push(new ComputationState(next.Item1, next.Item2, unfold(next.Item1, next.Item2)));
                }
                else
                {
                    result = fold(top.N, top.V, top.Results);
                    enumState.Pop();
                    if (enumState.Count > 0)
                    {
                        enumState.Peek().AddResult(result);
                    }
                }
            }

            return result;
        }

        public T Compute(S initVal)
        {
            T result = default(T);
            enumState.Push(new ComputationState(start, initVal, unfold(start, initVal)));
            while (enumState.Count > 0)
            {
                var top = enumState.Peek();
                var next = top.GetNext();
                if (controlToken != null)
                {
                    controlToken.Unfolded();
                    if (controlToken.IsSuspended)
                    {
                        return default(T);
                    }
                }

                if (next != null)
                {
                    enumState.Push(new ComputationState(next.Item1, next.Item2, unfold(next.Item1, next.Item2)));
                }
                else
                {
                    result = fold(top.N, top.V, top.Results);
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
            private IEnumerator<Tuple<Node, S>> enumState;

            private LinkedList<T> results = new LinkedList<T>();

            public Node N
            {
                get;
                private set;
            }

            public S V
            {
                get;
                private set;
            }

            public IEnumerable<T> Results
            {
                get { return results; }
            }

            public ComputationState(Node n, S v, IEnumerable<Tuple<Node, S>> unfold)
            {
                N = n;
                V = v;
                enumState = unfold != null ? unfold.GetEnumerator() : null;
            }

            public Tuple<Node, S> GetNext()
            {
                return enumState != null && enumState.MoveNext() ? enumState.Current : null;
            }

            public void AddResult(T r)
            {
                results.AddLast(r);
            }
        }
    }
}