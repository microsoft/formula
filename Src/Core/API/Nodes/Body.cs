namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Body : Node
    {
        private LinkedList<Node> constraints;

        public override int ChildCount
        {
            get { return constraints.Count; }
        }

        public ImmutableCollection<Node> Constraints
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Body; }
        }

        internal Body(Span span)
            : base(span)
        {
            constraints = new LinkedList<Node>();
            Constraints = new ImmutableCollection<Node>(constraints);
        }

        private Body(Body n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Body(this, true);
            int occurs = 0;
            cnode.Constraints = new ImmutableCollection<Node>(CloneCollection<Node>(constraints, replace, pos, ref occurs, out cnode.constraints));
            return cnode;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Body(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Constraints = new ImmutableCollection<Node>(TakeClones<Node>(constraints.Count, cenum, out cnode.constraints));
            }

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

            return ((Body)n).constraints.Count == constraints.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        internal void AddConstr(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsConstraint);
            if (addLast)
            {
                constraints.AddLast(n);
            }
            else
            {
                constraints.AddFirst(n);
            }
        }

        public override IEnumerable<Node> Children
        {
	        get { return constraints; }
        }
    }
}
