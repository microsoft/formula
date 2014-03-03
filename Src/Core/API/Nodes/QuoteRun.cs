namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class QuoteRun : Node
    {
        public override int ChildCount
        {
            get { return 0; }
        }

        public string Text
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.QuoteRun; }
        }

        internal QuoteRun(Span span, string text)
            : base(span)
        {
            Text = text == null ? string.Empty : text;
            cachedHashCode = GetDetailedNodeKindHash();
        }

        private QuoteRun(QuoteRun n, bool keepCompilerData)
            : base(n.Span)
        {
            Text = n.Text;
            CompilerData = keepCompilerData ? n.CompilerData : null;
            cachedHashCode = n.cachedHashCode;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            if (attribute == AttributeKind.Text)
            {
                value = Text;
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

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.Text, Text);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            return new QuoteRun(this, keepCompilerData);
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

            return string.CompareOrdinal(((QuoteRun)n).Text, Text) == 0;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Text.GetHashCode();
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
