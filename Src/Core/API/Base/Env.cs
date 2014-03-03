namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Nodes;
    using Common;
    using Common.Extras;
    using Compiler;

    public sealed class Env
    {
        private SpinLock isBusyLock = new SpinLock();
        private bool isBusy = false;

        private SpinLock fileRootLock = new SpinLock();
        private ASTConcr<Folder> fileRoot;

        private SpinLock envRootLock = new SpinLock();
        private ASTConcr<Folder> envRoot;

        private Dictionary<ProgramName, Program> programs =
            new Dictionary<ProgramName, Program>();

        private DependencyCollection<ProgramName, Unit> programDeps =
            new DependencyCollection<ProgramName, Unit>(ProgramName.Compare, Unit.Compare);
           
        private CancellationTokenSource canceler = null;

        internal Dictionary<ProgramName, Program> Programs
        {
            get { return programs; }
        }

        public EnvParams Parameters
        {
            get;
            private set;
        }

        public bool IsBusy
        {
            get
            {
                bool gotLock = false;
                try
                {
                    isBusyLock.Enter(ref gotLock);
                    return isBusy;
                }
                finally
                {
                    if (gotLock)
                    {
                        isBusyLock.Exit();
                    }
                }
            }
        }

        public AST<Folder> FileRoot
        {
            get 
            {
                bool gotLock = false;
                try
                {
                    fileRootLock.Enter(ref gotLock);
                    return fileRoot;
                }
                finally
                {
                    if (gotLock)
                    {
                        fileRootLock.Exit();
                    }
                }
            }

            private set
            {
                bool gotLock = false;
                try
                {
                    fileRootLock.Enter(ref gotLock);
                    fileRoot = (ASTConcr<Folder>)value;
                }
                finally
                {
                    if (gotLock)
                    {
                        fileRootLock.Exit();
                    }
                }
            }
        }

        public AST<Folder> EnvRoot
        {
            get
            {
                bool gotLock = false;
                try
                {
                    envRootLock.Enter(ref gotLock);
                    return envRoot;
                }
                finally
                {
                    if (gotLock)
                    {
                        envRootLock.Exit();
                    }
                }
            }

            private set
            {
                bool gotLock = false;
                try
                {
                    envRootLock.Enter(ref gotLock);
                    envRoot = (ASTConcr<Folder>)value;
                }
                finally
                {
                    if (gotLock)
                    {
                        envRootLock.Exit();
                    }
                }
            }
        }

        public Env(EnvParams envParams = null)
        {
            fileRoot = new ASTConcr<Folder>(new Folder("/"));
            envRoot = new ASTConcr<Folder>(new Folder("/"));
            Parameters = envParams == null ? new EnvParams() : envParams;
        }

        public void Cancel()
        {
            bool gotLock = false;
            try
            {
                isBusyLock.Enter(ref gotLock);
                if (canceler != null)
                {
                    canceler.Cancel();
                }
            }
            finally
            {
                if (gotLock)
                {
                    isBusyLock.Exit();
                }
            }              
        }

        public bool CreateObjectGraph(
                        ProgramName progName,
                        string modelName,
                        Func<Common.Rational, Generators.ICSharpTerm> rationalConstructor,
                        Func<string, Generators.ICSharpTerm> stringConstructor,
                        Dictionary<string, Func<Generators.ICSharpTerm[], Generators.ICSharpTerm>> userConstructors,
                        out Task<ObjectGraphResult> task,
                        CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(progName != null && modelName != null);
            Contract.Requires(rationalConstructor != null);
            Contract.Requires(stringConstructor != null);
            Contract.Requires(userConstructors != null);

            if (!GetEnvLock())
            {
                task = null;
                return false;
            }
             
            var ogres = new ObjectGraphResult();
            Program program;
            if (!programs.TryGetValue(progName, out program))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                    Constants.UndefinedSymbol.Code);
                ogres.AddFlag(flag);
                task = Task.Factory.StartNew<ObjectGraphResult>(() => ogres);
                goto Unlock;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location modLoc;
            if (!progConf.TryResolveLocalModule(modelName, out modLoc))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("module", modelName),
                    Constants.UndefinedSymbol.Code);
                ogres.AddFlag(flag);
                task = Task.Factory.StartNew<ObjectGraphResult>(() => ogres);
                goto Unlock;
            }

            var factSet = ((ModuleData)modLoc.AST.Node.CompilerData).FinalOutput as FactSet;
            if (factSet == null || factSet.Model.Node.IsPartial)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("non-partial model", modelName),
                    Constants.UndefinedSymbol.Code);
                ogres.AddFlag(flag);
                task = Task.Factory.StartNew<ObjectGraphResult>(() => ogres);
                goto Unlock;
            }

            task = Task.Factory.StartNew<ObjectGraphResult>(() =>
            {
                factSet.MkObjectGraph(ogres, userConstructors, rationalConstructor, stringConstructor);
                return ogres;
            });

        Unlock:
            ReleaseEnvLock();
            return true;
        }

        public bool Generate(
                        ProgramName progName,
                        string moduleName,
                        System.IO.TextWriter writer,
                        Generators.GeneratorOptions options,
                        out Task<GenerateResult> task,
                        CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(progName != null && moduleName != null);
            Contract.Requires(writer != null && options != null);

            if (!GetEnvLock())
            {
                task = null;
                return false;
            }

            var gres = new GenerateResult();
            Program program;
            if (!programs.TryGetValue(progName, out program))
            {
                gres.AddFlag(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                    Constants.UndefinedSymbol.Code));
                task = Task.Factory.StartNew<GenerateResult>(() => gres);
                goto Unlock;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location moduleLoc;
            if (!progConf.TryResolveLocalModule(moduleName, out moduleLoc))
            {
                gres.AddFlag(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("module", moduleName),
                    Constants.UndefinedSymbol.Code));
                task = Task.Factory.StartNew<GenerateResult>(() => gres);
                goto Unlock;
            }

            var moduleData = (ModuleData)moduleLoc.AST.Node.CompilerData;
            if (moduleData.SymbolTable == null)
            {
                gres.AddFlag(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("module", moduleName),
                    Constants.UndefinedSymbol.Code));
                task = Task.Factory.StartNew<GenerateResult>(() => gres);
                goto Unlock;
            }
            
            task = Task.Factory.StartNew<GenerateResult>(() =>
            {
                switch (options.OutputLanguage)
                {
                    case Generators.GeneratorOptions.Language.CSharp:
                        new Generators.CSharpDataModelGen(
                            moduleData.SymbolTable,
                            writer,
                            options,
                            gres).Generate();
                        break;
                    default:
                        throw new NotImplementedException();
                }

                return gres;
            });

        Unlock:
            ReleaseEnvLock();
            return true;
        }

        public bool Render(
                        ProgramName progName,
                        string moduleName,
                        out Task<RenderResult> task,
                        CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(progName != null && moduleName != null);
            if (!GetEnvLock())
            {
                task = null;
                return false;
            }

            var rres = new RenderResult();
            Program program;
            if (!programs.TryGetValue(progName, out program))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                    Constants.UndefinedSymbol.Code);
                rres.AddFlag(flag);
                task = Task.Factory.StartNew<RenderResult>(() => rres);
                goto Unlock;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location modLoc;
            if (!progConf.TryResolveLocalModule(moduleName, out modLoc))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("module", moduleName),
                    Constants.UndefinedSymbol.Code);
                rres.AddFlag(flag);
                task = Task.Factory.StartNew<RenderResult>(() => rres);
                goto Unlock;
            }

            task = Task.Factory.StartNew<RenderResult>(() =>
                {
                    var renderer = new Renderer(modLoc.AST, rres, cancel);
                    renderer.Render();
                    return rres;
                });
        
        Unlock:
            ReleaseEnvLock();
            return true;
        }

        /// <summary>
        /// Applies a transform given a single step. All ModRefs in the step should be 
        /// fully qualified with absolute paths.
        /// 
        /// If keepDeriviations is true, then keep track of deriviations.
        /// If keepStatistics is true, then returns an exeStats object for
        /// examining the state of execution.
        /// </summary>
        public bool Apply(AST<Step> transformStep,
                          bool keepDerivations,
                          bool keepStatistics,
                          out List<Flag> flags,
                          out Task<ApplyResult> task,
                          out Common.Rules.ExecuterStatistics exeStats,
                          CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(transformStep != null);
            task = null;
            exeStats = null;
            flags = new List<Flag>();
            if (!GetEnvLock())
            {
                return false;
            }

            //// Clean any attached compiler data.
            transformStep = (AST<Step>)transformStep.DeepClone();

            bool succeeded = true;
            int i = 1, otherId;
            var ids = new Common.Map<string, int>(string.Compare);
            foreach (var id in transformStep.Node.Lhs)
            {
                if (!API.ASTQueries.ASTSchema.Instance.IsId(id.Name, false, false, false, false))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        id,
                        Constants.BadSyntax.ToString(string.Format("The name {0} is not a valid output name.", id.Name)),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                if (ids.TryFindValue(id.Name, out otherId))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        id,
                        Constants.DuplicateDefs.ToString("lhs arg " + id.Name, "arg " + otherId.ToString(), "arg " + i.ToString()),
                        Constants.DuplicateDefs.Code));
                    succeeded = false;
                }
                else
                {
                    ids.Add(id.Name, i);
                }

                ++i;
            }

            ModuleData stepModule, argModule;
            if (!ResolveAbsoluteModuleRef(transformStep.Node.Rhs.Module, false, flags, out stepModule))
            {
                goto Unlock;
            }
          
            var source = stepModule.Reduced.Node;
            if (source.NodeKind != NodeKind.Model && source.NodeKind != NodeKind.Transform && source.NodeKind != NodeKind.TSystem)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    source,
                    Constants.BadSyntax.ToString(string.Format("Expected {0} to be a model, transform, or transform system.", transformStep.Node.Rhs.Module.Name)),
                    Constants.BadSyntax.Code));
                goto Unlock;
            }

            if (source.NodeKind == NodeKind.Model)
            {
                if (((Model)source).IsPartial)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        source,
                        Constants.BadSyntax.ToString("Partial models cannot be used in an apply operation."),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                if (transformStep.Node.Rhs.Args.Count != 0)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        source,
                        Constants.BadSyntax.ToString("Models to do not take arguments."),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                if (transformStep.Node.Lhs.Count != 1)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        source,
                        Constants.BadSyntax.ToString(string.Format("Expected only one output, but got {0}.", transformStep.Node.Lhs.Count)),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                if (!succeeded)
                {
                    goto Unlock;
                }

                var lclExeStats = keepStatistics ? new Formula.Common.Rules.ExecuterStatistics() : null;
                task = new Task<ApplyResult>(() =>
                {
                    var ap = new ApplyResult(
                        (FactSet)stepModule.FinalOutput,
                        transformStep.Node.Lhs,
                        new Common.Terms.TermIndex(stepModule.SymbolTable),
                        lclExeStats,
                        keepDerivations, 
                        cancel);

                    ap.Start();
                    return ap;
                },
                TaskCreationOptions.LongRunning);
                exeStats = lclExeStats;
            }
            else if (source.NodeKind == NodeKind.Transform || source.NodeKind == NodeKind.TSystem)
            {
                var index = new Common.Terms.TermIndex(stepModule.SymbolTable);

                string transName;
                Configuration transConf;
                Common.Terms.TermIndex origIndex;
                Common.ImmutableCollection<Param> transInputs, transOutputs;

                if (source.NodeKind == NodeKind.Transform)
                {
                    var transform = (Transform)source;
                    transName = transform.Name;
                    transInputs = transform.Inputs;
                    transOutputs = transform.Outputs;
                    transConf = (Configuration)transform.Config.CompilerData;
                    origIndex = ((Common.Rules.RuleTable)stepModule.FinalOutput).Index;
                }
                else
                {
                    var tsystem = (TSystem)source;
                    transName = tsystem.Name;
                    transInputs = tsystem.Inputs;
                    transOutputs = tsystem.Outputs;
                    transConf = (Configuration)tsystem.Config.CompilerData;
                    origIndex = ((Common.Composites.CoreTSystem)stepModule.FinalOutput).SignatureIndex;
                }

                if (transformStep.Node.Rhs.Args.Count != transInputs.Count)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        source,
                        Constants.BadSyntax.ToString(string.Format("Expected {0} inputs, but got {1}.", transInputs.Count, transformStep.Node.Rhs.Args.Count)),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                if (transformStep.Node.Lhs.Count != transOutputs.Count)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        source,
                        Constants.BadSyntax.ToString(string.Format("Expected {0} outputs, but got {1}.", transOutputs.Count, transformStep.Node.Lhs.Count)),
                        Constants.BadSyntax.Code));
                    succeeded = false;
                }

                i = 0;
                bool wasAdded;
                Model argModel;
                Common.Terms.Term grndTerm;
                Common.Terms.UserSymbol other;
                var valueArgs = new Common.Map<string, Common.Terms.Term>(string.Compare);
                var modelArgs = new Common.Map<string, FactSet>(string.Compare);
                using (var inputIt = transInputs.GetEnumerator())
                {
                    using (var argIt = transformStep.Node.Rhs.Args.GetEnumerator())
                    {
                        while (inputIt.MoveNext() && argIt.MoveNext())
                        {
                            ++i;
                            if (inputIt.Current.IsValueParam)
                            {
                                var type = index.MkClone(origIndex.GetSymbCnstType(origIndex.MkApply(
                                    origIndex.SymbolTable.Resolve(string.Format("{0}.%{1}", transName, inputIt.Current.Name), out other),
                                    Common.Terms.TermIndex.EmptyArgs,
                                    out wasAdded)));

                                if (index.ParseGroundTerm(Factory.Instance.ToAST(argIt.Current), transConf, flags, out grndTerm))
                                {
                                    if (index.IsGroundMember(type, grndTerm))
                                    {
                                        valueArgs.Add(inputIt.Current.Name, grndTerm);
                                    }
                                    else
                                    {
                                        flags.Add(new Flag(
                                            SeverityKind.Error,
                                            argIt.Current,
                                            Constants.BadArgType.ToString(i, transName),
                                            Constants.BadArgType.Code));
                                        succeeded = false;
                                    }
                                }
                                else
                                {
                                    succeeded = false;
                                }
                            }
                            else
                            {
                                if (argIt.Current.NodeKind != NodeKind.ModRef)
                                {
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        argIt.Current,
                                        Constants.BadSyntax.ToString("Expected a module reference"),
                                        Constants.BadSyntax.Code));
                                    succeeded = false;
                                }
                                else if (!ResolveAbsoluteModuleRef((ModRef)argIt.Current, false, flags, out argModule))
                                {
                                    succeeded = false;
                                }
                                else if (argModule.Reduced.Node.NodeKind != NodeKind.Model)
                                {
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        argIt.Current,
                                        Constants.BadSyntax.ToString("Expected a model"),
                                        Constants.BadSyntax.Code));
                                    succeeded = false;
                                }
                                else
                                {
                                    argModel = (Model)argModule.Reduced.Node;
                                    if (argModel.IsPartial)
                                    {
                                        flags.Add(new Flag(
                                            SeverityKind.Error,
                                            argIt.Current,
                                            Constants.BadSyntax.ToString("Partial models cannot be used in an apply operation."),
                                            Constants.BadSyntax.Code));
                                        succeeded = false;
                                    }
                                    else if (Location.Compare((Location)argModel.Domain.CompilerData, (Location)((ModRef)inputIt.Current.Type).CompilerData) != 0)
                                    {
                                        flags.Add(new Flag(
                                            SeverityKind.Error,
                                            argIt.Current,
                                            Constants.BadArgType.ToString(i, transName),
                                            Constants.BadArgType.Code));
                                        succeeded = false;
                                    }
                                    else
                                    {
                                        modelArgs.Add(((ModRef)inputIt.Current.Type).Rename, (FactSet)argModule.FinalOutput);
                                    }
                                }
                            }
                        }
                    }
                }

                if (!succeeded)
                {
                    goto Unlock;
                }

                var lclExeStats = keepStatistics ? new Formula.Common.Rules.ExecuterStatistics() : null;
                task = new Task<ApplyResult>(() =>
                {
                    var ap = new ApplyResult(
                        stepModule,
                        modelArgs,
                        valueArgs,
                        transformStep.Node.Lhs,
                        index,
                        lclExeStats,
                        keepDerivations,
                        cancel);

                    ap.Start();
                    return ap;
                },
                TaskCreationOptions.LongRunning);
                exeStats = lclExeStats;
            }
            else 
            {
                throw new NotImplementedException();
            }

        Unlock:
            ReleaseEnvLock();
            return true;
        }
                                                         
        /// <summary>
        /// Queries a model for a disjunction of goals. Only performs
        /// work sufficient to evaluate the goals. Returns a null task
        /// if some parameters were incorrect.
        ///  
        /// If keepDeriviations is true, then keep track of deriviations.
        /// If keepStatistics is true, then returns an exeStats object for
        /// examining the state of query execution.
        /// </summary>
        public bool Query(ProgramName progName,
                          string modelName,
                          IEnumerable<AST<Body>> goals,
                          bool keepDerivations,
                          bool keepStatistics,
                          out List<Flag> flags,
                          out Task<QueryResult> task,
                          out Common.Rules.ExecuterStatistics exeStats,
                          CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(progName != null && modelName != null);
            Contract.Requires(goals != null && goals.Count<AST<Body>>() > 0);
            var span = goals.First<AST<Body>>().Node.Span;

            flags = new List<Flag>();
            if (!GetEnvLock())
            {
                task = null;
                exeStats = null;
                return false;
            }

            Program program;
            if (!programs.TryGetValue(progName, out program))
            {
                task = null;
                exeStats = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                    Constants.UndefinedSymbol.Code));
                goto Unlock;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location modLoc;
            if (!progConf.TryResolveLocalModule(modelName, out modLoc) ||
                modLoc.AST.Node.NodeKind != NodeKind.Model)
            {
                task = null;
                exeStats = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("model", modelName),
                    Constants.UndefinedSymbol.Code));
                goto Unlock;
            }

            var modData = (ModuleData)modLoc.AST.Node.CompilerData;
            var redModel = (Model)modData.Reduced.Node;
            if (redModel.IsPartial)
            {
                task = null;
                exeStats = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    goals.First<AST<Body>>().Node,
                    Constants.BadSyntax.ToString("cannot query a partial model"),
                    Constants.BadSyntax.Code));
                goto Unlock;
            }

            //// Build a dummy model to hold the goals as an additional requires
            var domRef = Factory.Instance.MkModRef(
                redModel.Domain.Name,
                redModel.Domain.Rename,
                redModel.Domain.Location,
                redModel.Domain.Span);

            var modRef = Factory.Instance.MkModRef(
                redModel.Name,
                null,
                null,
                redModel.Span);

            var queryName = "_Query_" + Guid.NewGuid().ToString("D").Replace('-', '_');
            var queryModel = Factory.Instance.MkModel(queryName, false, domRef, ComposeKind.Includes, span);
            var requires = Factory.Instance.MkContract(ContractKind.RequiresProp, span);
            foreach (var g in goals)
            {
                requires = Factory.Instance.AddContractSpec(requires, (AST<Body>)g.DeepClone());
            }

            queryModel = (AST<Model>)Factory.Instance.AddContract(queryModel, requires);
            queryModel = Factory.Instance.AddModelCompose(queryModel, modRef);
            queryModel = CaptureConfig(
                queryModel,
                (Model)modData.Source.AST.Node,
                span);

            var queryProgram = Factory.Instance.MkProgram(new ProgramName(Common.Terms.SymbolTable.ManglePrefix + "Query", modData.Source.Program.Name));
            queryProgram = Factory.Instance.AddModule(queryProgram, queryModel);
            queryProgram = CaptureConfig(
                queryProgram,
                (AST<Program>)Factory.Instance.ToAST(modData.Source.Program), 
                span);

            var result = new InstallResult();
            result.Succeeded = true;
            result.AddTouched(queryProgram, InstallKind.Compiled);
            result.Succeeded = InstallSafeProgram(queryProgram, result, true) & result.Succeeded;

            foreach (var f in result.Flags)
            {
                flags.Add(f.Item2);
            }

            if (!result.Succeeded)
            {
                task = null;
                exeStats = null;
                goto Unlock;
            }

            modData = queryModel.Node.CompilerData as ModuleData;
            var lclExeStats = keepStatistics ? new Formula.Common.Rules.ExecuterStatistics() : null;
            task = new Task<QueryResult>(() =>
            {
                var qr = new QueryResult((FactSet)modData.FinalOutput, lclExeStats, keepDerivations, cancel);
                qr.Start();
                return qr;
            }, 
            TaskCreationOptions.LongRunning);
            exeStats = lclExeStats;

        Unlock:
            ReleaseEnvLock();
            return true;
        }

        /// <summary>
        /// Tries to solve a model for a disjunction of goals. Currently reduces this to
        /// old formula, so this interface is under-defined.
        /// </summary>
        public bool Solve(ProgramName progName,
                          string modelName,                       
                          IEnumerable<AST<Body>> goals,
                          int maxSols,
                          out List<Flag> flags,
                          out Task<SolveResult> task,
                          CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(progName != null && modelName != null);
            Contract.Requires(goals != null && goals.Count<AST<Body>>() > 0);
            Contract.Requires(maxSols > 0);

            var span = goals.First<AST<Body>>().Node.Span;

            flags = new List<Flag>();
            if (!GetEnvLock())
            {
                task = null;
                return false;
            }

            Program program;
            if (!programs.TryGetValue(progName, out program))
            {
                task = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                    Constants.UndefinedSymbol.Code));
                goto Unlock;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location modLoc;
            if (!progConf.TryResolveLocalModule(modelName, out modLoc) ||
                modLoc.AST.Node.NodeKind != NodeKind.Model)
            {
                task = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("model", modelName),
                    Constants.UndefinedSymbol.Code));
                goto Unlock;
            }

            var modData = (ModuleData)modLoc.AST.Node.CompilerData;
            var redModel = (Model)modData.Reduced.Node;
            if (!redModel.IsPartial)
            {
                task = null;
                flags.Add(new Flag(
                    SeverityKind.Error,
                    goals.First<AST<Body>>().Node,
                    Constants.BadSyntax.ToString("cannot solve a model; expected partiality."),
                    Constants.BadSyntax.Code));
                goto Unlock;
            }

            //// Build a dummy model to hold the goals as an additional requires
            var domRef = Factory.Instance.MkModRef(
                redModel.Domain.Name,
                redModel.Domain.Rename,
                redModel.Domain.Location,
                redModel.Domain.Span);

            var modRef = Factory.Instance.MkModRef(
                redModel.Name,
                null,
                null,
                redModel.Span);

            var queryName = "_Query_" + Guid.NewGuid().ToString("D").Replace('-', '_');
            var queryModel = Factory.Instance.MkModel(queryName, false, domRef, ComposeKind.Includes, span);
            var requires = Factory.Instance.MkContract(ContractKind.RequiresProp, span);
            foreach (var g in goals)
            {
                requires = Factory.Instance.AddContractSpec(requires, (AST<Body>)g.DeepClone());
            }

            queryModel = (AST<Model>)Factory.Instance.AddContract(queryModel, requires);
            queryModel = Factory.Instance.AddModelCompose(queryModel, modRef);
            queryModel = CaptureConfig(
                queryModel,
                (Model)modData.Source.AST.Node,
                span);

            var queryProgram = Factory.Instance.MkProgram(new ProgramName(Common.Terms.SymbolTable.ManglePrefix + "Query", modData.Source.Program.Name));
            queryProgram = Factory.Instance.AddModule(queryProgram, queryModel);
            queryProgram = CaptureConfig(
                queryProgram,
                (AST<Program>)Factory.Instance.ToAST(modData.Source.Program),
                span);

            var result = new InstallResult();
            result.Succeeded = true;
            result.AddTouched(queryProgram, InstallKind.Compiled);
            result.Succeeded = InstallSafeProgram(queryProgram, result, true) & result.Succeeded;

            foreach (var f in result.Flags)
            {
                flags.Add(f.Item2);
            }

            if (!result.Succeeded)
            {
                task = null;
                goto Unlock;
            }

            modData = queryModel.Node.CompilerData as ModuleData;
            task = new Task<SolveResult>(() =>
            {
                var sr = new SolveResult(
                    redModel,
                    (FactSet)modData.FinalOutput,
                    maxSols,
                    cancel);
                sr.Start();
                return sr;
            },
            TaskCreationOptions.LongRunning);

        Unlock:
            ReleaseEnvLock();
            return true;
        }
                                                  
        public bool Install(string filename, out InstallResult result)
        {
            Contract.Requires(filename != null);

            if (!GetEnvLock())
            {
                result = null;
                return false;
            }

            LockedInstall(filename, out result);

            ReleaseEnvLock();
            return true;
        }

        public bool Install(AST<Program> program, out InstallResult result)
        {
            Contract.Requires(program != null);
            if (!GetEnvLock())
            {
                result = null;
                return false;
            }

            LockedInstall(program, out result);

            ReleaseEnvLock();
            return true;
        }

        public bool Uninstall(IEnumerable<ProgramName> progs, out InstallResult result)
        {
            if (!GetEnvLock())
            {
                result = null;
                return false;
            }

            LockedUninstall(progs, out result);

            ReleaseEnvLock();
            return true;
        }

        public bool Reinstall(IEnumerable<ProgramName> progs, out InstallResult result)
        {
            if (!GetEnvLock())
            {
                result = null;
                return false;
            }

            result = new InstallResult();
            if (progs == null)
            {
                goto Unlock;
            }

            InstallResult uninstRes;
            var uninstallResults = new LinkedList<InstallResult>();
            foreach (var p in progs)
            {
                if (p != null && p.IsFileProgramName)
                {
                    LockedUninstall(new ProgramName[] { p }, out uninstRes);
                    if (uninstRes != null)
                    {
                        uninstallResults.AddLast(uninstRes);
                    }
                }
            }

            InstallResult instRes;
            foreach (var ur in uninstallResults)
            {
                foreach (var istat in ur.Touched)
                {
                    if (istat.Program.Node.Name.IsFileProgramName)
                    {
                        LockedInstall(istat.Program.Node.Name.Uri.LocalPath, out instRes);
                        result.Union(instRes);
                    }
                    else
                    {
                        LockedInstall(istat.Program, out instRes);
                        result.Union(instRes);
                    }
                }
            }

        Unlock:
            ReleaseEnvLock();
            return true;
        }

        private void LockedInstall(string filename, out InstallResult result)
        {
            result = new InstallResult();
            result.Succeeded = true;
            ProgramName progName;
            try
            {
                progName = new ProgramName(filename);
            }
            catch (Exception e)
            {
                var unknown = Factory.Instance.MkProgram(ProgramName.ApiErrorName);
                var flag = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.BadFile.ToString(string.Format("Could not open {0}; {1}", filename, e.Message)),
                    Constants.BadFile.Code,
                    unknown.Node.Name);
                result.AddFlag(unknown, flag);
                return;
            }

            var task = Factory.Instance.ParseFile(progName, canceler.Token);
            task.Wait();

            if (!task.Result.Succeeded)
            {
                result.Succeeded = false;
                result.AddTouched(task.Result.Program, InstallKind.Failed);
            }
            else
            {
                result.AddTouched(task.Result.Program, InstallKind.Compiled);
            }

            result.AddFlags(task.Result);
            result.Succeeded = InstallSafeProgram(task.Result.Program, result) &
                               result.Succeeded;
        }

        private void LockedInstall(AST<Program> program, out InstallResult result)
        {
            result = new InstallResult();
            result.Succeeded = true;

            var clone = program.DeepClone(canceler.Token) as AST<Program>;
            if (canceler.Token.IsCancellationRequested)
            {
                result.Succeeded = false;
                result.AddTouched(program, InstallKind.Failed);
                result.AddFlag(program,
                    new Flag(
                        SeverityKind.Error,
                        program.Node,
                        Constants.OpCancelled.ToString(""),
                        Constants.OpCancelled.Code,
                        program.Node.Name));
            }
            else
            {
                result.AddTouched(clone, InstallKind.Compiled);
                result.Succeeded = InstallSafeProgram(clone, result) & result.Succeeded;
            }
        }

        private void LockedUninstall(IEnumerable<ProgramName> progs, out InstallResult result)
        {
            result = new InstallResult();
            if (progs == null)
            {
                return;
            }

            var delSet = new Set<ProgramName>(ProgramName.Compare);
            foreach (var del in progs)
            {
                if (del != null && programs.ContainsKey(del))
                {
                    delSet.Add(del);
                }
            }

            int size;
            var top = programDeps.GetTopologicalSort(out size);
            var nextDeps = new DependencyCollection<ProgramName, Unit>(ProgramName.Compare, Unit.Compare);
            bool isDeleted;
            foreach (var n in top)
            {
                isDeleted = false;
                foreach (var p in EnumeratePrograms(n))
                {
                    if (delSet.Contains(p))
                    {
                        isDeleted = true;
                        break;
                    }
                }

                if (isDeleted)
                {
                    foreach (var p in EnumeratePrograms(n))
                    {
                        if (programs.ContainsKey(p))
                        {
                            result.AddTouched((AST<Program>)Factory.Instance.ToAST(programs[p]), InstallKind.Uninstalled);
                            programs.Remove(p);
                        }
                    }

                    //// If this node is deleted, then all programs depending on it are deleted.
                    foreach (var pr in n.Provides)
                    {
                        foreach (var p in EnumeratePrograms(pr.Target))
                        {
                            delSet.Add(p);
                        }
                    }
                }
                else
                {
                    //// If this node is not deleted, then it depends on its requests.
                    foreach (var p in EnumeratePrograms(n))
                    {
                        nextDeps.Add(p);
                    }

                    if (n.Kind == DependencyNodeKind.Normal)
                    {
                        foreach (var rq in n.Requests)
                        {
                            nextDeps.Add(rq.Target.Resource, n.Resource, default(Unit));
                        }
                    }
                    else
                    {
                        foreach (var m in n.InternalNodes)
                        {
                            foreach (var rq in m.Requests)
                            {
                                nextDeps.Add(rq.Target.Resource, m.Resource, default(Unit));
                            }
                        }
                    }
                }
            }

            //// Rebuild the directory structure
            string schemeStr;
            var newFileRoot = new ASTConcr<Folder>(new Folder("/"));
            var newEnvRoot = new ASTConcr<Folder>(new Folder("/"));
            foreach (var kv in programs)
            {
                var path = kv.Key.Uri.AbsoluteUri;
                if (path.StartsWith(ProgramName.EnvironmentScheme.AbsoluteUri))
                {
                    schemeStr = ProgramName.EnvironmentScheme.AbsoluteUri;
                }
                else if (path.StartsWith(ProgramName.FileScheme.AbsoluteUri))
                {
                    schemeStr = ProgramName.FileScheme.AbsoluteUri;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var segments = path.Substring(schemeStr.Length).Split(ProgramName.UriSeparators);
                Contract.Assert(segments.Length > 0);
                if (kv.Key.IsFileProgramName)
                {
                    AST<Folder> newRoot = newFileRoot;
                    if (segments.Length > 1)
                    {
                        newRoot = MkSubFolders(newRoot.Node, segments.Truncate<string>(1));
                    }

                    newRoot = Factory.Instance.AddProgram(newRoot, (AST<Program>)Factory.Instance.ToAST(kv.Value));
                    newFileRoot = (ASTConcr<Folder>)Factory.Instance.ToAST(newRoot.Root);
                }
                else
                {
                    AST<Folder> newRoot = newEnvRoot;
                    if (segments.Length > 1)
                    {
                        newRoot = MkSubFolders(newRoot.Node, segments.Truncate<string>(1));
                    }

                    newRoot = Factory.Instance.AddProgram(newRoot, (AST<Program>)Factory.Instance.ToAST(kv.Value));
                    newEnvRoot = (ASTConcr<Folder>)Factory.Instance.ToAST(newRoot.Root);
                }
            }

            FileRoot = newFileRoot;
            EnvRoot = newEnvRoot;
        }

        private IEnumerable<ProgramName> EnumeratePrograms(DependencyCollection<ProgramName, Unit>.IDependencyNode n)
        {
            if (n.Kind == DependencyNodeKind.Cyclic)
            {
                foreach (var i in n.InternalNodes)
                {
                    yield return i.Resource;
                }
            }
            else if (n.Kind == DependencyNodeKind.Normal)
            {
                var scc = n.SCCNode;
                if (scc == n)
                {
                    yield return n.Resource;
                }
                else
                {
                    foreach (var i in scc.InternalNodes)
                    {
                        yield return i.Resource;
                    }
                }
            }
        }

        /// <summary>
        /// If isQueryContainer is true, then program is a dummy for wrapping query evaluation.
        /// </summary>
        private bool InstallSafeProgram(AST<Program> program, InstallResult result, bool isQueryContainer = false)
        {
            if (programs.ContainsKey(program.Node.Name))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    program.Node,
                    Constants.AlreadyInstalledError.ToString(program.Node.Name.ToString(Parameters)),
                    Constants.AlreadyInstalledError.Code,
                    program.Node.Name);
                result.AddFlag(program, flag);
                return false;
            }

            var loader = new Loader(this, program, result, canceler.Token);
            if (!loader.Load())
            {
                if (!result.Succeeded)
                {
                    result.AddFlag(
                        program,
                        new Flag(SeverityKind.Error, default(Span), Constants.OpFailed.ToString("install"), Constants.OpFailed.Code));
                }

                ReleaseEnvLock();
                return true;
            }

            var compiler = new Compiler(this, result, canceler.Token);
            if (!compiler.Compile(isQueryContainer))
            {
                if (!result.Succeeded)
                {
                    result.AddFlag(
                        program,
                        new Flag(SeverityKind.Error, default(Span), Constants.OpFailed.ToString("install"), Constants.OpFailed.Code));
                }
            }
            else if (!isQueryContainer)
            {
                FinalizeInstallation(result);
            }

            return true;
        }

        private void FinalizeInstallation(InstallResult result)
        {
            foreach (var p in result.Touched)
            {
                if (p.Status != InstallKind.Compiled)
                {
                    continue;
                }

                //// Add the program to the program map
                programs.Add(p.Program.Node.Name, p.Program.Node);
                RegisterProgramDependencies(p.Program);

                //// Add the program to the folder tree
                var path = p.Program.Node.Name.Uri.AbsoluteUri;
                string schemeStr;
                if (path.StartsWith(ProgramName.EnvironmentScheme.AbsoluteUri))
                {
                    schemeStr = ProgramName.EnvironmentScheme.AbsoluteUri;
                }
                else if (path.StartsWith(ProgramName.FileScheme.AbsoluteUri))
                {
                    schemeStr = ProgramName.FileScheme.AbsoluteUri;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var segments = path.Substring(schemeStr.Length).Split(ProgramName.UriSeparators);
                Contract.Assert(segments.Length > 0);

                if (p.Program.Node.Name.IsFileProgramName)
                {
                    AST<Folder> newRoot = fileRoot;
                    if (segments.Length > 1)
                    {
                        newRoot = MkSubFolders(newRoot.Node, segments.Truncate<string>(1));
                    }

                    newRoot = Factory.Instance.AddProgram(newRoot, p.Program);
                    FileRoot = (ASTConcr<Folder>)Factory.Instance.ToAST(newRoot.Root);
                }
                else
                {
                    AST<Folder> newRoot = envRoot;
                    if (segments.Length > 1)
                    {
                        newRoot = MkSubFolders(newRoot.Node, segments.Truncate<string>(1));
                    }

                    newRoot = Factory.Instance.AddProgram(newRoot, p.Program);
                    EnvRoot = (ASTConcr<Folder>)Factory.Instance.ToAST(newRoot.Root);
                }
            }
        }

        private AST<Folder> MkSubFolders(Folder root, IEnumerable<string> path)
        {
            var cpath = new LinkedList<ChildInfo>();
            var crnt = (AST<Folder>)Factory.Instance.ToAST(root);
            var parent = root;
            var recomputeCrnt = false;
            AST<Folder> newChild;

            cpath.AddLast(new ChildInfo(root, ChildContextKind.AnyChildContext, -1, -1));
            foreach (var itm in path)
            {
                bool matched = false;
                foreach (var ci in parent.ChildrenInfo)
                {
                    if (ci.Node.NodeKind == NodeKind.Folder &&
                        ((Folder)ci.Node).Name == itm)
                    {
                        recomputeCrnt = true;
                        cpath.AddLast(ci);
                        parent = (Folder)ci.Node;
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    if (recomputeCrnt)
                    {
                        crnt = (AST<Folder>)Factory.Instance.FromAbsPositions(crnt.Root, cpath);
                    }

                    newChild = Factory.Instance.MkFolder(itm);
                    crnt = Factory.Instance.AddSubFolder(crnt, newChild);
                    cpath.AddLast(new ChildInfo(
                                        newChild.Node, 
                                        ChildContextKind.AnyChildContext, 
                                        parent.SubFolders.Count,
                                        parent.SubFolders.Count));
                    crnt = (AST<Folder>)Factory.Instance.FromAbsPositions(crnt.Root, cpath);
                    recomputeCrnt = false;
                    parent = newChild.Node;
                }
            }

            if (recomputeCrnt)
            {
                return (AST<Folder>)Factory.Instance.FromAbsPositions(crnt.Root, cpath);
            }
            else
            {
                return crnt;
            }
        }

        private void RegisterProgramDependencies(AST<Program> program)
        {
            var modRefQuery = new API.ASTQueries.NodePred[]
            {
                API.ASTQueries.NodePredFactory.Instance.Star,
                API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
            };

            programDeps.Add(program.Node.Name);
            program.FindAll(
                modRefQuery, 
                (ch, n) =>                
                {
                    programDeps.Add(((Location)n.CompilerData).Program.Name, program.Node.Name, default(Unit));
                });
        }

        private void ReleaseEnvLock()
        {
            bool gotLock = false;
            try
            {
                isBusyLock.Enter(ref gotLock);
                isBusy = false;
            }
            finally
            {
                if (gotLock)
                {
                    isBusyLock.Exit();
                }
            }
        }

        private bool GetEnvLock()
        {
            bool gotLock = false;
            try
            {
                isBusyLock.Enter(ref gotLock);
                if (!isBusy)
                {
                    isBusy = true;
                    canceler = new CancellationTokenSource();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            finally
            {
                if (gotLock)
                {
                    isBusyLock.Exit();
                }
            }
        }

        private bool ResolveAbsoluteModuleRef(ModRef modRef, bool canRename, List<Flag> flags, out ModuleData data)
        {
            data = null;
            if (!canRename && !string.IsNullOrEmpty(modRef.Rename))
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    modRef,
                    Constants.BadSyntax.ToString("The renaming operator cannot be used here."),
                    Constants.BadSyntax.Code));
                return false;
            }

            Program program = null;
            try
            {
                var progName = new ProgramName(modRef.Location);
                if (!programs.TryGetValue(progName, out program))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        default(Span),
                        Constants.UndefinedSymbol.ToString("program", progName.ToString(Parameters)),
                        Constants.UndefinedSymbol.Code));
                    return false; 
                }                
            }
            catch
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("program", modRef.Location),
                    Constants.UndefinedSymbol.Code));
                return false;
            }

            var progConf = program.Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            Location modLoc;
            if (!progConf.TryResolveLocalModule(modRef.Name, out modLoc))
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.UndefinedSymbol.ToString("module", modRef.Name),
                    Constants.UndefinedSymbol.Code));
                return false;
            }

            data = modLoc.AST.Node.CompilerData as ModuleData;
            Contract.Assert(data != null);
            return true;
        }

        /// <summary>
        /// Copies the configuration from src into dst
        /// </summary>
        private AST<Model> CaptureConfig(AST<Model> dstModel, Model srcModel, Span span)
        {
            Contract.Requires(dstModel != null && srcModel != null);
            Contract.Requires(dstModel.Root.NodeKind == NodeKind.Model);

            var config = (AST<Config>)dstModel.FindAny(
                new ASTQueries.NodePred[]
                {
                    ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Model),
                    ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Config),
                });
            Contract.Assert(config != null);

            foreach (var s in srcModel.Config.Settings)
            {
                config = Factory.Instance.AddSetting(
                    config,
                    Factory.Instance.MkId(s.Key.Name, span),
                    (AST<Cnst>)Factory.Instance.ToAST(s.Value).DeepClone());
            }

            return (AST<Model>)Factory.Instance.ToAST(config.Root);
        }

        /// <summary>
        /// Creates a config that uses src program as the default config, and
        /// registers all the local modules in the dst program config.
        /// </summary>
        private AST<Program> CaptureConfig(AST<Program> dstProgram, AST<Program> srcProgram, Span span)
        {
            Contract.Requires(dstProgram != null && srcProgram != null);
            Contract.Requires(dstProgram.Root.NodeKind == NodeKind.Program);

            var config = (AST<Config>)dstProgram.FindAny(
                new ASTQueries.NodePred[]
                {
                    ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Program),
                    ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Config),
                });
            Contract.Assert(config != null);

            config = Factory.Instance.AddSetting(
                config,
                Factory.Instance.MkId(Configuration.DefaultsSetting, span),
                Factory.Instance.MkCnst(srcProgram.Node.Name.ToString()));

            string name;
            foreach (var m in srcProgram.Node.Modules)
            {
                if (!m.TryGetStringAttribute(AttributeKind.Name, out name))
                {
                    continue;
                }

                config = Factory.Instance.AddSetting(
                    config,
                    Factory.Instance.MkId(Configuration.ModulesCollectionName + "." + name, span),
                    Factory.Instance.MkCnst(name + " at " + srcProgram.Node.Name.ToString()));
            }

            return (AST<Program>)Factory.Instance.ToAST(config.Root);
        }
    }
}
