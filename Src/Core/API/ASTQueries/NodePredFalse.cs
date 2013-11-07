namespace Microsoft.Formula.API.ASTQueries
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public sealed class NodePredFalse : NodePredAtom
    {
        public override NodePredicateKind PredicateKind
        {
            get { return NodePredicateKind.False; }
        }

        internal NodePredFalse()
            : base()
        {
        }
    }
}
