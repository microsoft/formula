namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public sealed class Setting : Node
    {
        public override int ChildCount
        {
            get { return 2; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Setting; }
        }

        public Id Key
        {
            get;
            private set;
        }

        public Cnst Value
        {
            get;
            private set;
        }

        internal Setting(Span span, Id key, Cnst value)
            : base(span)
        {
            Contract.Requires(key != null);
            Contract.Requires(value != null);

            Key = key;
            Value = value;
        }

        private Setting(Setting n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Setting(this);
            int occurs = 0;
            cnode.Key = CloneField<Id>(Key, replace, pos, ref occurs);
            cnode.Value = CloneField<Cnst>(Value, replace, pos, ref occurs);
            return cnode;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Setting(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Key = TakeClone<Id>(cenum);
                cnode.Value = TakeClone<Cnst>(cenum);
            }

            return cnode;
        }

        internal override bool IsLocallyEquivalent(Node n)
        {
            if (n == this)
            {
                return true;
            }

            return n.NodeKind == NodeKind;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        public override IEnumerable<Node> Children
        {
            get 
            { 
                yield return Key;
                yield return Value;
            }
        }
    }
}
