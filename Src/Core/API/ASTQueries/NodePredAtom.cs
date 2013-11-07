namespace Microsoft.Formula.API.ASTQueries
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class NodePredAtom : NodePred
    {
        public override NodePredicateKind PredicateKind
        {
            get { return NodePredicateKind.Atom; }
        }

        public NodeKind TargetKind
        {
            get;
            private set;
        }

        public ChildContextKind ChildContext
        {
            get;
            private set;
        }

        public int ChildIndexLower
        {
            get;
            private set;
        }

        public int ChildIndexUpper
        {
            get; 
            private set;
        }

        public Func<AttributeKind, object, bool> AttributePredicate
        {
            get;
            private set;
        }

        internal NodePredAtom Conjoin(NodePredAtom a)
        {
            if (PredicateKind == NodePredicateKind.False ||
                a.PredicateKind == NodePredicateKind.False)
            {
                return NodePredFactory.Instance.False;
            }

            if (TargetKind != NodeKind.AnyNodeKind &&
                a.TargetKind != NodeKind.AnyNodeKind &&
                TargetKind != a.TargetKind)
            {
                return NodePredFactory.Instance.False;
            }

            if (ChildContext != ChildContextKind.AnyChildContext &&
                a.ChildContext != ChildContextKind.AnyChildContext &&
                ChildContext != a.ChildContext)
            {
                return NodePredFactory.Instance.False;
            }

            if (ChildIndexLower < a.ChildIndexLower &&
                ChildIndexUpper < a.ChildIndexUpper)
            {
                return NodePredFactory.Instance.False;
            }

            if (a.ChildIndexLower < ChildIndexLower &&
                a.ChildIndexUpper < ChildIndexUpper)
            {
                return NodePredFactory.Instance.False;
            }

            Func<AttributeKind, object, bool> newAP;
            if (AttributePredicate != null && a.AttributePredicate != null)
            {
                var ap1 = AttributePredicate;
                var ap2 = a.AttributePredicate;
                newAP = (at, obj) => ap1(at, obj) && ap2(at, obj);
            }
            else
            {
                newAP = AttributePredicate == null ? a.AttributePredicate : AttributePredicate;
            }

            return new NodePredAtom(
                TargetKind == NodeKind.AnyNodeKind ? a.TargetKind : TargetKind,
                ChildContext == ChildContextKind.AnyChildContext ? a.ChildContext : ChildContext,
                Math.Max(ChildIndexLower, a.ChildIndexLower),
                Math.Min(ChildIndexUpper, a.ChildIndexUpper),
                newAP);
        }

        public static NodePredAtom operator &(NodePredAtom p1, NodePredAtom p2)
        {
            Contract.Requires(p1 != null && p2 != null);
            return p1.Conjoin(p2);
        }

        public static NodePredOr operator |(NodePredAtom p1, NodePredAtom p2)
        {
            Contract.Requires(p1 != null && p1.PredicateKind != NodePredicateKind.Star);
            Contract.Requires(p2 != null && p2.PredicateKind != NodePredicateKind.Star);
            return new NodePredOr(p1, p2);
        }

        internal NodePredAtom(
            NodeKind targetKind,
            ChildContextKind childContext,
            int childIndexLower,
            int childIndexUpper,
            Func<AttributeKind, object, bool> attributePredicate
            )
        {
            TargetKind = targetKind;
            ChildContext = childContext;
            ChildIndexLower = childIndexLower;
            ChildIndexUpper = childIndexUpper;
            AttributePredicate = attributePredicate;
        }

        /// <summary>
        /// Reserved for the construction of the false atom.
        /// </summary>
        protected NodePredAtom()
        {}
    }
}
