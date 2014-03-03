namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Union : Node
    {
        private LinkedList<Node> components;

        public override int ChildCount
        {
            get { return components.Count; }
        }

        public ImmutableCollection<Node> Components
        {
            get;
            private set;
        }

        internal Union(Span span)
            : base(span)
        {
            components = new LinkedList<Node>();
            Components = new ImmutableCollection<Node>(components);
        }

        private Union(Union n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Union(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Components = new ImmutableCollection<Node>(TakeClones<Node>(components.Count, cenum, out cnode.components));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Union(this, true);
            int occurs = 0;
            cnode.Components = new ImmutableCollection<Node>(CloneCollection<Node>(components, replace, pos, ref occurs, out cnode.components));
            return cnode;
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

            return ((Union)n).components.Count == components.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Union; }
        }

        public override IEnumerable<Node> Children
        {
            get { return components; }
        }

        internal void AddComponent(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsUnionComponent);
            if (addLast)
            {
                components.AddLast(n);
            }
            else
            {
                components.AddFirst(n);
            }
        }
    }
}