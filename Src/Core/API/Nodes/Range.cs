namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Microsoft.Formula.Common;

    public sealed class Range : Node
    {
        public override int ChildCount
        {
            get { return 0; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Range; }
        }

        public Rational Lower
        {
            get;
            private set;
        }

        public Rational Upper
        {
            get;
            private set;
        }

        internal Range(Span span, Rational end1, Rational end2)
            : base(span)
        {
            Contract.Requires(end1.IsInteger && end2.IsInteger);

            if (end1.CompareTo(end2) <= 0)
            {
                Lower = end1;
                Upper = end2;
            }
            else
            {
                Lower = end2;
                Upper = end1;
            }

            cachedHashCode = GetDetailedNodeKindHash();
        }

        private Range(Range n, bool keepCompilerData)
            : base(n.Span)
        {
            Lower = n.Lower;
            Upper = n.Upper;
            CompilerData = keepCompilerData ? n.CompilerData : null;
            cachedHashCode = n.cachedHashCode;
        }

        public override bool TryGetNumericAttribute(AttributeKind attribute, out Rational value)
        {
            if (attribute == AttributeKind.Lower)
            {
                value = Lower;
                return true;
            }
            else if (attribute == AttributeKind.Upper)
            {
                value = Upper;
                return true;
            }

            value = Rational.Zero;
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

            return pred.AttributePredicate(AttributeKind.Lower, Lower) &&
                   pred.AttributePredicate(AttributeKind.Upper, Upper);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            return new Range(this, keepCompilerData);
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            return this;
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

            var nn = (Range)n;
            return nn.Lower.Equals(Lower) && nn.Upper.Equals(Upper);
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Lower.GetHashCode() + Upper.GetHashCode();
            }

            return v;
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
