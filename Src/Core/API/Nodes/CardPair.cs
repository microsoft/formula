namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Microsoft.Formula.Common;

    public sealed class CardPair : Node
    {
        public override NodeKind NodeKind
        {
            get { return NodeKind.CardPair; }
        }

        public override int ChildCount
        {
            get { return 1; }
        }

        public Id TypeId
        {
            get;
            private set;
        }

        public int Cardinality
        {
            get;
            private set;
        }

        internal CardPair(Span span, Id typeId, int cardinality)
            : base(span)
        {
            Contract.Requires(typeId != null);
            Contract.Requires(cardinality >= 0);
            TypeId = typeId;
            Cardinality = cardinality;
        }

        private CardPair(CardPair n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
            Cardinality = n.Cardinality;
        }

        protected override bool EvalAtom(ASTQueries.NodePredAtom pred, ChildContextKind context, int absPos, int relPos)
        {
            if (!base.EvalAtom(pred, context, absPos, relPos))
            {
                return false;
            }

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.Cardinality, Cardinality);
        }

        public override bool TryGetNumericAttribute(AttributeKind attribute, out Rational value)
        {
            if (attribute == AttributeKind.Cardinality)
            {
                value = new Rational(Cardinality);
                return true;
            }

            value = Rational.Zero;
            return false;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new CardPair(this);
            int occurs = 0;
            cnode.TypeId = CloneField<Id>(TypeId, replace, pos, ref occurs);
            return cnode;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new CardPair(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.TypeId = TakeClone<Id>(cenum);
            }

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

            return ((CardPair)n).Cardinality == Cardinality;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind + Cardinality;
        }

        public override IEnumerable<Node> Children
        {
            get { yield return TypeId; }
        }
    }
}
