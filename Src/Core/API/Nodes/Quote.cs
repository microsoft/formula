namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Quote : Node
    {
        private LinkedList<Node> contents;

        public override int ChildCount
        {
            get { return contents.Count; }
        }

        public ImmutableCollection<Node> Contents
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Quote; }        
        }

        internal Quote(Span span)
            : base(span)
        {
            contents = new LinkedList<Node>();
            Contents = new ImmutableCollection<Node>(contents);
        }

        private Quote(Quote n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Quote(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Contents = new ImmutableCollection<Node>(TakeClones<Node>(contents.Count, cenum, out cnode.contents));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Quote(this);
            int occurs = 0;
            cnode.Contents = new ImmutableCollection<Node>(CloneCollection<Node>(contents, replace, pos, ref occurs, out cnode.contents));
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

            return ((Quote)n).contents.Count == contents.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        internal void AddItem(Node item, bool addLast = true)
        {
            Contract.Requires(item != null);
            Contract.Requires(item.IsQuoteItem);
            if (addLast)
            {
                contents.AddLast(item);
            }
            else
            {
                contents.AddFirst(item);
            }
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                foreach (var n in contents)
                {
                    yield return n;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                int index = 0;
                foreach (var n in contents)
                {
                    yield return new ChildInfo(n, ChildContextKind.Args, index, index);
                    ++index;
                }
            }
        }
    }
}
