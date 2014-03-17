namespace Microsoft.Formula.API.ASTQueries
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    public sealed class NodePredStar : NodePred
    {
        public override NodePredicateKind PredicateKind
        {
            get { return NodePredicateKind.Star; }
        }

        internal NodePredStar()
        {
        }
    }
}
