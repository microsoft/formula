namespace Microsoft.Formula.API.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Nodes;
    using Common;
    using Common.Terms;
    using Compiler;
    using Solver;

    public interface ISearchStrategy
    {
        /// <summary>
        /// Some settings for this plugin.
        /// </summary>
        IEnumerable<Tuple<string, CnstKind>> SuggestedSettings
        {
            get;
        }

        /// <summary>
        /// Creates an instance of this strategy. Specifies the module where the instance
        /// is attached, and the collection and instance names used to register this strategy.
        /// For example: [ collectionName.instanceName = "parser at parser.dll" ]
        /// </summary>
        ISearchStrategy CreateInstance(AST<Node> module, string collectionName, string instanceName);

        /// <summary>
        /// Returns a new instance of this strategy to begin enumeration for a specific solving task.
        /// </summary>
        ISearchStrategy Begin(ISolver solver, out List<Flag> flags);

        /// <summary>
        /// Returns the next set of dofs for search. If null, then search terminates.
        /// </summary>
        IEnumerable<KeyValuePair<UserSymbol, int>> GetNextCmd();
    }
}
