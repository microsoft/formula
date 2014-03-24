namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics.Contracts;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.ASTQueries;
    using API.Plugins;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Common.Rules;

    public class Compiler
    {
        //// Queries used to find configs and defaults settings
        private static NodePred[] queryConfig = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Config)
            };

        private static NodePred[] queryMyConfig = new NodePred[]
            {
                NodePredFactory.Instance.MkPredicate(NodeKind.AnyNodeKind),
                NodePredFactory.Instance.MkPredicate(NodeKind.Config)
            };

        private static NodePred[] queryProgModule = new NodePred[]
            {
                NodePredFactory.Instance.MkPredicate(NodeKind.Program),
                NodePredFactory.Instance.Module
            };

        private static NodePred[] queryModRef = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
            };

        private static NodePred[] queryQuote = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Quote)
            };

        private CancellationToken cancel;

        internal InstallResult Result
        {
            get;
            private set;
        }

        internal Env Env
        {
            get;
            private set;
        }

        internal Compiler(Env env, InstallResult result, CancellationToken cancel)
        {
            Env = env;
            Result = result;
            this.cancel = cancel;
        }

        /***********************************************************/
        /****************       Compiler Data    *******************/
        /***********************************************************/

        /// <summary>
        /// If module has been returned by the compiler, then tries to get the reduced version of the
        /// module without DSL quotations.
        /// </summary>
        public static bool TryGetReducedForm(AST<Node> module, out AST<Node> redModule)
        {
            Contract.Requires(module.Node.IsModule);
            var modData = module.Node.CompilerData as ModuleData;
            if (modData != null)
            {
                redModule = modData.Reduced;
                return true;
            }
            else
            {
                redModule = null;
                return false;
            }
        }

        /// <summary>
        /// If module has been returned by the compiler, then tries to get the symbol table
        /// of this module.
        /// </summary>
        public static bool TryGetSymbolTable(AST<Node> module, out SymbolTable symbols)
        {
            Contract.Requires(module.Node.IsModule);
            var modData = module.Node.CompilerData as ModuleData;
            if (modData != null && modData.SymbolTable != null)
            {
                symbols = modData.SymbolTable;
                return true;
            }
            else
            {
                symbols = null;
                return false;
            }
        }

        public static bool TryGetTypeEnvironment(Node n, out TypeEnvironment typeEnv)
        {
            Contract.Requires(n != null);
            typeEnv = n.CompilerData as TypeEnvironment;
            return typeEnv != null;
        }

        public static bool TryGetTarget(ModRef n, out AST<Node> target)
        {
            Contract.Requires(n != null);
            if (!(n.CompilerData is Location))
            {
                target = null;
                return false;
            }

            target = ((Location)n.CompilerData).AST;
            return true;
        }

        internal bool Compile(bool isQueryContainer)
        {
            Map<ProgramName, Configuration> programConfigs;
            if (!RegisterModules(out programConfigs))
            {
                Result.Succeeded = false;
            }

            if (Result.Succeeded && !ApplyConfiguration(programConfigs))
            {
                Result.Succeeded = false;
            }

            DependencyCollection<Location, Location> newModDeps = null;
            if (Result.Succeeded && !BuildModuleDependencies(programConfigs, out newModDeps))
            {
                Result.Succeeded = false;
            }

            if (Result.Succeeded && !EliminateQuotations(isQueryContainer))
            {
                Result.Succeeded = false;
            }

            if (Result.Succeeded && !BuildModules(newModDeps))
            {
                Result.Succeeded = false;
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Tries to eliminate the quotations from the AST using the provided configuration.
        /// </summary>        
        internal static Node EliminateQuotations(Configuration config, AST<Node> ast, List<Flag> flags)
        {
            Contract.Requires(config != null && ast != null && flags != null);
            var configStack = new Stack<Configuration>();
            var success = new SuccessToken();

            configStack.Push(config);
            var simplified = ast.Compute<Node>(
                (node) =>
                {
                    return ElimQuoteUnfold(node, configStack, success, flags, default(CancellationToken));
                },
                (node, folds) =>
                {
                    return ElimQuoteFold(node, folds, configStack);
                });

            return success.Result ? simplified : null;
        }

        private bool BuildModules(DependencyCollection<Location, Location> modDeps)
        {
            int nDeps;
            var deps = modDeps.GetTopologicalSort(out nDeps, cancel);
            var flags = new List<Flag>();
            var result = true;
            foreach (var d in deps)
            {
                Contract.Assert(d.Kind == DependencyNodeKind.Normal);
                if (d.Resource.AST.Node.NodeKind != NodeKind.Domain &&
                    d.Resource.AST.Node.NodeKind != NodeKind.Transform &&
                    d.Resource.AST.Node.NodeKind != NodeKind.TSystem &&
                    d.Resource.AST.Node.NodeKind != NodeKind.Model)
                {
                    continue; //// Others not implemented yet.
                }

                var ast = d.Resource.AST;
                var modData = d.Resource.AST.Node.CompilerData as ModuleData;
                Contract.Assert(modData != null);

                //// In this case, the module was already compiled on an earlier interaction.
                if (modData.Phase != ModuleData.PhaseKind.Reduced)
                {
                    continue;
                }

                string name;
                modData.Reduced.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                //// Console.WriteLine("Building symbol table for {0}", name);

                var symbTable = new SymbolTable(modData);

                flags.Clear();
                result = symbTable.Compile(flags, cancel) & result;
                Result.AddFlags((AST<Program>)Factory.Instance.ToAST(ast.Root), flags);
                flags.Clear();

                if (cancel.IsCancellationRequested)
                {
                    return false;
                }

                if (!symbTable.IsValid)
                {
                    continue;
                }

                modData.PassedPhase(ModuleData.PhaseKind.TypesDefined, symbTable);

                if (modData.Source.AST.Node.NodeKind == NodeKind.Model)
                {
                    //// Console.WriteLine("Building fact table for {0}", name);
                    var facts = new FactSet(modData);
                    if (facts.Validate(flags, cancel))
                    {
                        modData.PassedPhase(ModuleData.PhaseKind.Compiled, facts);
                    }
                    else
                    {
                        result = false;
                    }
                }
                else if (modData.Source.AST.Node.NodeKind == NodeKind.TSystem)
                {
                    var tsys = new Common.Composites.CoreTSystem(modData);
                    if (tsys.Compile(flags, cancel))
                    {
                        modData.PassedPhase(ModuleData.PhaseKind.Compiled, tsys);
                    }
                    else
                    {
                        result = false;
                    }
                }
                else
                {
                    //// Console.WriteLine("Building rule table for {0}", name);
                    var ruleTable = new RuleTable(modData);
                    if (ruleTable.Compile(flags, cancel))
                    {
                        //// ruleTable.Debug_PrintRuleTable();
                        modData.PassedPhase(ModuleData.PhaseKind.Compiled, ruleTable);
                        Configuration conf = null;
                        switch (modData.Source.AST.Node.NodeKind)
                        {
                            case NodeKind.Domain:
                                conf = (Configuration)((Domain)modData.Source.AST.Node).Config.CompilerData;
                                break;
                            case NodeKind.Transform:
                                conf = (Configuration)((Transform)modData.Source.AST.Node).Config.CompilerData;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        Cnst prodCheckSetting;
                        if (conf.TryGetSetting(Configuration.Compiler_ProductivityCheckSetting, out prodCheckSetting))
                        {
                            ruleTable.ProductivityCheck(prodCheckSetting, flags);
                        }
                    }
                    else
                    {
                        result = false;
                    }
                }

                Result.AddFlags((AST<Program>)Factory.Instance.ToAST(ast.Root), flags);

                if (cancel.IsCancellationRequested)
                {
                    return false;
                }
            }

            return result;
        }

        /// <summary>
        /// Generates configurations for every new program, and registers all the local
        /// modules in each program's configuration.
        /// </summary>
        private bool RegisterModules(out Map<ProgramName, Configuration> programConfigs)
        {
            bool succeeded = true;
            programConfigs = new Map<ProgramName, Configuration>(ProgramName.Compare);
            foreach (var p in Result.Touched)
            {
                Contract.Assert(p.Status == InstallKind.Cached || p.Status == InstallKind.Compiled);
                if (p.Status == InstallKind.Cached)
                {
                    programConfigs.Add(p.Program.Node.Name, (Configuration)p.Program.Node.Config.CompilerData);
                    continue;
                }

                var programNode = p.Program.Node;
                var settings = new Configuration(Env.Parameters, (AST<Config>)p.Program.FindAny(queryMyConfig));
                programNode.Config.CompilerData = settings;
                programConfigs.Add(p.Program.Node.Name, settings);
                p.Program.FindAll(
                    queryProgModule,
                    (path, node) =>
                    {
                        List<Flag> flags;
                        succeeded = settings.RegisterModule(new Location(Factory.Instance.FromAbsPositions(programNode, path)), out flags) & succeeded;
                        Result.AddFlags(p.Program, flags);
                    },
                    cancel);
            }
         
            return succeeded;
        }

        /// <summary>
        /// Applies the settings in each configuration block. Resolves module references
        /// inside module registrations. Resolves "defaults" and plugin references.
        /// Checks that configuration imports are acyclic.
        /// </summary>
        private bool ApplyConfiguration(Map<ProgramName, Configuration> programConfigs)
        {
            bool succeeded = true;
            var configDeps = new DependencyCollection<Location, Unit>(Location.Compare, Unit.Compare);
            foreach (var p in Result.Touched)
            {
                Contract.Assert(p.Status == InstallKind.Cached || p.Status == InstallKind.Compiled);
                if (p.Status == InstallKind.Cached)
                {
                    continue;
                }

                var pconf = programConfigs[p.Program.Node.Name];
                p.Program.FindAll(
                    queryConfig,
                    (path, node) =>
                    {
                        var confOwner = ((LinkedList<ChildInfo>)path).Last.Previous.Value.Node;
                        var confNode = (Config)node;
                        var confAST = (AST<Config>)Factory.Instance.FromAbsPositions(p.Program.Node, path);
                        Configuration conf;
                        if (confNode.CompilerData == null)
                        {
                            conf = new Configuration(Env.Parameters, confAST, configDeps);
                            confNode.CompilerData = conf;
                        }
                        else 
                        {
                            conf = (Configuration)confNode.CompilerData;
                        }

                        List<Flag> flags;
                        if (confOwner.IsModule)
                        {
                            succeeded = conf.RegisterModulesAndLocals(pconf, out flags);
                            Result.AddFlags(p.Program, flags);
                        }

                        succeeded = conf.ApplyConfigurations(confAST, programConfigs, configDeps, out flags);
                        Result.AddFlags(p.Program, flags);
                    },
                    cancel);
            }
            
            int nSCCs;
            int cycleNum = 0;
            foreach (var scc in configDeps.GetSCCs(out nSCCs, cancel))
            {
                if (scc.Kind == DependencyNodeKind.Normal)
                {
                    continue;
                }

                ++cycleNum;
                succeeded = false;                
                foreach (var dep in scc.InternalNodes)
                {
                    Result.AddFlag(
                        (AST<Program>)Factory.Instance.ToAST(dep.Resource.AST.Root),
                        new Flag(
                            SeverityKind.Error,
                            dep.Resource.AST.Node,
                            Constants.BadDepCycle.ToString("configuration", cycleNum),
                            Constants.BadDepCycle.Code));
                }
            }

            return succeeded;
        }
        
        /// <summary>
        /// Tries to resolve all module references and instantiate extentensions. Every module reference will be tagged with a resolving location. Implicit module references appearing
        /// as Ids in ModApplies will not be tagged in this phase, though they will be resolved. Finally, the dependency structure will be checked for 
        /// cycles.
        /// </summary>
        private bool BuildModuleDependencies(Map<ProgramName, Configuration> programConfigs, out DependencyCollection<Location, Location> newModDeps)
        {
            var modDeps = new DependencyCollection<Location, Location>(Location.Compare, Location.Compare);
            bool succeeded = true;
            foreach (var p in Result.Touched)
            {
                Contract.Assert(p.Status == InstallKind.Cached || p.Status == InstallKind.Compiled);
                if (p.Status == InstallKind.Cached)
                {
                    continue;
                }

                var progConf = new Location(p.Program.FindAny(queryMyConfig));                
                p.Program.FindAll(
                    queryProgModule,
                    (path, node) =>
                    {
                        var conf = node.GetModuleConfiguration();
                        List<Flag> flags = new List<Flag>();
                        if (!conf.CreatePlugins(flags))
                        {
                            succeeded = false;
                        }

                        var modLoc = new Location(conf.AttachedAST);
                        modDeps.Add(modLoc);
                        conf.AttachedAST.FindAll(
                            queryModRef,
                            (mpath, mnode) =>
                            {
                                //////// This is all we need to do for all mod references, except the operators of mod applies.
                                //////// Local module references are ignored for the purpose of this calculation.
                                var modRef = (ModRef)mnode;
                                Location res;
                                bool isLoc;
                                var flagCount = flags.Count;
                                if (!conf.TryResolve(modRef, programConfigs, flags, out res, out isLoc))
                                {
                                    succeeded = false;
                                    if (flagCount == flags.Count)
                                    {
                                        var flag = new Flag(
                                                SeverityKind.Error,
                                                modRef,
                                                Constants.UndefinedSymbol.ToString("module", modRef.Name),
                                                Constants.UndefinedSymbol.Code,
                                                ((Program)conf.AttachedAST.Root).Name);
                                        flags.Add(flag);
                                    }

                                    return;
                                }

                                Contract.Assert(modRef.CompilerData == null);
                                modRef.CompilerData = res; 
                                if (!isLoc)
                                {
                                    modDeps.Add(res, modLoc, new Location(Factory.Instance.FromAbsPositions(conf.AttachedAST.Root, mpath)));
                                }                                                                                           
                            });

                        Result.AddFlags(p.Program, flags);
                    },
                    cancel);
            }

            int cycleNum;
            newModDeps = modDeps;
            var topo = modDeps.GetTopologicalSort(out cycleNum, cancel);
            cycleNum = 0;
            foreach (var dep in topo)
            {
                if (dep.Kind == DependencyNodeKind.Normal)
                {
                    continue;
                }

                ++cycleNum;
                foreach (var scc in dep.InternalNodes)
                {
                    succeeded = false;
                    string modName;
                    scc.Resource.AST.Node.TryGetStringAttribute(AttributeKind.Name, out modName);
                    var flag = new Flag(
                            SeverityKind.Error,
                            scc.Resource.AST.Node,
                            Constants.BadDepCycle.ToString(string.Format("module {0}", modName), cycleNum),
                            Constants.BadDepCycle.Code,
                            ((Program)scc.Resource.AST.Root).Name);
                    Result.AddFlag((AST<Program>)Factory.Instance.ToAST(scc.Resource.AST.Root), flag);
                }
            }

            return succeeded;
        }

        private bool EliminateQuotations(bool isQueryContainer)
        {
            bool succeeded = true;
            foreach (var p in Result.Touched)
            {
                Contract.Assert(p.Status == InstallKind.Cached || p.Status == InstallKind.Compiled);
                if (p.Status == InstallKind.Cached)
                {
                    continue;
                }

                var flags = new List<Flag>();
                p.Program.FindAll(
                    queryProgModule,
                    (path, node) =>
                    {
                        succeeded = EliminateQuotations(isQueryContainer, Factory.Instance.FromAbsPositions(p.Program.Node, path), flags) & succeeded;
                    },
                    cancel);

                Result.AddFlags(p.Program, flags);
            }
          
            return succeeded;
        }

        /// <summary>
        /// Tries to eliminate the quotations in a module using parser plugins.
        /// It succeeds, then the compiler data of module is set to the simplified
        /// module definition.
        /// </summary>
        private bool EliminateQuotations(bool isQueryContainer, AST<Node> module, List<Flag> flags)
        {
            var configStack = new Stack<Configuration>();
            var success = new SuccessToken();
            var simplified = module.Compute<Node>(
                (node) =>
                {
                    return ElimQuoteUnfold(node, configStack, success, flags, cancel);
                },
                (node, folds) =>
                {
                    return ElimQuoteFold(node, folds, configStack);
                },
                cancel);

            if (cancel.IsCancellationRequested)
            {
                return false;
            }

            if (success.Result)
            {
                var modData = new ModuleData(Env, new Location(module), simplified, isQueryContainer);
                module.Node.CompilerData = modData;
                simplified.CompilerData = modData;
            }

            return success.Result;
        }

        private static Node ElimQuoteFold(
            Node n,
            IEnumerable<Node> folds,
            Stack<Configuration> configStack)
        {
            if (n.NodeKind == NodeKind.Config)
            {
                return n;
            }

            Configuration conf;
            if (n.TryGetConfiguration(out conf))
            {
                Contract.Assert(configStack.Count > 0 && configStack.Peek() == conf);
                configStack.Pop();
            }

            Node result = n;
            if (folds.IsEmpty<Node>())
            {
                return n;
            }
            else if (n.NodeKind == NodeKind.Quote)
            {
                using (var it = folds.GetEnumerator())
                {
                    it.MoveNext();
                    result = it.Current;
                    Contract.Assert(!it.MoveNext());
                    return result;
                }
            }

            //// TODO: This could be a performance bottle-neck if many children
            //// have children with quotations. In this case, a new ShallowClone
            //// function must implemented, which simultaneously replaces many children.
            bool mF, mC;
            int pos = 0;
            using (var itF = folds.GetEnumerator())
            {
                using (var itC = n.Children.GetEnumerator())
                {
                    while ((mF = itF.MoveNext()) & (mC = itC.MoveNext()))
                    {
                        if (itF.Current != itC.Current)
                        {
                            result = result.ShallowClone(itF.Current, pos);
                        }

                        ++pos;
                    }

                    Contract.Assert(!mF && !mC);
                }
            }

            return result;
        }

        private static IEnumerable<Node> ElimQuoteUnfold(
            Node n, 
            Stack<Configuration> configStack,   
            SuccessToken success, 
            List<Flag> flags,
            CancellationToken cancel)
        {
            if (n.NodeKind == NodeKind.Config)
            {
                yield break;
            }

            Cnst value;
            Configuration conf;
            if (n.TryGetConfiguration(out conf))
            {
                configStack.Push(conf);
            }

            if (n.NodeKind != NodeKind.Quote)
            {
                foreach (var c in n.Children)
                {
                    yield return c;
                }

                yield break;
            }

            Contract.Assert(configStack.Count > 0);
            conf = configStack.Peek();
            if (!conf.TryGetSetting(Configuration.Parse_ActiveParserSetting, out value))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.QuotationError.ToString("No active parser configured."),
                    Constants.QuotationError.Code);
                flags.Add(flag);
                success.Failed();
                yield break;
            }

            IQuoteParser parser;
            if (!conf.TryGetParserInstance(value.GetStringValue(), out parser))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.QuotationError.ToString(string.Format("Cannot find a parser named {0}", value.GetStringValue())),
                    Constants.QuotationError.Code);
                flags.Add(flag);
                success.Failed();
                yield break;
            }

            string unquotePrefix = "";
            bool parseSuccess = true;
            AST<Node> result = null;
            try
            {
                unquotePrefix = parser.UnquotePrefix;
                List<Flag> parseFlags;
                if (!parser.Parse(
                            conf,
                            new QuoteStream((Quote)n, parser.UnquotePrefix, cancel),
                            new SourcePositioner((Quote)n, parser.UnquotePrefix),
                            out result,
                            out parseFlags))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.QuotationError.ToString(string.Empty),
                        Constants.QuotationError.Code);
                    flags.Add(flag);
                    parseSuccess = false;
                }

                if (parseFlags != null)
                {
                    flags.AddRange(parseFlags);
                }

            }
            catch (Exception e)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.PluginException.ToString(Configuration.ParsersCollectionName, value.GetStringValue(), e.Message),
                    Constants.PluginException.Code);
                flags.Add(flag);
                parseSuccess = false;
            }

            if (cancel.IsCancellationRequested)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.QuotationError.ToString("Cancelled quotation parse"),
                    Constants.QuotationError.Code);
                flags.Add(flag);
                parseSuccess = false;
            }

            if (parseSuccess && (result == null || result.FindAny(queryQuote, cancel) != null))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.QuotationError.ToString("Quotation parser did not eliminate quotations."),
                    Constants.QuotationError.Code);
                flags.Add(flag);
                parseSuccess = false;
            }

            if (!parseSuccess)
            {
                success.Failed();
                yield break;
            }

            Quote qn = (Quote)n;
            int childId = 0;
            var unquoteMap = new Map<string, AST<Node>>(string.CompareOrdinal);
            foreach (var c in qn.Contents)
            {
                if (c.NodeKind != NodeKind.QuoteRun)
                {
                    unquoteMap.Add(string.Format("{0}{1}", unquotePrefix, childId), Factory.Instance.ToAST(c));
                    ++childId;
                }
            }

            if (childId == 0)
            {
                yield return result.Node;
            }
            else
            {
                yield return SubstituteEscapes(result, unquoteMap, cancel).Root;
            }
        }

        private static AST<Node> SubstituteEscapes(AST<Node> n, Map<string, AST<Node>> unquoteMap, CancellationToken cancel)
        {
            return n.Substitute(
                NodePredFactory.Instance.MkPredicate((attr, obj) => attr == AttributeKind.Name && unquoteMap.ContainsKey((string)obj)),
                (path) => unquoteMap[((Id)((LinkedList<ChildInfo>)path).Last.Value.Node).Name].Root.NodeKind,
                (path) => ((IInternalClonable)unquoteMap[((Id)((LinkedList<ChildInfo>)path).Last.Value.Node).Name]).DeepClone(true).Root,
                cancel);
        }
    }
}
