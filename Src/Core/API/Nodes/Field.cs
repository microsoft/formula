namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class Field : Node
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

        public bool IsAny
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Field; }
        }

        internal Field(Span span, string name, Node type, bool isAny)
            : base(span)
        {
            Contract.Requires(type != null && type.IsTypeTerm);
            Name = name;
            Type = type;
            IsAny = isAny;
        }

        private Field(Field n, bool keepCompilerData)
            : base(n.Span)
        {
            Name = n.Name;
            IsAny = n.IsAny;
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

        public override bool TryGetBooleanAttribute(AttributeKind attribute, out bool value)
        {
            if (attribute == AttributeKind.IsAny)
            {
                value = IsAny;
                return true;
            }

            value = false;
            return false;
        }

        protected override bool EvalAtom(ASTQueries.NodePredAtom pred, ChildContextKind context, int absPos, int relPos)
        {
            if (!base.EvalAtom(pred, context, absPos, relPos))
            {
                return false;
            }

            if (pred.AttributePredicate == null)
            {
                return true;
            }

            return pred.AttributePredicate(AttributeKind.Name, Name) &&
                   pred.AttributePredicate(AttributeKind.IsAny, IsAny);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Field(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Type = TakeClone<Node>(cenum);
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Field(this, true);
            int occurs = 0;
            cnode.Type = CloneField<Node>(Type, replace, pos, ref occurs);
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

            var nn = (Field)n;
            return nn.Name == Name &&
                   nn.IsAny == IsAny;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += IsAny.GetHashCode() + 
                    (Name == null ? 0 : Name.GetHashCode());
            }

            return v;
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
