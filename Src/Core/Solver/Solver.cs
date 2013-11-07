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

    //// Aliases for Z3 types to avoid clashes
    using Z3Context = Microsoft.Z3.Context;
    using Z3Solver = Microsoft.Z3.Solver;
    using Z3Sort = Microsoft.Z3.Sort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3RealExpr = Microsoft.Z3.RealExpr;
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3RatNum = Microsoft.Z3.RatNum;

    internal class Solver : IDisposable
    {
        private CancellationToken cancel;
        private bool disposed = false;
        private List<Flag> solverFlags = new List<Flag>();

        public IEnumerable<Flag> Flags
        {
            get { return solverFlags; }
        }

        private FactSet PartialModel
        {
            get;
            set;
        }

        private Model Source
        {
            get;
            set;
        }

        private Z3Context Context
        {
            get;
            set;
        }

        private Z3Solver Z3Solver
        {
            get;
            set;
        }

        private TypeEmbedder TypeEmbedder
        {
            get;
            set;
        }

        private CardSystem CardSystem
        {
            get;
            set;
        }

        /// <summary>
        /// The search strategy. Can be null if goal decided as Unsat prior to
        /// instantiation, or if Strategy.Begin(...) failed.
        /// </summary>
        private ISearchStrategy Strategy
        {
            get;
            set;
        }

        public Solver(FactSet partialModel, Model source, CancellationToken cancel)
        {
            Contract.Requires(partialModel != null);
            Contract.Requires(source != null);
            this.cancel = cancel;

            Source = source;
            PartialModel = partialModel;

            //// Step 1. Create Z3 Context and Solver
            CreateContextAndSolver();

            //// Step 2. Create type embedder
            TypeEmbedder = new TypeEmbedder(partialModel.Index, Context);

            //// Step 3. Create cardinality system.
            CardSystem = new CardSystem(partialModel);

            //// Step 4. Try to create the search strategy.
            if (!CardSystem.IsUnsat)
            {
               Strategy = CreateStrategy(solverFlags);
            }
        }

        /// <summary>
        /// Returns false is no more models can be enumerated.
        /// </summary>
        public bool GetModel()
        {
            if (Strategy == null)
            {
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Create and set the solver. Will get more complicated as 
        /// params become clear.
        /// </summary>
        private void CreateContextAndSolver()
        {
            Context = new Z3Context();
            Z3Solver = Context.MkSolver();
        }

        /// <summary>
        /// Creates and sets the search strategy.
        /// </summary>
        private ISearchStrategy CreateStrategy(List<Flag> flags)
        {
            Contract.Requires(!CardSystem.IsUnsat);

            var conf = (Configuration)Source.Config.CompilerData;
            ISearchStrategy strategy;
            Cnst activeStrategyName;
            if (conf.TryGetSetting(Configuration.Solver_ActiveStrategySetting, out activeStrategyName))
            {
                var result = conf.TryGetStrategyInstance((string)activeStrategyName.Raw, out strategy);
                Contract.Assert(result);
            }
            else
            {
                strategy = OATStrategy.TheFactoryInstance;
            }

            List<Flag> beginFlags;
            var inst = strategy.Begin(conf, PartialModel.Index.SymbolTable, CardSystem.GetInitialDOFs(), out beginFlags);
            if (beginFlags != null)
            {
                flags.AddRange(beginFlags);
            }

            return inst;
        }

        /// <summary>
        /// TODO: Need to recheck this dispose logic
        /// </summary>
        protected void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing && Context != null)
                {
                    Context.Dispose();
                }

                if (disposing && Z3Solver != null)
                {
                    Z3Solver.Dispose();
                }
            }

            disposed = true;
        }
    }
}
