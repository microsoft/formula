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
    public abstract class NodePred
    {
        public abstract NodePredicateKind PredicateKind
        {
            get;
        }
    }
}
