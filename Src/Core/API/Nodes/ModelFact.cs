namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class ModelFact : Node
    {
        public override int ChildCount
        {
            get { return (Binding == null ? 1 : 2) + (Config == null ? 0 : 1); }
        }

        public Node Match
        {
            get;
            private set;
        }

        public Id Binding
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.ModelFact; }
        }

        internal ModelFact(Span span, Id binding, Node match)
            : base(span)
        {
            Contract.Requires(match != null && match.IsFuncOrAtom);
            Binding = binding;
            Match = match;
        }

        private ModelFact(ModelFact n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new ModelFact(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Binding = Binding == null ? null : TakeClone<Id>(cenum);
                cnode.Match = TakeClone<Node>(cenum);
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new ModelFact(this);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            if (Binding != null)
            {
                cnode.Binding = CloneField<Id>(Binding, replace, pos, ref occurs);
            }

            cnode.Match = CloneField<Node>(Match, replace, pos, ref occurs);
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

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
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

                if (Binding != null)
                {
                    yield return Binding;
                }

                yield return Match;
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                var index = 0;
                if (Config != null)
                {
                    yield return new ChildInfo(Config, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                if (Binding != null)
                {
                    yield return new ChildInfo(Binding, ChildContextKind.Binding, index, 0);
                    ++index;
                }

                yield return new ChildInfo(Match, ChildContextKind.Match, index, 0);
            }
        }
    }
}
