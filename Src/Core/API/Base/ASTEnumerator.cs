namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Nodes;

    internal class ASTEnumerator : IEnumerator<Node>
    {
        private Stack<IEnumerator<Node>> enumState = null;

        private Node root;

        private Node current;

        public ASTEnumerator(Node n)
        {
            Contract.Requires(n != null);
            root = n;
        }

        public void Reset()
        {
            enumState = null;
        }

        object System.Collections.IEnumerator.Current
        {
            get { return current; }
        }

        public Node Current
        {
            get { return current; }
        }

        public void Dispose()
        {
            enumState = null;
        }

        public bool MoveNext()
        {
            if (enumState == null)
            {
                enumState = new Stack<IEnumerator<Node>>();
                enumState.Push(root.Children.GetEnumerator());
                current = root;
                return true;
            }
            else
            {
                while (enumState.Count > 0)
                {
                    var top = enumState.Peek();
                    if (top.MoveNext())
                    {
                        current = top.Current;
                        enumState.Push(current.Children.GetEnumerator());
                        return true;
                    }
                    else
                    {
                        enumState.Pop();
                    }
                }

                return false;
            }
        }
    }
}
