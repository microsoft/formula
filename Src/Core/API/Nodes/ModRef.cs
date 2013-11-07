namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class ModRef : Node
    {
        public override int ChildCount
        {
            get { return 0; }
        }

        public string Name
        {
            get;
            private set;
        }

        public string Rename
        {
            get;
            private set;
        }

        public string Location
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return Formula.API.NodeKind.ModRef; }
        }

        internal ModRef(Span span, string name, string rename, string loc)
            : base(span)
        {
            Contract.Requires(name != null);
            Name = name;
            Rename = rename;
            Location = loc;
            cachedHashCode = GetDetailedNodeKindHash();
        }

        private ModRef(ModRef n)
            : base(n.Span)
        {
            Name = n.Name;
            Rename = n.Rename;
            Location = n.Location;
            cachedHashCode = n.cachedHashCode;
            CompilerData = n.CompilerData;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            switch (attribute)
            {
                case AttributeKind.Name:
                    value = Name;
                    return true;
                case AttributeKind.Rename:
                    value = Rename;
                    return true;
                case AttributeKind.Location:
                    value = Location;
                    return true;
                default:
                    value = null;
                    return false;
            }
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
                   pred.AttributePredicate(AttributeKind.Rename, Rename) &&
                   pred.AttributePredicate(AttributeKind.Location, Location);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            return new ModRef(this);
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            return this;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += (Rename == null ? 0 : Rename.GetHashCode()) +
                     (Location == null ? 0 : Location.GetHashCode()) +
                     Name.GetHashCode();
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

            var nn = (ModRef)n;
            return nn.Name == Name &&
                   nn.Rename == Rename &&
                   nn.Location == Location;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield break;
            }
        }
    }
}
