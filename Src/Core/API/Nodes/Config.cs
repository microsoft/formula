namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Config : Node
    {
        private LinkedList<Setting> settings;

        public override int ChildCount
        {
            get { return settings.Count; }
        }

        public ImmutableCollection<Setting> Settings
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Config; }
        }

        internal Config(Span span)
            : base(span)
        {
            settings = new LinkedList<Setting>();
            Settings = new ImmutableCollection<Setting>(settings);
        }

        private Config(Config n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Config(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Settings = new ImmutableCollection<Setting>(TakeClones<Setting>(settings.Count, cenum, out cnode.settings));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Config(this, true);
            int occurs = 0;
            cnode.Settings = new ImmutableCollection<Setting>(CloneCollection<Setting>(settings, replace, pos, ref occurs, out cnode.settings));
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

            return ((Config)n).settings.Count == settings.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        internal void AddSetting(Setting setting, bool addLast = true)
        {
            Contract.Requires(setting != null);
            if (addLast)
            {
                settings.AddLast(setting);
            }
            else
            {
                settings.AddFirst(setting);
            }
        }

        public override IEnumerable<Node> Children
        {
            get { return settings; }
        }
    }
}
