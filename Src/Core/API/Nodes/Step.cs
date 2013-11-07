namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Step : Node
    {
        private LinkedList<Id> lhs;

        public override int ChildCount
        {
            get { return 1 + lhs.Count + (Config == null ? 0 : 1); }
        }

        public Config Config
        {
            get;
            private set;
        }

        public ImmutableCollection<Id> Lhs
        {
            get;
            private set;
        }

        public ModApply Rhs
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Step; }
        }

        internal Step(Span span, ModApply rhs)
            : base(span)
        {
            Contract.Requires(rhs != null);
            Rhs = rhs;
            lhs = new LinkedList<Id>();
            Lhs = new ImmutableCollection<Id>(lhs);
        }

        /// <summary>
        /// DO NOT use directly: Only called by parser. 
        /// </summary>
        internal Step(Span span)
            : base(span)
        {
            lhs = new LinkedList<Id>();
            Lhs = new ImmutableCollection<Id>(lhs);
            Rhs = new ModApply(span, new ModRef(span, "?", null, null));
        }

        private Step(Step n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Step(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Lhs = new ImmutableCollection<Id>(TakeClones<Id>(lhs.Count, cenum, out cnode.lhs));
                cnode.Rhs = TakeClone<ModApply>(cenum);
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Step(this);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Lhs = new ImmutableCollection<Id>(CloneCollection<Id>(lhs, replace, pos, ref occurs, out cnode.lhs));
            cnode.Rhs = CloneField<ModApply>(Rhs, replace, pos, ref occurs);
            return cnode;
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
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

            return ((Step)n).lhs.Count == lhs.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var id in Lhs)
                {
                    yield return id;
                }

                yield return Rhs;
            }
        }

        internal void AddLhs(Id id, bool addLast = true)
        {
            Contract.Requires(id != null);
            if (addLast)
            {
                lhs.AddLast(id);
            }
            else
            {
                lhs.AddFirst(id);
            }
        }

        /// <summary>
        /// DO NOT use directly. Only called by parser
        /// </summary>
        /// <param name="modapp"></param>
        internal void SetRhs(ModApply modApp)
        {
            Rhs = modApp;
        }
    }
}
