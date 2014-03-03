namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class UnnDecl : Node
    {
        public override int ChildCount
        {
            get { return 1 + (Config == null ? 0 : 1); }
        }

        public string Name
        {
            get;
            private set;
        }

        public Node Body
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
            get { return NodeKind.UnnDecl; }
        }

        internal UnnDecl(Span span, string name, Node body)
            : base(span)
        {
            Contract.Requires(name != null);
            Contract.Requires(body != null && body.IsTypeTerm);

            Name = name;
            Body = body;
        }

        private UnnDecl(UnnDecl n, bool keepCompilerData)
            : base(n.Span)
        {
            Name = n.Name;
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            if (attribute == AttributeKind.Name)
            {
                value = Name;
                return true;
            }

            value = null;
            return false;
        }

        protected override bool EvalAtom(ASTQueries.NodePredAtom pred, ChildContextKind context, int absPos, int relPos)
        {
            if (!base.EvalAtom(pred, context, absPos, relPos))
            {
                return false;
            }

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.Name, Name);
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new UnnDecl(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Body = TakeClone<Node>(cenum);
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new UnnDecl(this, true);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Body = CloneField<Node>(Body, replace, pos, ref occurs);
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

            return ((UnnDecl)n).Name == Name;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Name.GetHashCode();
            }

            return v;
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                if (Config != null)
                {
                    yield return Config;
                }

                yield return Body; 
            }
        }
    }
}
