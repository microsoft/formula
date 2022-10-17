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

    internal class Solver : ISolver, IDisposable
    {
        public static readonly uint DefaultRecursionBound = 10;

        private CancellationToken cancel;
        private bool disposed = false;
        private List<Flag> solverFlags = new List<Flag>();

        private List<List<AST<Node>>> cardInequalities = new List<List<AST<Node>>>();

        private SymExecuter executer;
        private bool solvable = false;

        public List<List<AST<Node>>> CardInequalities
        {
            get { return cardInequalities; }
        }

        public CardSystem Cardinalities
        {
            get;
            private set;
        }

        public Configuration Configuration
        {
            get { return (Configuration)Source.Config.CompilerData; }
        }

        public SymbolTable SymbolTable
        {
            get { return PartialModel.Index.SymbolTable; }
        }

        public Env Env
        {
            get;
            private set;
        }

        public uint RecursionBound
        {
            get;
            private set;
        }

        internal IEnumerable<Flag> Flags
        {
            get { return solverFlags; }
        }

        internal FactSet PartialModel
        {
            get;
            private set;
        }

        internal Model Source
        {
            get;
            private set;
        }

        internal Z3Context Context
        {
            get;
            private set;
        }

        internal Z3Solver Z3Solver
        {
            get;
            private set;
        }

        internal TypeEmbedder TypeEmbedder
        {
            get;
            private set;
        }

        internal TermIndex Index
        {
            get { return PartialModel.Index; }
        }

        private uint symbCnstId = 0;

        /// <summary>
        /// The search strategy. Can be null if goal decided as Unsat prior to
        /// instantiation, or if Strategy.Begin(...) failed.
        /// </summary>
        private ISearchStrategy Strategy
        {
            get;
            set;
        }

        private BuilderRef MkModelDecl(string modelName, string modelRefName, string modelLocName, Builder bldr)
        {
            BuilderRef result;
            var domLoc = (Location)PartialModel.Model.Node.Domain.CompilerData;

            // 1. PushModRef to the model we are extending
            bldr.PushModRef(modelRefName, null, modelLocName);

            // 2. PushModRef to the domain of the model
            bldr.PushModRef(((Domain)domLoc.AST.Node).Name, null, ((Program)domLoc.AST.Root).Name.ToString());

            // 3. Push the new model that will extend the previous model
            bldr.PushModel(modelName, true, ComposeKind.Extends);

            // Now bldr.AddModelCompose
            bldr.AddModelCompose();

            bldr.Store(out result);
            return result;
        }

        // Introduce Terms for cardinality constraints
        public void ExtendPartialModel()
        {
            ProgramName programName = new ProgramName("env:///dummy.4ml");
            AST<Program> prog = Factory.Instance.MkProgram(programName);
            String modName = "dummy";

            Builder bldr = new Builder();
            string modLocation = Source.Span.Program.ToString();
            var modelRef = MkModelDecl(modName, Source.Name, modLocation, bldr);

            Map<UserSymbol, List<BuilderRef>> termNodes = new Map<UserSymbol, List<BuilderRef>>(Symbol.Compare);

            foreach (var entry in Cardinalities.SolverState)
            {
                foreach (var item in entry)
                {
                    var cardVar = item.Key;
                    var cardLower = item.Value.Item1.Lower;
                    int arity = cardVar.Symbol.Arity;

                    if (cardVar.Symbol.IsDataConstructor &&
                        cardVar.IsLFPCard &&
                        cardLower > 0)
                    {
                        List<BuilderRef> builderRefs;
                        if (!termNodes.TryFindValue(cardVar.Symbol, out builderRefs))
                        {
                            builderRefs = new List<BuilderRef>();
                            termNodes.Add(cardVar.Symbol, builderRefs);
                        }

                        for (BigInteger i = 0; i < (BigInteger)cardLower; i++)
                        {
                            for (int j = 0; j < arity; j++)
                            {
                                // TODO: try ~
                                bldr.PushId("SC" + symbCnstId++ + "SCNew");
                            }

                            bldr.PushId(cardVar.Symbol.Name);
                            bldr.PushFuncTerm();

                            for (int j = 0; j < arity; j++)
                            {
                                bldr.AddFuncTermArg();
                            }

                            // Store the terms temporarily
                            BuilderRef termNode = new BuilderRef();
                            bldr.Store(out termNode);
                            builderRefs.Add(termNode);

                            bldr.Load(termNode);
                            bldr.PushModelFactNoBinding();
                            bldr.Load(modelRef);
                            bldr.AddModelFact();
                            bldr.Pop();
                        }
                    }
                }
            }

            // Load all of the added facts
            foreach (var kvp in termNodes)
            {
                foreach (var node in kvp.Value)
                {
                    bldr.Load(node);
                }
            }

            // Load the newly created model
            bldr.Load(modelRef);
            bldr.Close();

            ImmutableArray<AST<Node>> asts;
            bldr.GetASTs(out asts);

            Contract.Assert(asts[0].Node.NodeKind == NodeKind.Model);
            prog = Factory.Instance.AddModule(prog, asts[0]);

            // Now retrieve all of the facts
            int currNodeIndex = 1;
            foreach (var kvp in termNodes)
            {
                List<AST<Node>> nodes = new List<AST<Node>>();
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    nodes.Add(asts[currNodeIndex++]);
                }
                cardInequalities.Add(nodes);
            }

            InstallResult result;
            Env.Install(prog, out result);
            if (!result.Succeeded)
            {
                System.Console.WriteLine("Error installing partial model!");
            }

            Program compiledProg = Env.GetProgram("env:///dummy.4ml");
            var progConf = compiledProg.Config.CompilerData as Configuration;
            Location modLoc;
            if (!progConf.TryResolveLocalModule(modName, out modLoc) ||
                modLoc.AST.Node.NodeKind != NodeKind.Model)
            {
                System.Console.WriteLine("Error installing partial model!");
            }

            ModuleData modData = (ModuleData)modLoc.AST.Node.CompilerData;
            Source = (Model)modData.Reduced.Node;
            PartialModel = (FactSet)modData.FinalOutput;
        }

        internal Solver(FactSet partialModel, Model source, Env env, CancellationToken cancel)
        {
            Contract.Requires(partialModel != null);
            Contract.Requires(source != null);
            this.cancel = cancel;

            // Source and PartialModel may be updated by ExtendPartialModel()
            Source = source;
            PartialModel = partialModel;
            Env = env;

            // TODO: reintroduce cardinality system with search heuristics
            //// Step 0. Create cardinality system.
            //Cardinalities = new CardSystem(partialModel);

            //// Step 1. Update the source and partial model
            //if (!Cardinalities.IsUnsat)
            //{
                // TODO: add this after temporary ConSymbs are supported in the TypeEmbedder
                //ExtendPartialModel();
            //}

            //// Step 2. Create Z3 Context and Solver
            CreateContextAndSolver();

            //// Step 3. Create type embedder
            CreateTypeEmbedder();

            //// Step 4. Create cardinality system.
            //Cardinalities = new CardSystem(partialModel);

            //// Step 4. Try to create the search strategy.
            //if (!Cardinalities.IsUnsat)
            //{
                Strategy = CreateStrategy(solverFlags);
            //}

            SetRecursionBound();

            executer = new SymExecuter(this);
        }

        public bool Solve()
        {
            solvable = executer.Solve();
            return solvable;
        }

        public void GetSolution(int num)
        {
            executer.GetSolution(num);
        }

        private void SetRecursionBound()
        {
            Cnst value;
            var conf = (Configuration)Source.Config.CompilerData;
            if (conf.TryGetSetting(Configuration.Solver_RecursionBoundSetting, out value))
            {
                RecursionBound = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                RecursionBound = DefaultRecursionBound;
            }
        }

        public SearchState GetState(IEnumerable<KeyValuePair<UserSymbol, int>> dofs)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns false is no more models can be enumerated.
        /// </summary>
        internal bool GetModel()
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

        private void CreateTypeEmbedder()
        {
            var conf = (Configuration)Source.Config.CompilerData;
            Cnst value;
            var costMap = new Map<BaseSortKind, uint>((x, y) => (int)x - (int)y);

            if (conf.TryGetSetting(Configuration.Solver_RealCostSetting, out value))
            {
                costMap[BaseSortKind.Real] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.Real] = 10;
            }

            if (conf.TryGetSetting(Configuration.Solver_StringCostSetting, out value))
            {
                costMap[BaseSortKind.String] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.String] = 10;
            }

            if (conf.TryGetSetting(Configuration.Solver_IntegerCostSetting, out value))
            {
                costMap[BaseSortKind.Integer] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.Integer] = 11;
            }

            if (conf.TryGetSetting(Configuration.Solver_NaturalCostSetting, out value))
            {
                costMap[BaseSortKind.Natural] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.Natural] = 12;
            }

            if (conf.TryGetSetting(Configuration.Solver_PosIntegerCostSetting, out value))
            {
                costMap[BaseSortKind.PosInteger] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.PosInteger] = 13;
            }

            if (conf.TryGetSetting(Configuration.Solver_NegIntegerCostSetting, out value))
            {
                costMap[BaseSortKind.NegInteger] = (uint)((Rational)value.Raw).Numerator;
            }
            else
            {
                costMap[BaseSortKind.NegInteger] = 13;
            }

            TypeEmbedder = new TypeEmbedder(PartialModel.Index, Context, costMap);
        }

        /// <summary>
        /// Create and set the solver. Will get more complicated as 
        /// params become clear.
        /// </summary>
        private void CreateContextAndSolver()
        {
            var settings = new Dictionary<string, string>()
            {
                { "unsat_core", "true" },
                { "proof", "true" },
                { "model", "true" }
            };

            Context = new Z3Context(settings);
            Z3Solver = Context.MkSolver();
            Z3Solver.Set("core.minimize", true);
            Z3Solver.Set("core.minimize_partial", true);
        }

        /// <summary>
        /// Creates and sets the search strategy.
        /// </summary>
        private ISearchStrategy CreateStrategy(List<Flag> flags)
        {
            Contract.Requires(!Cardinalities.IsUnsat);

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
            var inst = strategy.Begin(this, out beginFlags);
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
