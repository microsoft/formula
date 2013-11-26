namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Numerics;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.Nodes;
    using API.Plugins;
    using Compiler;
    using Common;
    using Common.Rules;
    using Common.Terms;
    using Common.Extras;

    public interface ISolver
    {
        /// <summary>
        /// The configuration object for the module being solved.
        /// </summary>
        Configuration Configuration { get; }

        /// <summary>
        /// The symbol table for the module being solved.
        /// </summary>
        SymbolTable SymbolTable { get; }

        /// <summary>
        /// Returns a cardinality system for reasoning about the candidate solutions.
        /// </summary>
        CardSystem Cardinalities { get; }

        /// <summary>
        /// Returns information about model finding for a vector of dofs.
        /// If a symbol is not in the enumerable, then its dof is treated as zero.
        /// </summary>
        SearchState GetState(IEnumerable<KeyValuePair<UserSymbol, int>> dofs);
    }
}
