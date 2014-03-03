namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    public sealed class RelConstr : Node
    {
        public override int ChildCount
        {
            get { return Arg2 == null ? 1 : 2; }
        }

        public RelKind Op
        {
            get;
            private set;
        }

        public Node Arg1
        {
            get;
            private set;
        }

        public Node Arg2
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.RelConstr; }
        }

        internal RelConstr(Span span, RelKind op, Node arg1, Node arg2)
            : base(span)
        {
            Contract.Requires(arg1 != null && arg2 != null);
            Contract.Requires(arg1.IsFuncOrAtom && arg2.IsFuncOrAtom);             

            Op = op;
            Arg1 = arg1;
            Arg2 = arg2;
        }

        internal RelConstr(Span span, RelKind op, Node arg)
            : base(span)
        {
            Contract.Requires(arg != null);
            Contract.Requires(arg.IsFuncOrAtom);

            Op = op;
            Arg1 = arg;
            Arg2 = null;
        }

        private RelConstr(RelConstr n, bool keepCompilerData)
            : base(n.Span)
        {
            Op = n.Op;
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        public override bool TryGetKindAttribute(AttributeKind attribute, out object value)
        {
            if (attribute == AttributeKind.Op)
            {
                value = Op;
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

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.Op, Op);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new RelConstr(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Arg1 = TakeClone<Node>(cenum);

                if (Arg2 != null)
                {
                    cnode.Arg2 = TakeClone<Node>(cenum);
                }
                else
                {
                    cnode.Arg2 = null;
                }
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new RelConstr(this, true);
            int occurs = 0;
            cnode.Arg1 = CloneField<Node>(Arg1, replace, pos, ref occurs);

            if (Arg2 != null)
            {
                cnode.Arg2 = CloneField<Node>(Arg2, replace, pos, ref occurs);
            }
            else
            {
                cnode.Arg2 = null;
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

            return ((RelConstr)n).Op == Op;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind + (int)Op;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield return Arg1;
                if (Arg2 != null)
                {
                    yield return Arg2;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                yield return new ChildInfo(Arg1, ChildContextKind.Args, 0, 0);

                if (Arg2 != null)
                {
                    yield return new ChildInfo(Arg2, ChildContextKind.Args, 1, 1);
                }
            }
        }
    }
}
