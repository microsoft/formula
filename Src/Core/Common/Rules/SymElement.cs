namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Compiler;
    using Extras;
    using Terms;

    //// Aliases for Z3 types to avoid clashes
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;

    /// <summary>
    /// A symbolic element is a term, possibly with symbolic constants, that exists in the LFP of the program
    /// only for those valuations where the side constraint is satisfied.
    /// </summary>
    internal class SymElement
    {
        public Term Term
        {
            get;
            private set;
        }

        public Z3BoolExpr SideConstraint
        {
            get;
            private set;
        }
    }
}
