namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Enum : Node
    {
        private LinkedList<Node> elements;

        public override int ChildCount
        {
            get { return elements.Count; }
        }

        public ImmutableCollection<Node> Elements
        {
            get;
            private set;
        }

        internal Enum(Span span)
            : base(span)
        {
            elements = new LinkedList<Node>();
            Elements = new ImmutableCollection<Node>(elements);        
        }

        private Enum(Enum n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Enum(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Elements = new ImmutableCollection<Node>(TakeClones<Node>(elements.Count, cenum, out cnode.elements));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Enum(this, true);
            int occurs = 0;
            cnode.Elements = new ImmutableCollection<Node>(CloneCollection<Node>(elements, replace, pos, ref occurs, out cnode.elements));
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        internal override bool IsLocallyEquivalent(Node n)
        {
            if (n == this)
            {
                return true;
            }
            else if (n.NodeKind != NodeKind)
            {
                return false;
            }

            return ((Enum)n).elements.Count == elements.Count;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Enum; }
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                return elements;
            }
        }

        internal void AddElement(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsEnumElement);
            if (addLast)
            {
                elements.AddLast(n);
            }
            else
            {
                elements.AddFirst(n);
            }
        }        
    }
}
