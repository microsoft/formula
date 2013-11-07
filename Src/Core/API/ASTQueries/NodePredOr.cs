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
    public sealed class NodePredOr : NodePred
    {
        public override NodePredicateKind PredicateKind
        {
            get { return NodePredicateKind.Or; }
        }

        public NodePred Arg1
        {
            get;
            private set;
        }

        public NodePred Arg2
        {
            get;
            private set;
        }

        public static NodePredOr operator |(NodePredOr p1, NodePredAtom p2)
        {
            Contract.Requires(p1 != null && p2 != null);
            return new NodePredOr(p1, p2);
        }

        public static NodePredOr operator |(NodePredAtom p1, NodePredOr p2)
        {
            Contract.Requires(p1 != null && p2 != null);
            return new NodePredOr(p1, p2);
        }

        public static NodePredOr operator |(NodePredOr p1, NodePredOr p2)
        {
            Contract.Requires(p1 != null && p2 != null);
            return new NodePredOr(p1, p2);
        }

        internal NodePredOr(NodePred p1, NodePred p2)
        {
            Contract.Requires(p1 != null && p1.PredicateKind != NodePredicateKind.Star);
            Contract.Requires(p2 != null && p2.PredicateKind != NodePredicateKind.Star);

            Arg1 = p1;
            Arg2 = p2;
        }       
    }
}
