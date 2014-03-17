namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Nodes;

    internal class ASTComputation<T> : ASTComputationBase
    {
        private Node start;

        private Func<Node, IEnumerable<Node>> unfold;

        private Func<Node, IEnumerable<T>, T> fold;

        private Stack<ComputationState> enumState = new Stack<ComputationState>();
       
        public ASTComputation(
                            Node root, 
                            Func<Node, IEnumerable<Node>> unfold, 
                            Func<Node, IEnumerable<T>, T> fold,
                            IControlToken controlToken = null)
            : base((ControlToken)controlToken)
        {
            Contract.Requires(root != null && unfold != null && fold != null);
            start = root;
            this.unfold = unfold;                
            this.fold = fold;
        }

        public T Compute()
        {
            T result = default(T);
            enumState.Push(new ComputationState(start, unfold(start)));

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
                    enumState.Push(new ComputationState(next, unfold(next)));
                }
                else
                {
                    result = fold(top.N, top.Results);
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
            private IEnumerator<Node> enumState;

            private LinkedList<T> results = 
                new LinkedList<T>();

            public Node N
            {
                get;
                private set;
            }

            public IEnumerable<T> Results
            {
                get { return results; }
            }

            public ComputationState(Node n, IEnumerable<Node> unfold)
            {
                N = n;
                enumState = unfold != null ? unfold.GetEnumerator() : null;
            }

            public Node GetNext()
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
