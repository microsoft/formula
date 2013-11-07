namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Microsoft.Formula.Common;

    public sealed class Cnst : Node
    {
        private object raw;

        public override int ChildCount
        {
            get { return 0; }
        }

        public CnstKind CnstKind
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Cnst; }
        }

        internal object Raw
        {
            get { return raw; }
        }

        internal Cnst(Span span, Rational value)
            : base(span)
        {
            raw = value;
            CnstKind = CnstKind.Numeric;
            cachedHashCode = GetDetailedNodeKindHash();
        }

        internal Cnst(Span span, string value)
            : base(span)
        {
            Contract.Requires(value != null);
            raw = value;
            CnstKind = CnstKind.String;
            cachedHashCode = GetDetailedNodeKindHash();
        }

        private Cnst(Cnst c)
            : base(c.Span)
        {
            raw = c.raw;
            CnstKind = c.CnstKind;
            cachedHashCode = c.cachedHashCode;
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

            return pred.AttributePredicate(AttributeKind.Raw, Raw) &&
                   pred.AttributePredicate(AttributeKind.CnstKind, CnstKind);
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            return this;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            return new Cnst(this);
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

            var other = (Cnst)n;
            if (other.CnstKind != CnstKind)
            {
                return false;
            }

            switch (CnstKind)
            {
                case CnstKind.Numeric:
                    return ((Rational)raw).Equals((Rational)other.raw);
                case CnstKind.String:
                    return string.CompareOrdinal((string)raw, (string)other.raw) == 0;
                default:
                    throw new NotImplementedException();
            }
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += raw.GetHashCode();
            }

            return v;
        }

        public override IEnumerable<Node> Children
        {
            get { yield break; }
        }

        public override bool TryGetNumericAttribute(AttributeKind attribute, out Rational value)
        {
            if (attribute == AttributeKind.Raw && CnstKind == CnstKind.Numeric)
            {
                value = (Rational)Raw;
                return true;
            }

            value = Rational.Zero;
            return false;
        }

        public Rational GetNumericValue()
        {
            Contract.Requires(CnstKind == CnstKind.Numeric);
            return (Rational)raw;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            if (attribute == AttributeKind.Raw && CnstKind == CnstKind.String)
            {
                value = (string)Raw;
                return true;
            }

            value = null;
            return false;
        }

        public override bool TryGetKindAttribute(AttributeKind attribute, out object value)
        {
            if (attribute == AttributeKind.CnstKind)
            {
                value = CnstKind;
                return true;
            }

            value = null;
            return false;
        }

        public string GetStringValue()
        {
            Contract.Requires(CnstKind == CnstKind.String);
            return (string)raw;
        }
    }
}
