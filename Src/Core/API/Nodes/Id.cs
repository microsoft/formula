namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Id : Node
    {
        private static readonly char[] splitChars = new char[] { '.' };

        public override int ChildCount
        {
            get { return 0; }
        }

        public string Name
        {
            get;
            private set;
        }

        public ImmutableArray<string> Fragments
        {
            get;
            private set;
        }

        public bool IsQualified
        {
            get { return Fragments.Length > 1; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Id; }
        }

        internal Id(Span span, string name)
            : base(span)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            Name = name;
            cachedHashCode = GetDetailedNodeKindHash();
            Fragments = new ImmutableArray<string>(Name.Split(splitChars, StringSplitOptions.None));
        }

        private Id(Id node)
            : base(node.Span)
        {
            Name = node.Name;
            cachedHashCode = node.cachedHashCode;
            Fragments = new ImmutableArray<string>(Name.Split(splitChars, StringSplitOptions.None));
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

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            return new Id(this);
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
                v += Name.GetHashCode();
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

            return ((Id)n).Name == Name;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield break;
            }
        }

        public Id Unqualify()
        {
            if (Fragments.Length == 1)
            {
                return this;
            }

            var subStr = Fragments[1];
            for (int i = 2; i < Fragments.Length; ++i)
            {
                subStr += "." + Fragments[i];
            }

            return new Id(Span, subStr);
        }
    }
}
