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
        ISearchStrategy CreateInstance(
                            AST<Node> module, 
                            string collectionName, 
                            string instanceName);

        /// <summary>
        /// Returns a new instance of this strategy to begin enumeration. The map initialDOFs assigns to each
        /// constructor upper and lower bounds and the number of DOFs manually introduced in the partial model.
        /// </summary>
        ISearchStrategy Begin(                            
                            Configuration config, 
                            SymbolTable table,
                            Dictionary<UserSymbol, Tuple<CardRange, uint>> initialDOFs,
                            out List<Flag> flags);

        /// <summary>
        /// Asks the strategy for a set of commands to start the next search. If GetNextCmds returns null or no commands, then 
        /// the engine tries to find another model under the current configuration.
        /// 
        /// lastAddedDOFs: Gives the total degrees-of-freedom that have been added to each type. If no DOFs were added to type T,
        /// then T does not appear in the domain of dictionary.
        /// 
        /// the total number of solutions that have been computed
        /// at this configuration (count remains after pushes).
        /// 
        /// If lastFailedCmd is non-null, then previous set of commands could not be applied. If the lastFailedCmd was a Pop, then there 
        /// was no Push to Pop. If the lastFailedCmd was Push, then adding n more DOFs to UserSymbol cannot yield any new solutions.
        /// </summary>
        IEnumerable<ISearchCommand> GetNextCmds(
                            Dictionary<UserSymbol, Cardinality> lastAddedDOfs, 
                            int nLastSolutions,
                            Tuple<ISearchCommand[], int, UserSymbol> lastFailedCmd = null);
    }
}
