namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Numerics;
    using System.Threading;
    using System.Reflection;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using API.Plugins;
    using Common;
    using Common.Extras;

    public sealed class Configuration
    {
        private static Tuple<string, Type, string>[] colDescrs;
        private static Tuple<string, CnstKind, string>[] settingDescrs;

        private static NodePred[] queryMyConfig = new NodePred[] 
        {
            NodePredFactory.Instance.MkPredicate(NodeKind.AnyNodeKind),
            NodePredFactory.Instance.MkPredicate(NodeKind.Config)
        };

        private static NodePred[] queryMySettings = new NodePred[] 
        {
            NodePredFactory.Instance.MkPredicate(NodeKind.AnyNodeKind),
            NodePredFactory.Instance.MkPredicate(NodeKind.Setting)
        };

        private static NodePred[] queryLocalMods = new NodePred[] 
        {
            NodePredFactory.Instance.MkPredicate(NodeKind.AnyNodeKind),
            NodePredFactory.Instance.MkPredicate(NodeKind.Param) |
            NodePredFactory.Instance.MkPredicate(NodeKind.Step) |
            NodePredFactory.Instance.MkPredicate(NodeKind.Update)
        };

        private static Map<string, Func<Setting, List<Flag>, bool>> TopSettingValidators;

        /// <summary>
        /// Names for the various collections
        /// </summary>
        public const string ParsersCollectionName = "parsers";
        public const string ModulesCollectionName = "modules";
        public const string StrategiesCollectionName = "strategies";

        /// <summary>
        /// Names for the various predefined settings
        /// </summary>
        public const string DefaultsSetting = "defaults";
        public const string Parse_ActiveParserSetting = "parse_ActiveParser";
        public const string Parse_ActiveRenderSetting = "parse_ActiveRenderer";
        public const string Compiler_ProductivityCheckSetting = "compiler_ProductivityCheck";
        public const string Solver_ActiveStrategySetting = "solver_ActiveStrategy";
        public const string Solver_RealCostSetting = "solver_RealCost";
        public const string Solver_IntegerCostSetting = "solver_IntegerCost";
        public const string Solver_NaturalCostSetting = "solver_NaturalCost";
        public const string Solver_NegIntegerCostSetting = "solver_NegIntegerCost";
        public const string Solver_PosIntegerCostSetting = "solver_PosIntegerCost";
        public const string Solver_StringCostSetting = "solver_StringCost";
        public const string Proofs_KeepLineNumbersSetting = "proofs_KeepLineNumbers";
        public const string Proofs_MaxLocationsSetting = "proofs_MaxLocations";
        public const string Rule_ClassesSetting = "rule_Classes";
        public const string Rule_WatchSetting = "rule_Watch";

        static Configuration()
        {
            TopSettingValidators = new Map<string, Func<Setting, List<Flag>, bool>>(string.CompareOrdinal);
            TopSettingValidators[Parse_ActiveParserSetting] = ValidateStringSetting;
            TopSettingValidators[Parse_ActiveRenderSetting] = ValidateStringSetting;
            TopSettingValidators[Solver_ActiveStrategySetting] = ValidateStringSetting;
            TopSettingValidators[Compiler_ProductivityCheckSetting] = ValidateStringSetting;
            TopSettingValidators[Rule_ClassesSetting] = ValidateStringSetting;
            TopSettingValidators[Rule_WatchSetting] = ValidateBoolSetting;
            TopSettingValidators[Proofs_KeepLineNumbersSetting] = ValidateBoolSetting;
            TopSettingValidators[Proofs_MaxLocationsSetting] = (s, f) => ValidateIntSetting(s, 1, 256, f);
            TopSettingValidators[Solver_RealCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);
            TopSettingValidators[Solver_IntegerCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);
            TopSettingValidators[Solver_NaturalCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);
            TopSettingValidators[Solver_NegIntegerCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);
            TopSettingValidators[Solver_PosIntegerCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);
            TopSettingValidators[Solver_StringCostSetting] = (s, f) => ValidateIntSetting(s, 0, int.MaxValue, f);

            colDescrs = new Tuple<string, Type, string>[]
            {
                new Tuple<string, Type, string>(
                    ModulesCollectionName, 
                    typeof(AST<Node>),
                    string.Format("A map from names to modules. Use {0}.name = \"name at place.4ml\".", ModulesCollectionName)),

                new Tuple<string, Type, string>(
                    ParsersCollectionName,
                    typeof(IQuoteParser),
                    string.Format("A map from names to parsers. Use {0}.name = \"parserClass at implementation.dll\".", ParsersCollectionName)),

                new Tuple<string, Type, string>(
                    StrategiesCollectionName, 
                    typeof(ISearchStrategy),
                    string.Format("A map from names to search strategies. Use {0}.name = \"strategyClass at implementation.dll\".", StrategiesCollectionName))
            };

            Array.Sort(colDescrs, (x, y) => string.Compare(x.Item1, y.Item1));

            settingDescrs = new Tuple<string, CnstKind, string>[]
            {
                new Tuple<string, CnstKind, string>(
                    DefaultsSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"file.4ml\" to inherit all settings at the file scope of file.4ml.", DefaultsSetting)),

                new Tuple<string, CnstKind, string>(
                    Parse_ActiveParserSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"name\" to use {1}.name as the active quotation parser.", Parse_ActiveParserSetting, ParsersCollectionName)),

                new Tuple<string, CnstKind, string>(
                    Parse_ActiveRenderSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"name\" to use {1}.name as the active quotation render.", Parse_ActiveRenderSetting, ParsersCollectionName)),

                new Tuple<string, CnstKind, string>(
                    Compiler_ProductivityCheckSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"F[i, j, ...], ...\" such that F ::= (T_1, ..., T_n). Checks if rules can produce a value F(..., v_i, ..., v_j, ...) for every v_i : T_i, v_j : T_j.", Compiler_ProductivityCheckSetting)),

                new Tuple<string, CnstKind, string>(
                    Solver_ActiveStrategySetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"name\" to use {1}.name as the active search strategy.", Solver_ActiveStrategySetting, StrategiesCollectionName)),

                new Tuple<string, CnstKind, string>(
                    Solver_RealCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as reals.", Solver_RealCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Solver_IntegerCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as integers.", Solver_IntegerCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Solver_NaturalCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as naturals.", Solver_NaturalCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Solver_NegIntegerCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as negative integers.", Solver_NegIntegerCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Solver_PosIntegerCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as positive integers.", Solver_PosIntegerCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Solver_StringCostSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n such that 0 <= n <= {1} to increase the cost of encoding symbolic constants as strings.", Solver_StringCostSetting, int.MaxValue)),

                new Tuple<string, CnstKind, string>(
                    Proofs_KeepLineNumbersSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"TRUE\" (\"FALSE\") to so proofs can (not) locate the line numbers of the model facts they require.", Proofs_KeepLineNumbersSetting)),

                new Tuple<string, CnstKind, string>(
                    Proofs_MaxLocationsSetting,
                    CnstKind.Numeric,
                    string.Format("Use {0} = n for 1 <= n <= 256 to set the maximum number of locations computed per proof.", Proofs_MaxLocationsSetting)),

                new Tuple<string, CnstKind, string>(
                    Rule_ClassesSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"class1, ..., classn\" to tag a rule with a set of classes.", Rule_ClassesSetting)),

                new Tuple<string, CnstKind, string>(
                    Rule_WatchSetting,
                    CnstKind.String,
                    string.Format("Use {0} = \"TRUE\" (\"FALSE\") to (not) generate an event whenever rules fire.", Rule_WatchSetting)),
            };

            Array.Sort(settingDescrs, (x, y) => string.Compare(x.Item1, y.Item1));
        }

        /// <summary>
        /// These are configurations there were inherited either lexically, or by using the
        /// defaults setting
        /// </summary>
        private LinkedList<Configuration> inheritedConfigs = new LinkedList<Configuration>();

        private Map<string, Cnst> topSettings = new Map<string, Cnst>(string.CompareOrdinal); 
        
        /// <summary>
        /// These map module identifiers to locations. Modules with step / update constructs define
        /// "local modules" by equations. These are stored in locals along with all the equations
        /// that contain the module name on the left-hand side
        /// </summary>
        private Map<string, Location> modules = new Map<string, Location>(string.CompareOrdinal);

        private Map<string, Set<Location>> locals = new Map<string, Set<Location>>(string.CompareOrdinal);

        /// <summary>
        /// These map holds settings related to plugins
        /// </summary>
        private Map<string, PluginSettings> parsers = new Map<string, PluginSettings>(string.CompareOrdinal);

        /// <summary>
        /// These map holds settings related to strategy plugins
        /// </summary>
        private Map<string, PluginSettings> strategies = new Map<string, PluginSettings>(string.CompareOrdinal);

        /// <summary>
        /// Plugins are instantiated per module and shared for the entire module. If this configuration is attached to
        /// a module, then it will have an instance of every plugin visible from this configuration.
        /// </summary>
        private Map<string, IQuoteParser> parserInstances = null;
        private Map<string, ISearchStrategy> strategiesInstances = null;

        /// <summary>
        /// Setters and registration functions.
        /// </summary>
        private Map<string, Func<AST<Setting>, List<Flag>, Map<ProgramName, Configuration>, bool>> registers =
            new Map<string, Func<AST<Setting>, List<Flag>, Map<ProgramName, Configuration>, bool>>(string.CompareOrdinal);

        private Map<string, Func<AST<Setting>, List<Flag>, bool>> setters =
            new Map<string, Func<AST<Setting>, List<Flag>, bool>>(string.CompareOrdinal);

        private Map<string, Map<string, PluginSettings>> plugins =
            new Map<string, Map<string, PluginSettings>>(string.CompareOrdinal);

        private EnvParams envParams;

        public static IEnumerable<Tuple<string, Type, string>> CollectionDescriptions
        {
            get { return colDescrs; }
        }

        public static IEnumerable<Tuple<string, CnstKind, string>> SettingsDescriptions
        {
            get { return settingDescrs; }
        }

        public AST<Node> AttachedAST
        {
            get;
            private set;
        }

        public AST<Node> ConfigAST
        {
            get;
            private set;
        }

        public IEnumerable<Configuration> InheritedConfigurations
        {
            get
            {
                return inheritedConfigs;
            }
        }

        internal Configuration(
                    EnvParams envParams,
                    AST<Config> configAST,
                    DependencyCollection<Location, Unit> configDep = null)
        {
            Contract.Requires(configAST.Root.NodeKind == NodeKind.Program);

            this.envParams = envParams;
            ConfigAST = configAST;
            AttachedAST = Factory.Instance.FromAbsPositions(configAST.Root, configAST.Path.Truncate<ChildInfo>(1));

            registers[ParsersCollectionName] = RegisterParser;
            setters[ParsersCollectionName] = SetParser;
            plugins[ParsersCollectionName] = parsers;

            registers[StrategiesCollectionName] = RegisterStrategy;
            setters[StrategiesCollectionName] = SetStrategy;
            plugins[StrategiesCollectionName] = strategies;

            registers[ModulesCollectionName] = RegisterModule;
            setters[ModulesCollectionName] = SetModule;

            if (configDep != null)
            {
                var path = (LinkedList<ChildInfo>)configAST.Path;
                var crnt = path.Last.Previous.Previous; //// Skip this config's parent
                AST<Config> parentConf;
                int truncAmount = 2;
                while (crnt != null)
                {
                    parentConf = (AST<Config>)Factory.Instance.FromAbsPositions(configAST.Root, configAST.Path.Truncate<ChildInfo>(truncAmount)).FindAny(queryMyConfig);
                    if (parentConf == null)
                    {
                        crnt = crnt.Previous;
                        ++truncAmount;
                        continue;
                    }

                    Contract.Assert(parentConf.Node.CompilerData is Configuration);
                    inheritedConfigs.AddFirst((Configuration)parentConf.Node.CompilerData);
                    configDep.Add(new Location(parentConf), new Location(configAST), default(Unit));
                    break;
                }
            }
        }

        public bool TryGetSetting(string settingName, out Cnst value)
        {
            Contract.Requires(settingName != null);

            if (topSettings.TryFindValue(settingName, out value))
            {
                return true;
            }

            foreach (var conf in inheritedConfigs)
            {
                if (conf.TryGetSetting(settingName, out value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetSetting(string collName, string pluginName, string settingName, out Cnst value)
        {
            Contract.Requires(collName != null && pluginName != null && settingName != null);
            Map<string, PluginSettings> collSettings;
            if (plugins.TryFindValue(collName, out collSettings))
            {
                PluginSettings plugSettings;
                if (collSettings.TryFindValue(pluginName, out plugSettings))
                {
                    if (plugSettings.TryGetSetting(settingName, out value))
                    {
                        return true;
                    }
                }
            }

            value = null;
            foreach (var conf in inheritedConfigs)
            {
                if (conf.TryGetSetting(collName, pluginName, settingName, out value))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetParserInstance(string pluginName, out IQuoteParser plugin)
        {
            Contract.Requires(pluginName != null);
            if (parserInstances != null)
            {
                if (parserInstances.TryFindValue(pluginName, out plugin))
                {
                    return true;
                }
            }

            plugin = null;
            if (AttachedAST.Node.NodeKind == NodeKind.Program || AttachedAST.Node.IsModule)
            {
                return false;
            }

            foreach (var conf in inheritedConfigs)
            {
                if (conf.TryGetParserInstance(pluginName, out plugin))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetStrategyInstance(string pluginName, out ISearchStrategy plugin)
        {
            Contract.Requires(pluginName != null);
            if (strategiesInstances != null)
            {
                if (strategiesInstances.TryFindValue(pluginName, out plugin))
                {
                    return true;
                }
            }

            plugin = null;
            if (AttachedAST.Node.NodeKind == NodeKind.Program || AttachedAST.Node.IsModule)
            {
                return false;
            }

            foreach (var conf in inheritedConfigs)
            {
                if (conf.TryGetStrategyInstance(pluginName, out plugin))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool ApplyConfigurations(
                            AST<Config> config,
                            Map<ProgramName, Configuration> programConfigs,
                            DependencyCollection<Location, Unit> configDeps,
                            out List<Flag> flags)
        {
            Contract.Requires(config.Root.NodeKind == NodeKind.Program);

            var lflags = new List<Flag>();
            var succeeded = true;
            config.FindAll(queryMySettings,
                (path, node) =>
                {
                    succeeded = Set(
                        (AST<Setting>)Factory.Instance.FromAbsPositions(config.Root, path),
                        programConfigs,
                        configDeps,
                        lflags) & succeeded;
                });

            flags = lflags;
            return succeeded;
        }

        internal bool RegisterModule(Location loc, out List<Flag> flags)
        {
            Location other;
            string name;
            loc.AST.Node.TryGetStringAttribute(AttributeKind.Name, out name);
            if (modules.TryFindValue(name, out other))
            {
                flags = new List<Flag>();
                flags.Add(MkDuplicateModuleFlag(name, other.AST.Node, loc.AST.Node));
                return false;
            }

            if (!ASTSchema.Instance.IsId(name, false, false, false, false))
            {
                flags = new List<Flag>();
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        loc.AST.Node,
                        Constants.BadId.ToString(name, "module"),
                        Constants.BadId.Code));
                return false;
            }

            flags = null;
            modules.Add(name, loc);
            return true;
        }

        internal bool RegisterModulesAndLocals(Configuration parent, out List<Flag> flags)
        {
            flags = new List<Flag>();
            var succeeded = true;
            string name;
            bool result;
            foreach (var kv in parent.modules)
            {
                result = kv.Value.AST.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                Contract.Assert(result);
                if (kv.Key != name)
                {
                    continue;
                }

                List<Flag> regFlags;
                succeeded = RegisterModule(kv.Value, out regFlags) & succeeded;
                if (regFlags != null)
                {
                    flags.AddRange(regFlags);
                }
            }

            return RegisterLocals(flags);
        }

        /// <summary>
        /// Tries to find a module with the given name this is declared in the 
        /// program containing this configuration.
        /// </summary>
        internal bool TryResolveLocalModule(string name, out Location loc)
        {
            var progConf = ((Program)AttachedAST.Root).Config.CompilerData as Configuration;
            Contract.Assert(progConf != null);
            if (!progConf.modules.TryFindValue(name, out loc))
            {
                return false;
            }

            return loc.AST.Root == AttachedAST.Root;
        }

        internal bool TryResolve(
            ModRef modRef,
            Map<ProgramName, Configuration> configurations,
            List<Flag> flags,
            out Location loc,
            out bool isLocal)
        {
            if (modRef.Location != null)
            {
                isLocal = false;
                return TryResolveAbsolute(modRef, configurations, flags, out loc);
            }

            if (modules.TryFindValue(modRef.Name, out loc))
            {
                isLocal = false;
                return true;
            }

            Set<Location> locs;
            if (locals.TryFindValue(modRef.Name, out locs))
            {
                Contract.Assert(locs.Count > 0);
                isLocal = true;
                loc = locs.GetSomeElement();
                return true;
            }

            foreach (var inh in inheritedConfigs)
            {
                if (inh.TryResolve(modRef, configurations, flags, out loc, out isLocal))
                {
                    return true;
                }
            }

            isLocal = false;
            return false;
        }

        internal bool CreatePlugins(List<Flag> flags)
        {
            parserInstances = new Map<string, IQuoteParser>(string.CompareOrdinal);
            strategiesInstances = new Map<string, ISearchStrategy>(string.CompareOrdinal);
            return CreatePlugins<IQuoteParser>(ParsersCollectionName, AttachedAST, parserInstances, flags) &
                    CreatePlugins<ISearchStrategy>(StrategiesCollectionName, AttachedAST, strategiesInstances, flags);
        }
       
        private bool Set(AST<Setting> setting, 
                         Map<ProgramName, Configuration> programConfigs,
                         DependencyCollection<Location, Unit> configDeps,
                         List<Flag> flags)
        {
            var name = setting.Node.Key;
            if (name.Fragments.Length == 1)
            {
                if (name.Fragments[0] == DefaultsSetting)
                {
                    return Inherit(setting, programConfigs, configDeps, flags);
                }
                else
                {
                    return TopSet(setting, flags);
                }
            }
            else if (name.Fragments.Length == 2)
            {
                Func<AST<Setting>, List<Flag>, Map<ProgramName, Configuration>, bool> register;
                if (!registers.TryFindValue(name.Fragments[0], out register))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(name.Name, setting.Node.Value.Raw, string.Format("Unknown collection {0}", name.Fragments[0])),
                        Constants.BadSetting.Code);
                    flags.Add(flag);
                    return false;
                }

                return register(setting, flags, programConfigs);
            }
            else if (name.Fragments.Length == 3) 
            {
                Func<AST<Setting>, List<Flag>, bool> setter;
                if (!setters.TryFindValue(name.Fragments[0], out setter))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(name.Name, setting.Node.Value.Raw, string.Format("Unknown collection {0}", name.Fragments[0])),
                        Constants.BadSetting.Code);
                    flags.Add(flag);
                    return false;
                }

                return setter(setting, flags);
            }
            else 
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting.Node,
                    Constants.BadSetting.ToString(name.Name, setting.Node.Value.Raw, "Use collections.plugin = location, or collections.plugin.key = value"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }               
        }

        private bool TopSet(AST<Setting> setting, List<Flag> flags)
        {
            Func<Setting, List<Flag>, bool> validator;
            var name = setting.Node.Key.Name;
            if (!TopSettingValidators.TryFindValue(name, out validator))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting.Node,
                    Constants.BadSetting.ToString(name, setting.Node.Value.Raw, "Invalid setting"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            if (!validator(setting.Node, flags))
            {
                return false;
            }

            Cnst other;
            if (topSettings.TryFindValue(name, out other))
            {
                flags.Add(MkDuplicateSettingFlag(setting.Node.Key, other, setting.Node.Value));
                return false;
            }

            topSettings.Add(name, setting.Node.Value);
            return true;
        }

        private bool Inherit(AST<Setting> setting,
                             Map<ProgramName, Configuration> programConfigs,
                             DependencyCollection<Location, Unit> configDeps,
                             List<Flag> flags)
        {
            var cnst = setting.Node.Value;
            if (cnst.CnstKind != CnstKind.String)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadSetting.ToString(setting.Node.Key.Name, cnst.Raw, "expected a filename"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            ProgramName refProg;
            try
            {
                refProg = new ProgramName(cnst.GetStringValue(), ((Program)AttachedAST.Root).Name);
            }
            catch (Exception e)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadFile.ToString(string.Format("Unable to load file {0}; {1}", cnst.GetStringValue(), e.Message)),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            Configuration inheritedConf;
            if (!programConfigs.TryFindValue(refProg, out inheritedConf))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadFile.ToString(string.Format("Could not find file {0}", cnst.GetStringValue())),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            inheritedConfigs.AddFirst(inheritedConf);
            configDeps.Add(new Location(inheritedConf.ConfigAST), new Location(ConfigAST), default(Unit));
            return true;
        }

        private bool TryResolveAbsolute(
            ModRef modRef,
            Map<ProgramName, Configuration> configurations, 
            List<Flag> flags,
            out Location loc)
        {
            loc = default(Location);
            ProgramName refProgram = null;
            try
            {
                refProgram = new ProgramName(modRef.Location, ((Program)AttachedAST.Root).Name);
            }
            catch (Exception e)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    modRef,
                    Constants.BadFile.ToString(string.Format("Unable to load module {0} at {1}; {2}", modRef.Name, modRef.Location, e.Message)),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            Configuration otherConfig;
            if (!configurations.TryFindValue(refProgram, out otherConfig))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    modRef,
                    Constants.BadFile.ToString(string.Format("Unable to load file {0}", modRef.Location)),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            if (!otherConfig.modules.TryFindValue(modRef.Name, out loc))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    modRef,
                    Constants.UndefinedSymbol.ToString("module", modRef.Name),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }
            
        private bool RegisterParser(AST<Setting> setting, List<Flag> flags, Map<ProgramName, Configuration> configurations)
        {
            if (!AttachedAST.Node.IsModule && AttachedAST.Node.NodeKind != NodeKind.Program)
            {
                var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(
                            setting.Node.Key.Name,
                            setting.Node.Value.Raw,
                            "Cannot register new plugins at this point."),
                        Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            Type type;
            ConstructorInfo constr;
            if (!TryLoadPlugin<IQuoteParser>(setting, out type, out constr, flags))
            {
                return false;
            }

            PluginSettings psettings;
            if (!GetPluginSettings(parsers, setting, out psettings, flags))
            {
                return false;
            }

            if (psettings.Type == null)
            {
                psettings.Type = type;
                psettings.Constructor = constr;
                psettings.Registration = setting;
                return true;
            }

            flags.Add(MkDuplicateSettingFlag(setting.Node.Key, psettings.Registration.Node, setting.Node.Value));
            return false;
        }

        private bool RegisterStrategy(AST<Setting> setting, List<Flag> flags, Map<ProgramName, Configuration> configurations)
        {
            if (!AttachedAST.Node.IsModule && AttachedAST.Node.NodeKind != NodeKind.Program)
            {
                var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(
                            setting.Node.Key.Name,
                            setting.Node.Value.Raw,
                            "Cannot register new plugins at this point."),
                        Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            Type type;
            ConstructorInfo constr;
            if (!TryLoadPlugin<ISearchStrategy>(setting, out type, out constr, flags))
            {
                return false;
            }

            PluginSettings psettings;
            if (!GetPluginSettings(strategies, setting, out psettings, flags))
            {
                return false;
            }

            if (psettings.Type == null)
            {
                psettings.Type = type;
                psettings.Constructor = constr;
                psettings.Registration = setting;
                return true;
            }

            flags.Add(MkDuplicateSettingFlag(setting.Node.Key, psettings.Registration.Node, setting.Node.Value));
            return false;
        }

        private bool RegisterModule(AST<Setting> setting, List<Flag> flags, Map<ProgramName, Configuration> configurations)
        {
            if (!AttachedAST.Node.IsModule && AttachedAST.Node.NodeKind != NodeKind.Program)
            {
                var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(
                            setting.Node.Key.Name,
                            setting.Node.Value.Raw,
                            "Cannot register new modules at this point."),
                        Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            var modName = setting.Node.Key.Fragments[1];
            if (!ASTSchema.Instance.IsId(modName, false, false, false, false))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting.Node.Key,
                    Constants.BadId.ToString(modName, "module"),
                    Constants.BadId.Code);
                flags.Add(flag);
                return false;
            }

            var cnst = setting.Node.Value;
            if (cnst.CnstKind != CnstKind.String)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadSetting.ToString(setting.Node.Key.Name, cnst.Raw, "expected a module reference"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            AST<ModRef> modRef;
            if (!Factory.Instance.TryParseReference(cnst.GetStringValue(), out modRef, cnst.Span) ||
                modRef.Node.Location == null || modRef.Node.Rename != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadSetting.ToString(setting.Node.Key.Name, cnst.Raw, "expected a module reference"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            Location modLoc;
            if (!TryResolveAbsolute(modRef.Node, configurations, flags, out modLoc))
            {
                return false;
            }

            string refModName;
            var result = modLoc.AST.Node.TryGetStringAttribute(AttributeKind.Name, out refModName);
            Contract.Assert(result);
            if (modRef.Node.Name != refModName)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.UndefinedSymbol.ToString("module", modRef.Node.Name),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }

            Location existingDef;
            if (modules.TryFindValue(modName, out existingDef))
            {
                var flag = MkDuplicateModuleFlag(modName, existingDef.AST.Node, setting.Node);
                flags.Add(flag);
                return false;
            }

            Set<Location> existingDefs;
            if (locals.TryFindValue(modName, out existingDefs) && existingDefs.Count > 0)
            {
                var flag = MkDuplicateModuleFlag(modName, existingDefs.GetSomeElement().AST.Node, setting.Node);
                flags.Add(flag);
                return false;
            }

            if (modName != refModName)
            {
                var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadSetting.ToString(
                            setting.Node.Key.Name,
                            setting.Node.Value.Raw,
                            string.Format("Use {0}.{1} = \"{1} at ...\"", ModulesCollectionName, refModName)),
                        Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            modules.Add(modName, modLoc);
            return true;
        }

        private bool SetParser(AST<Setting> setting, List<Flag> flags)
        {
            PluginSettings plugSettings;
            if (!GetPluginSettings(parsers, setting, out plugSettings, flags))
            {
                return false;
            }

            return plugSettings.Set(setting, flags);
        }

        private bool SetStrategy(AST<Setting> setting, List<Flag> flags)
        {
            PluginSettings plugSettings;
            if (!GetPluginSettings(strategies, setting, out plugSettings, flags))
            {
                return false;
            }

            return plugSettings.Set(setting, flags);
        }

        private bool SetModule(AST<Setting> setting, List<Flag> flags)
        {
            var flag = new Flag(
                    SeverityKind.Error,
                    setting.Node,
                    Constants.BadSetting.ToString(
                        setting.Node.Key.Name,
                        setting.Node.Value.Raw,
                        "Module does not accept this setting"),
                    Constants.BadSetting.Code);
            flags.Add(flag);
            return false;
        }

        private bool GetPluginSettings(
            Map<string, PluginSettings> map, 
            AST<Setting> setting,
            out PluginSettings plugSettings,
            List<Flag> flags)
        {
            var name = setting.Node.Key.Fragments[1];
            if (!ASTSchema.Instance.IsId(name, false, false, false, false))
            {
                var flag = new Flag(
                        SeverityKind.Error,
                        setting.Node,
                        Constants.BadId.ToString(name, "setting"),
                        Constants.BadId.Code);
                flags.Add(flag);
                plugSettings = null;
                return false;
            }

            if (map.TryFindValue(name, out plugSettings))
            {
                return true;
            }

            plugSettings = new PluginSettings(this, name);
            map.Add(name, plugSettings);
            return true;
        }

        private bool TryLoadPlugin<T>(AST<Setting> setting, out Type pluginType, out ConstructorInfo constr, List<Flag> flags)
        {
            bool succeeded = true;
            var plugName = setting.Node.Key.Fragments[1];
            if (!ASTSchema.Instance.IsId(plugName, false, false, false, false))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting.Node.Key,
                    Constants.BadId.ToString(plugName, "plugin"),
                    Constants.BadId.Code);
                flags.Add(flag);
                succeeded = false;
            }

            var cnst = setting.Node.Value;
            if (cnst.CnstKind != CnstKind.String)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadSetting.ToString(setting.Node.Key.Name, cnst.Raw, "expected a reference string"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                succeeded = false;
            }

            constr = null;
            pluginType = null;
            if (!succeeded)
            {
                return false;
            }

            AST<ModRef> modRef;
            if (!Factory.Instance.TryParseReference(cnst.GetStringValue(), out modRef, cnst.Span) ||
                modRef.Node.Location == null ||
                modRef.Node.Rename != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadSetting.ToString(setting.Node.Key.Name, cnst.Raw, "expected a reference string"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            try
            {
                var absUri = new Uri(((Program)AttachedAST.Root).Name.Uri, modRef.Node.Location);
                var qptype = typeof(T);
                var assm = System.Reflection.Assembly.LoadFrom(absUri.LocalPath);
                var types = assm.GetExportedTypes();
                foreach (var t in types)
                {
                    if (t.Name == modRef.Node.Name ||
                        t.FullName == modRef.Node.Name)
                    {
                        if (!qptype.IsAssignableFrom(t))
                        {
                            continue;
                        }

                        constr = t.GetConstructor(Type.EmptyTypes);
                        if (constr == null)
                        {
                            continue;
                        }

                        var testInstance = (T)constr.Invoke(null);
                        pluginType = t;
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadFile.ToString(string.Format("Unable to load plugin {0} at {1}; {2}", modRef.Node.Name, modRef.Node.Location, e.Message)),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            if (pluginType == null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    cnst,
                    Constants.BadFile.ToString(string.Format("Could not find plugin {0} at {1}", modRef.Node.Name, modRef.Node.Location)),
                    Constants.BadFile.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private Flag MkDuplicateSettingFlag(Id setting, Node v1, Node v2, ProgramName p1 = null)
        {
            if (p1 == null)
            {
                p1 = ((Program)AttachedAST.Root).Name;
            }

            return new Flag(
                    SeverityKind.Error,
                    v2,
                    Constants.DuplicateDefs.ToString(
                        string.Format("setting {0}", setting.Name),
                        string.Format(
                            "{0} ({1}, {2})",
                           p1.ToString(envParams),
                           v1.Span.StartLine,
                           v1.Span.StartCol),
                        string.Format(
                            "{0} ({1}, {2})",
                           ((Program)AttachedAST.Root).Name.ToString(envParams),
                           v2.Span.StartLine,
                           v2.Span.StartCol)),
                    Constants.DuplicateDefs.Code);
        }

        private Flag MkDuplicateModuleFlag(string module, Node v1, Node v2)
        {
            return new Flag(
                    SeverityKind.Error,
                    v2,
                    Constants.DuplicateDefs.ToString(
                        string.Format("module {0}", module),
                        string.Format(
                            "{0} ({1}, {2})",
                           ((Program)AttachedAST.Root).Name.ToString(envParams),
                           v1.Span.StartLine,
                           v1.Span.StartCol),
                        string.Format(
                            "{0} ({1}, {2})",
                           ((Program)AttachedAST.Root).Name.ToString(envParams),
                           v2.Span.StartLine,
                           v2.Span.StartCol)),
                    Constants.DuplicateDefs.Code);
        }

        /// <summary>
        /// For transform systems and machines, registers all the local
        /// modules from equations/updates/signatures
        /// </summary>
        /// <returns></returns>
        private bool RegisterLocals(List<Flag> flags)
        {
            if (AttachedAST.Node.NodeKind != NodeKind.TSystem &&
                AttachedAST.Node.NodeKind != NodeKind.Machine)
            {
                return true;
            }

            bool succeeded = true;
            AttachedAST.FindAll(queryLocalMods,
                (path, node) =>
                {
                    switch (node.NodeKind)
                    {
                        case NodeKind.Param:
                            {
                                var p = (Param)node;
                                if (p.Type.NodeKind == NodeKind.ModRef)
                                {
                                    succeeded = RegisterLocal(
                                        ((ModRef)p.Type).Rename,
                                        new Location(Factory.Instance.FromAbsPositions(AttachedAST.Root, path)),
                                        flags);
                                }

                                break;
                            }
                        case NodeKind.Step:
                            {
                                var s = (Step)node;
                                var loc = new Location(Factory.Instance.FromAbsPositions(AttachedAST.Root, path));
                                foreach (var id in s.Lhs)
                                {
                                    succeeded = RegisterLocal(id.Name, loc, flags);
                                }

                                break;
                            }
                        case NodeKind.Update:
                            {
                                var u = (Update)node;
                                var loc = new Location(Factory.Instance.FromAbsPositions(AttachedAST.Root, path));
                                foreach (var id in u.States)
                                {
                                    succeeded = RegisterLocal(id.Name, loc, flags);
                                }

                                break;
                            }
                        default:
                            throw new NotImplementedException();
                    }
                });

            return succeeded;
        }

        private bool RegisterLocal(string name, Location loc, List<Flag> flags)
        {
            Location other;
            if (modules.TryFindValue(name, out other))
            {
                flags.Add(MkDuplicateModuleFlag(name, other.AST.Node, loc.AST.Node));
                return false;
            }

            Set<Location> locs;
            if (!locals.TryFindValue(name, out locs))
            {
                locs = new Set<Location>(Location.Compare);
                locals.Add(name, locs);
            }

            locs.Add(loc);
            return true;
        }

        private bool CreatePlugins<T>(string collName, AST<Node> attached, Map<string, T> extMap, List<Flag> flags)
        {
            var succeeded = true;
            var pluginSettings = plugins[collName];
            foreach (var kv in pluginSettings)
            {
                if (extMap.ContainsKey(kv.Key) || kv.Value.Constructor == null)
                {
                    continue;
                }

                try
                {
                    var facInst = kv.Value.Constructor.Invoke(null);
                    var mi = kv.Value.Type.GetMethod("CreateInstance", new Type[]{ typeof(AST<Node>), typeof(string), typeof(string) });
                    if (mi == null)
                    {
                        throw new NotImplementedException(
                            string.Format("Plugin {0} does not implement CreateInstance", kv.Value.Registration.Node.Value.GetStringValue()));
                    }

                    var inst = (T)mi.Invoke(facInst, new object[]{ attached, collName, kv.Key });
                    extMap.Add(kv.Key, inst);                                                            
                }
                catch (Exception e)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        attached.Node,
                        Constants.PluginException.ToString(collName, kv.Key, e.Message),
                        Constants.PluginException.Code,
                        ((Program)AttachedAST.Root).Name);
                    flags.Add(flag);
                    succeeded = false;
                    continue;
                }                
            }

            foreach (var inh in inheritedConfigs)
            {
                succeeded = inh.CreatePlugins<T>(collName, attached, extMap, flags) & succeeded;
            }
                        
            return succeeded;
        }

        private static bool ValidateIntSetting(Setting setting, BigInteger minValue, BigInteger maxValue, List<Flag> flags)
        {
            Rational rat;
            if (setting.Value.CnstKind != CnstKind.Numeric ||
                !(rat = (Rational)setting.Value.Raw).IsInteger ||
                rat.Numerator < minValue ||
                rat.Numerator > maxValue)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting,
                    Constants.BadSetting.ToString(setting.Key.Name, setting.Value.Raw, string.Format("Expected an integer value in {0}..{1}", minValue, maxValue)),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private static bool ValidateStringSetting(Setting setting, List<Flag> flags)
        {
            if (setting.Value.CnstKind != CnstKind.String)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting,
                    Constants.BadSetting.ToString(setting.Key.Name, setting.Value.Raw, "Expected a string value"),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private static bool ValidateBoolSetting(Setting setting, List<Flag> flags)
        {
            if (setting.Value.CnstKind != CnstKind.String)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting,
                    Constants.BadSetting.ToString(setting.Key.Name, setting.Value.Raw, "Expected either \"TRUE\" or \"FALSE\""),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }

            var strVal = setting.Value.GetStringValue();
            if (strVal != API.ASTQueries.ASTSchema.Instance.ConstNameTrue && 
                strVal != API.ASTQueries.ASTSchema.Instance.ConstNameFalse)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    setting,
                    Constants.BadSetting.ToString(setting.Key.Name, setting.Value.Raw, "Expected either \"TRUE\" or \"FALSE\""),
                    Constants.BadSetting.Code);
                flags.Add(flag);
                return false;
            }
           
            return true;
        }

        internal class PluginSettings
        {
            private Map<string, Cnst> settings = new Map<string, Cnst>(string.CompareOrdinal);

            public Configuration Owner
            {
                get;
                private set;
            }
       
            public string Name
            {
                get;
                private set;
            }

            public AST<Setting> Registration
            {
                get;
                set;
            }

            public Type Type
            {
                get;
                set;
            }

            public ConstructorInfo Constructor
            {
                get;
                set;
            }

            public PluginSettings(Configuration owner, string name)
            {
                Owner = owner;
                Name = name;
            }

            public bool TryGetSetting(string settingName, out Cnst value)
            {
                if (settings.TryFindValue(settingName, out value))
                {
                    return true;
                }

                return false;
            }

            public bool Set(AST<Setting> setting, List<Flag> flags)
            {
                Cnst value;
                if (!ASTSchema.Instance.IsId(setting.Node.Key.Fragments[2], false, false, false, false))
                {
                    var flag = new Flag(
                            SeverityKind.Error,
                            setting.Node,
                            Constants.BadId.ToString(
                                setting.Node.Key.Fragments[2],
                                "setting"
                            ),
                            Constants.BadId.Code);
                    flags.Add(flag);
                    return false;
                }

                if (settings.TryFindValue(setting.Node.Key.Fragments[2], out value))
                {
                    var flag = Owner.MkDuplicateSettingFlag(setting.Node.Key, value, setting.Node.Value); 
                    flags.Add(flag);
                    return false;
                }

                settings.Add(setting.Node.Key.Fragments[2], setting.Node.Value);
                return true;
            }
        }
    }
}
