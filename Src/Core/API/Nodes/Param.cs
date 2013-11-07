namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class Param : Node
    {
        public override int ChildCount
        {
            get { return 1; }
        }

        public string Name
        {
            get;
            private set;
        }

        public Node Type
        {
            get;
            private set;
        }

        public bool IsValueParam
        {
            get { return Type.NodeKind != NodeKind.ModRef; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Param; }
        }

        internal Param(Span span, string name, Node type)
            : base(span)
        {
            Contract.Requires(type != null && type.IsParamType);
            Contract.Requires((name == null && type.NodeKind == NodeKind.ModRef) ||
                              (name != null && type.NodeKind != API.NodeKind.ModRef));
            Contract.Requires(type.NodeKind != API.NodeKind.ModRef || ((ModRef)type).Rename != null);

            Type = type;
            Name = name;
        }

        private Param(Param n)
            : base(n.Span)
        {
            Name = n.Name;
            CompilerData = n.CompilerData;
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

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Param(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Type = TakeClone<Node>(cenum);
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Param(this);
            int occurs = 0;
            cnode.Type = CloneField<Node>(Type, replace, pos, ref occurs);
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v +=  Type.NodeKind == NodeKind.ModRef ? ((ModRef)Type).Name.GetHashCode() : Name.GetHashCode();
            }

            return v;
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

            return ((Param)n).Name == Name;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield return Type;
            }
        }
    }
}
