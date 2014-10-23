namespace Microsoft.Formula.CommandLine
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;

    using API;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;

    /// <summary>
    /// The command interface.
    /// </summary>
    public class CommandInterface : IDisposable
    {
        private enum WatchLevelKind { Off, On, Prompt };

        private static readonly char[] cmdSplitChars = new char[] { ' ' };
        public const string ExitCommand = "exit";
        public const string ExitShortCommand = "x";

        private const string BusyMsg = "Busy; cancel or wait until operation completes";
        private const string UnkCmdMsg = "Unknown command '{0}'";
        private const string UnkSwitchMsg = "Unknown switch '{0}'";
        private const string NotDefVarMsg = "The variable '{0}' is not defined";
        private const string ParseErrMsg = "Could not parse term";

        private const string SetInfoMsg = "Sets a variable. Use: set var term.";
        private const string DelInfoMsg = "Deletes a variable. Use: del var.";
        private const string HelpInfoMsg = "Prints this message.";
        private const string ExitInfoMsg = "Exits the interface loop.";
        private const string SaveMsg = "Saves the module modname into file.";
        private const string LSInfoMsg = "Lists environment objects. Use: ls [vars | progs | tasks]";
        private const string LoadMsg = "Loads and compiles a file that is not yet loaded. Use: load filename";
        private const string UnloadMsg = "Unloads and an installed program and all dependent programs. Use: unload [prog | *]";
        private const string ReloadMsg = "Reloads and an installed program and all dependent programs. Use: reload [prog | *]";
        private const string PrintMsg = "Prints the installed program with the given name. Use: print progname";
        private const string DetailsMsg = "Prints details about the compiled module with the given name. Use: det modname";
        private const string TypesMsg = "Prints inferred variable types. Use: types modname";
        private const string RenderMsg = "Tries to render the module. Use: render modname";
        private const string VerboseMsg = "Changes verbosity. Use: verbose (on | off)";
        private const string WaitMsg = "Changes waiting behavior. Use: wait (on | off) to block until task completes";
        private const string QueryMsg = "Start a query task. Use: query model goals";
        private const string SolveMsg = "Start a solve task. Use: solve partial_model max_sols goals";
        private const string ApplyMsg = "Start an apply task. Use: apply transformstep";
        private const string StatsMsg = "Prints task statistics. Use: stats task_id [top_k_rule]";
        private const string GenDataMsg = "Generate C# data model. Use: generate modname";
        private const string TruthMsg = "Test if a ground term is derivable under a model/apply. Use: truth task_id [term]";
        private const string ProofMsg = "Enumerate proofs that a ground term is derivable under a model/apply. Use: proof task_id [term]";
        private const string ExtractMsg = "Extract and install a result. Use: extract (app_id | solv_id n) output_name [render_class render_dll]";
        private const string DelVarMsg = "Deleted variable '{0}'";
        private const string ConfigHelpMsg = "Provides help about module configurations and settings";
        private const string WatchMsg = "Use: watch [off | on | prompt] to control watch behavior";

        private SpinLock cmdLock = new SpinLock();
        private bool isCmdLocked = false;
        private CancellationTokenSource canceler = null;

        private SortedDictionary<string, Command> cmdMap =
            new SortedDictionary<string, Command>();

        private SortedDictionary<string, AST<Node>> cmdVars =
            new SortedDictionary<string, AST<Node>>();

        /// <summary>
        /// The order in which programs were loaded with the load command.
        /// </summary>
        private LinkedList<ProgramName> loadOrder = new 
            LinkedList<ProgramName>();

        /// <summary>
        /// A non-blocking thread safe object for writing messages.
        /// </summary>
        private IMessageSink sink;

        /// <summary>
        /// A (possibly blocking) thread safe object for getting choices from client
        /// </summary>
        private IChooser chooser;

        /// <summary>
        /// True when the interface is executing options from the
        /// command line.
        /// </summary>
        private bool exeOptions = false;

        /// <summary>
        /// Controls verbosity of interface
        /// </summary>
        private bool isVerboseOn = true;

        /// <summary>
        /// The current watch level of the prompt.
        /// </summary>
        private WatchLevelKind watchLevel = WatchLevelKind.Off;
        private SpinLock watchLevelLock = new SpinLock();

        /// <summary>
        /// This event blocks continuation of executers while in "watch prompt" mode.
        /// </summary>
        private AutoResetEvent promptStepEvent = new AutoResetEvent(true);

        private WatchLevelKind WatchLevel
        {
            get
            {
                bool gotLock = false;
                try
                {
                    watchLevelLock.Enter(ref gotLock);
                    return watchLevel;
                }
                finally
                {
                    if (gotLock)
                    {
                        watchLevelLock.Exit();
                    }
                }
            }

            set
            {
                bool gotLock = false;
                try
                {
                    watchLevelLock.Enter(ref gotLock);
                    watchLevel = value;
                }
                finally
                {
                    if (gotLock)
                    {
                        watchLevelLock.Exit();
                    }
                }
            }
        }

        /// <summary>
        /// The environment used by the interface
        /// </summary>
        private Env env;

        /// <summary>
        /// The task manager
        /// </summary>
        private TaskManager taskManager;

        private API.Nodes.Program focus = null;
        private SpinLock focusLock = new SpinLock();
        private API.Nodes.Program Focus
        {
            get
            {
                bool gotLock = false;
                try
                {
                    focusLock.Enter(ref gotLock);
                    return focus;
                }
                finally
                {
                    if (gotLock)
                    {
                        focusLock.Exit();
                    }
                }
            }

            set
            {
                bool gotLock = false;
                try
                {
                    focusLock.Enter(ref gotLock);
                    focus = value;
                }
                finally
                {
                    if (gotLock)
                    {
                        focusLock.Exit();
                    }
                }
            }
        }

        public CommandInterface(IMessageSink sink, IChooser chooser, EnvParams envParams = null)
        {
            Contract.Requires(sink != null && chooser != null);
            env = new Env(envParams);
            this.sink = sink;
            this.chooser = chooser;
            
            var exitCmd = new Command("exit", "x", (x) => { }, ExitInfoMsg);
            cmdMap.Add(exitCmd.Name, exitCmd);
            cmdMap.Add(exitCmd.ShortName, exitCmd);

            var helpCmd = new Command("help", "h", DoHelp, HelpInfoMsg);
            cmdMap.Add(helpCmd.Name, helpCmd);
            cmdMap.Add(helpCmd.ShortName, helpCmd);

            var setCmd = new Command("set", "s", DoSet, SetInfoMsg);
            cmdMap.Add(setCmd.Name, setCmd);
            cmdMap.Add(setCmd.ShortName, setCmd);

            var delCmd = new Command("del", "d", DoDel, DelInfoMsg);
            cmdMap.Add(delCmd.Name, delCmd);
            cmdMap.Add(delCmd.ShortName, delCmd);

            var lsCmd = new Command("list", "ls", DoLS, LSInfoMsg);
            cmdMap.Add(lsCmd.Name, lsCmd);
            cmdMap.Add(lsCmd.ShortName, lsCmd);

            var ldCmd = new Command("load", "l", DoLoad, LoadMsg);
            cmdMap.Add(ldCmd.Name, ldCmd);
            cmdMap.Add(ldCmd.ShortName, ldCmd);

            var ulCmd = new Command("unload", "ul", DoUnload, UnloadMsg);
            cmdMap.Add(ulCmd.Name, ulCmd);
            cmdMap.Add(ulCmd.ShortName, ulCmd);

            var rlCmd = new Command("reload", "rl", DoReload, ReloadMsg);
            cmdMap.Add(rlCmd.Name, rlCmd);
            cmdMap.Add(rlCmd.ShortName, rlCmd);

            var saveCmd = new Command("save", "sv", DoSave, SaveMsg);
            cmdMap.Add(saveCmd.Name, saveCmd);
            cmdMap.Add(saveCmd.ShortName, saveCmd);

            var printCmd = new Command("print", "p", DoPrint, PrintMsg);
            cmdMap.Add(printCmd.Name, printCmd);
            cmdMap.Add(printCmd.ShortName, printCmd);

            var renderCmd = new Command("render", "r", DoRender, RenderMsg);
            cmdMap.Add(renderCmd.Name, renderCmd);
            cmdMap.Add(renderCmd.ShortName, renderCmd);

            var detCmd = new Command("det", "dt", DoDetails, DetailsMsg);
            cmdMap.Add(detCmd.Name, detCmd);
            cmdMap.Add(detCmd.ShortName, detCmd);

            var verCmd = new Command("verbose", "v", DoVerbose, VerboseMsg);
            cmdMap.Add(verCmd.Name, verCmd);
            cmdMap.Add(verCmd.ShortName, verCmd);

            var waitCmd = new Command("wait", "w", DoWait, WaitMsg);
            cmdMap.Add(waitCmd.Name, waitCmd);
            cmdMap.Add(waitCmd.ShortName, waitCmd);

            var watchCmd = new Command("watch", "wch", DoWatch, WatchMsg);
            cmdMap.Add(watchCmd.Name, watchCmd);
            cmdMap.Add(watchCmd.ShortName, watchCmd);

            var typesCmd = new Command("types", "typ", DoTypes, TypesMsg);
            cmdMap.Add(typesCmd.Name, typesCmd);
            cmdMap.Add(typesCmd.ShortName, typesCmd);

            var queryCmd = new Command("query", "qr", DoQuery, QueryMsg);
            cmdMap.Add(queryCmd.Name, queryCmd);
            cmdMap.Add(queryCmd.ShortName, queryCmd);

            var solveCmd = new Command("solve", "sl", DoSolve, SolveMsg);
            cmdMap.Add(solveCmd.Name, solveCmd);
            cmdMap.Add(solveCmd.ShortName, solveCmd);

            var truthCmd = new Command("truth", "tr", DoTruth, TruthMsg);
            cmdMap.Add(truthCmd.Name, truthCmd);
            cmdMap.Add(truthCmd.ShortName, truthCmd);

            var proofCmd = new Command("proof", "pr", DoProof, ProofMsg);
            cmdMap.Add(proofCmd.Name, proofCmd);
            cmdMap.Add(proofCmd.ShortName, proofCmd);

            var applyCmd = new Command("apply", "ap", DoApply, ApplyMsg);
            cmdMap.Add(applyCmd.Name, applyCmd);
            cmdMap.Add(applyCmd.ShortName, applyCmd);

            var statsCmd = new Command("stats", "st", DoStats, StatsMsg);
            cmdMap.Add(statsCmd.Name, statsCmd);
            cmdMap.Add(statsCmd.ShortName, statsCmd);

            var generateCmd = new Command("generate", "gn", DoGenerate, GenDataMsg);
            cmdMap.Add(generateCmd.Name, generateCmd);
            cmdMap.Add(generateCmd.ShortName, generateCmd);

            var extractCmd = new Command("extract", "ex", DoExtract, ExtractMsg);
            cmdMap.Add(extractCmd.Name, extractCmd);
            cmdMap.Add(extractCmd.ShortName, extractCmd);

            var configHelpCmd = new Command("confhelp", "ch", DoConfigHelp, ConfigHelpMsg);
            cmdMap.Add(configHelpCmd.Name, configHelpCmd);
            cmdMap.Add(configHelpCmd.ShortName, configHelpCmd);

            taskManager = new TaskManager();
        }

        public bool DoCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                promptStepEvent.Set();
                return true;
            }

            if (!GetCommandLock())
            {
                sink.WriteMessageLine(BusyMsg, SeverityKind.Warning);
                PrintPrompt();
                return false;
            }

            DoCommandLocked(command);
            ReleaseCommandLock();
            return true;
        }

        public void Cancel()
        {
            bool gotLock = false;
            try
            {
                cmdLock.Enter(ref gotLock);
                if (canceler != null)
                {
                    canceler.Cancel();
                }

                env.Cancel();
            }
            finally
            {
                if (gotLock)
                {
                    cmdLock.Exit();
                }
            }
        }

        public void Dispose()
        {
        }

        internal bool DoOptions(out bool isExit)
        {
            isExit = false;

            if (!GetCommandLock())
            {
                sink.WriteMessageLine(BusyMsg);
                PrintPrompt();
                return false;
            }
            
            int errPos;
            Options opts;
            string cmdStr;

            if (!OptionParser.Parse(out opts, out errPos, out cmdStr))
            {
                sink.WriteMessageLine("");
                sink.WriteMessageLine("Could not parse command line arguments", SeverityKind.Error);
                sink.WriteMessageLine(string.Format("Input: {0}", cmdStr), SeverityKind.Info);
                sink.WriteMessageLine(string.Format("Pos  : {0}^", errPos == 0 ? string.Empty : new string(' ', errPos)),
                                      SeverityKind.Info);
                PrintPrompt();
            }
            else
            {
                exeOptions = true;
                PrintPrompt();
                foreach (var opt in opts.OptionLists)
                {
                    if (opt.Item1 == ExitCommand ||
                        opt.Item1 == ExitShortCommand)
                    {
                        sink.WriteMessageLine(opt.Item1);
                        isExit = true;
                        break;
                    }
                    else if (opt.Item2.Count == 0)
                    {
                        sink.WriteMessageLine(opt.Item1);
                        DoCommandLocked(opt.Item1);
                    }
                    else
                    {
                        foreach (var val in opt.Item2)
                        {
                            sink.WriteMessageLine(string.Format("{0} {1}", opt.Item1, val.Item2));
                            DoCommandLocked(string.Format("{0} {1}", opt.Item1, val.Item2));

                            if (canceler.IsCancellationRequested)
                            {
                                sink.WriteMessageLine("Commands canceled by user", SeverityKind.Error);
                                break;
                            }
                        }
                    }

                    if (canceler.IsCancellationRequested)
                    {
                        sink.WriteMessageLine("Commands canceled by user", SeverityKind.Error);
                        break;
                    }
                }

                exeOptions = false;
            }

            ReleaseCommandLock();
            return true;
        }

        private void DoCommandLocked(string command)
        {
            var cmdSplit = command.Trim().Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            Command cmd;
            if (!cmdMap.TryGetValue(cmdSplit[0], out cmd))
            {
                sink.WriteMessageLine(string.Format(UnkCmdMsg, cmdSplit[0]), SeverityKind.Warning);
                PrintPrompt();
                return;
            }

            var start = DateTime.Now;
            cmd.Action(cmdSplit.Length == 2 ? cmdSplit[1] : string.Empty);
            var time = DateTime.Now - start;

            if (isVerboseOn)
            {
                sink.WriteMessage(string.Format("{0:F2}s.", time.TotalSeconds), SeverityKind.Info);
            }

            PrintPrompt();
        }

        private void DoVerbose(string s)
        {
            if (s.StartsWith("on"))
            {
                isVerboseOn = true;
                sink.WriteMessageLine("verbose on");
            }
            else if (s.StartsWith("off"))
            {
                isVerboseOn = false;
                sink.WriteMessageLine("verbose off");
            }
            else
            {
                sink.WriteMessageLine(VerboseMsg, SeverityKind.Warning);
            }
        }

        private void DoWait(string s)
        {
            if (s.StartsWith("on"))
            {
                taskManager.IsWaitOn = true;
                sink.WriteMessageLine("wait on");
            }
            else if (s.StartsWith("off"))
            {
                taskManager.IsWaitOn = false;
                sink.WriteMessageLine("wait off");
            }
            else
            {
                sink.WriteMessageLine(WaitMsg, SeverityKind.Warning);
            }
        }

        private void DoWatch(string s)
        {
            promptStepEvent.Set();

            if (s.StartsWith("on"))
            {
                WatchLevel = WatchLevelKind.On;
                sink.WriteMessageLine("watch on");
            }
            else if (s.StartsWith("off"))
            {
                WatchLevel = WatchLevelKind.Off;
                sink.WriteMessageLine("watch off");
            }
            else if (s.StartsWith("prompt"))
            {
                WatchLevel = WatchLevelKind.Prompt;
                sink.WriteMessageLine("watch prompt");
            }
            else if (string.IsNullOrEmpty(s))
            {
                sink.WriteMessageLine(string.Format("watch {0}", WatchLevel.ToString().ToLowerInvariant()));
            }
            else
            {
                sink.WriteMessageLine(WatchMsg, SeverityKind.Warning);
            }
        }

        private void DoStats(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length == 0)
            {
                sink.WriteMessageLine(StatsMsg, SeverityKind.Warning);
                return;
            }

            int taskId;
            if (!int.TryParse(cmdParts[0], out taskId))
            {
                sink.WriteMessageLine(string.Format("{0} is not a task id", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            int maxCount = 0;
            if (cmdParts.Length == 2 && (!int.TryParse(cmdParts[1], out maxCount) || maxCount <= 0))
            {
                sink.WriteMessageLine(string.Format("{0} is not a positive integer", cmdParts[1]), SeverityKind.Warning);
                return;
            }

            Common.Rules.ExecuterStatistics stats;
            if (!taskManager.TryGetStatistics(taskId, out stats))
            {
                sink.WriteMessageLine(string.Format("{0} is not a task id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (stats == null)
            {
                sink.WriteMessageLine(string.Format("task {0} is not recording execution statistics", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            sink.WriteMessageLine("** Execution statistics");
            sink.WriteMessageLine(string.Format("Number of rules       : {0}", stats.NRules));
            sink.WriteMessageLine(string.Format("Number of strata      : {0}", stats.NStrata));
            sink.WriteMessageLine(string.Format("Current stratum       : {0}", stats.CurrentStratum));
            sink.WriteMessageLine(string.Format("Current fixpoint size : {0}", stats.CurrentFixpointSize));
            var activations = stats.Activations;
            if (activations == null)
            {
                sink.WriteMessageLine("Activations currently unknown.");
                return;
            }

            ///// Sort activations
            int totalRules = 0;
            LinkedList<Common.Rules.ActivationStatistics> equalCounts;
            var sorted = new Map<System.Numerics.BigInteger, LinkedList<Common.Rules.ActivationStatistics>>((x, y) => x > y ? -1 : (x == y ? 0 : 1));
            foreach (var a in activations)
            {
                if (a.TotalActivations == 0)
                {
                    continue;
                }

                ++totalRules;
                if (!sorted.TryFindValue(a.TotalActivations, out equalCounts))
                {
                    equalCounts = new LinkedList<Common.Rules.ActivationStatistics>();
                    sorted.Add(a.TotalActivations, equalCounts);
                }

                equalCounts.AddLast(a);
            }

            var count = 0;
            foreach (var eqs in sorted.Values)
            {
                foreach (var a in eqs)
                {
                    a.PrintRule(null);
                    sink.WriteMessageLine(string.Format("Rule: {0}, Acts: {1}, TotalPend: {2}, TotalFail: {3}", a.RuleId, a.TotalActivations, a.TotalPends, a.TotalFailures));
                    ++count;
                    if (cmdParts.Length > 1 && count == maxCount)
                    {
                        break;
                    }
                }

                if (cmdParts.Length > 1 && count == maxCount)
                {
                    break;
                }
            }

            sink.WriteMessageLine(string.Format("Listed {0} of {1} activations", cmdParts.Length > 1 ? maxCount : totalRules, totalRules));
        }

        private void DoTruth(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length == 0)
            {
                sink.WriteMessageLine(TruthMsg, SeverityKind.Warning);
                return;
            }

            int queryId;
            if (!int.TryParse(cmdParts[0], out queryId))
            {
                sink.WriteMessageLine(string.Format("{0} is not a query/apply id", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            TaskKind kind;
            System.Threading.Tasks.Task task;
            if (!taskManager.TryGetTask(queryId, out task, out kind) || (kind != TaskKind.Query && kind != TaskKind.Apply))
            {
                sink.WriteMessageLine(string.Format("{0} is not a query/apply id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (!task.IsCompleted)
            {
                sink.WriteMessageLine(string.Format("Task {0} is still running.", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            if (kind == TaskKind.Query)
            {
                var result = ((System.Threading.Tasks.Task<QueryResult>)task).Result;
                List<Flag> flags;
                var goal = cmdParts.Length == 1 ? string.Format("{0}.requires", result.Source.Node.Name) : cmdParts[1];
                sink.WriteMessageLine("Listing all derived values...", SeverityKind.Info);
                foreach (var a in result.EnumerateDerivations(goal, out flags, true))
                {
                    sink.WriteMessage("   ");
                    a.Print(sink.Writer);
                    sink.WriteMessageLine(string.Empty);
                }

                sink.WriteMessageLine("List complete", SeverityKind.Info);
                WriteFlags(new ProgramName("CommandLine.4ml"), flags);
            }
            else if (kind == TaskKind.Apply)
            {
                var result = ((System.Threading.Tasks.Task<ApplyResult>)task).Result;
                List<Flag> flags;
                if (cmdParts.Length == 1)
                {
                    sink.WriteMessageLine(string.Format("You must supply a goal term"), SeverityKind.Warning);
                    return;
                }

                var goal = cmdParts[1].Trim();
                sink.WriteMessageLine("Listing all derived values...", SeverityKind.Info);
                foreach (var a in result.EnumerateDerivations(goal, out flags, true))
                {
                    sink.WriteMessage("   ");
                    a.Print(sink.Writer);
                    sink.WriteMessageLine(string.Empty);
                }

                sink.WriteMessageLine("List complete", SeverityKind.Info);
                WriteFlags(new ProgramName("CommandLine.4ml"), flags);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void DoExtract(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 4, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length < 2 || cmdParts.Length > 5)
            {
                sink.WriteMessageLine(ExtractMsg, SeverityKind.Warning);
                return;
            }

            int taskId;
            int solNum = 0;
            bool isAppExtract = cmdParts.Length % 2 == 0;
            var modName = isAppExtract ? cmdParts[1].Trim() : cmdParts[2].Trim();

            if (!int.TryParse(cmdParts[0], out taskId))
            {
                sink.WriteMessageLine(string.Format("{0} is not task id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (string.IsNullOrEmpty(modName))
            {
                sink.WriteMessageLine(ExtractMsg, SeverityKind.Warning);
                return;
            }
            else if (!isAppExtract && !int.TryParse(cmdParts[1], out solNum))
            {
                sink.WriteMessageLine(string.Format("{0} is not solution number", cmdParts[1]), SeverityKind.Warning);
                return;
            }

            TaskKind kind;
            System.Threading.Tasks.Task task;
            if (!taskManager.TryGetTask(taskId, out task, out kind))
            {
                sink.WriteMessageLine(string.Format("{0} is not an task id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (isAppExtract && kind != TaskKind.Apply)
            {
                sink.WriteMessageLine(string.Format("{0} is not an apply id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (!isAppExtract && kind != TaskKind.Solve)
            {
                sink.WriteMessageLine(string.Format("{0} is not an solve id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (!task.IsCompleted)
            {
                sink.WriteMessageLine(string.Format("Task {0} is still running.", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            System.Threading.Tasks.Task<AST<Program>> bldTask = null;
            if (isAppExtract)
            {
                var result = ((System.Threading.Tasks.Task<ApplyResult>)task).Result;
                bldTask = result.GetOutputModel(
                    modName,
                    new ProgramName(string.Format("{0}{1}.4ml", ProgramName.EnvironmentScheme, modName)),
                    modName);

                if (bldTask == null)
                {
                    sink.WriteMessageLine(string.Format("There is no output named {0}.", modName), SeverityKind.Warning);
                    return;
                }

                bldTask.Wait();
            }
            else
            {
                var result = ((System.Threading.Tasks.Task<SolveResult>)task).Result;
                WriteFlags(new ProgramName("program.4ml"), result.Flags);

                if ((solNum < result.NumSolutions) != true)
                {
                    sink.WriteMessageLine(string.Format("{0} is not a legal solution number.", solNum), SeverityKind.Warning);
                    return;
                }

                bldTask = result.GetOutputModel(
                    solNum,
                    modName,
                    new ProgramName(string.Format("{0}{1}.4ml", ProgramName.EnvironmentScheme, modName)),
                    modName);

                bldTask.Wait();
            }

            InstallResult insResult;
            AST<Program> installTarget = bldTask.Result;
            if (cmdParts.Length >= 4)
            {
                var cnfQuery = new Microsoft.Formula.API.ASTQueries.NodePred[]
                                {
                                    Microsoft.Formula.API.ASTQueries.NodePredFactory.Instance.Star,
                                    Microsoft.Formula.API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Model),
                                    Microsoft.Formula.API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Config)
                                };

                var fndResult = installTarget.FindAny(cnfQuery);
                if (fndResult != null)
                {
                    var config = (AST<Config>)fndResult;
                    config = Factory.Instance.AddSetting(
                        config,
                        Factory.Instance.MkId(string.Format("parsers.{0}", isAppExtract ? cmdParts[2] : cmdParts[3])),
                        Factory.Instance.MkCnst(string.Format(
                                "{0} at {1}",
                                isAppExtract ? cmdParts[2] : cmdParts[3], 
                                isAppExtract ? cmdParts[3] : cmdParts[4])));

                    config = Factory.Instance.AddSetting(
                        config,
                        Factory.Instance.MkId("parse_ActiveRenderer"),
                        Factory.Instance.MkCnst(cmdParts[2]));

                    installTarget = (AST<Program>)Factory.Instance.ToAST(config.Root);
                }
            }

            if (!env.Install(installTarget, out insResult))
            {
                sink.WriteMessageLine("Cannot extract model; environment is busy.", SeverityKind.Warning);
                return;
            }

            foreach (var kv in insResult.Touched)
            {
                sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
            }

            foreach (var f in insResult.Flags)
            {
                sink.WriteMessageLine(
                    string.Format("{0} ({1}, {2}): {3}",
                    f.Item1.Node.Name.ToString(env.Parameters),
                    f.Item2.Span.StartLine,
                    f.Item2.Span.StartCol,
                    f.Item2.Message), f.Item2.Severity);
            }
        }

        private void DoProof(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length == 0)
            {
                sink.WriteMessageLine(ProofMsg, SeverityKind.Warning);
                return;
            }

            int queryId;
            if (!int.TryParse(cmdParts[0], out queryId))
            {
                sink.WriteMessageLine(string.Format("{0} is not a query/apply id", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            TaskKind kind;
            System.Threading.Tasks.Task task;
            if (!taskManager.TryGetTask(queryId, out task, out kind) || (kind != TaskKind.Query && kind != TaskKind.Apply))
            {
                sink.WriteMessageLine(string.Format("{0} is not a query/apply id", cmdParts[0]), SeverityKind.Warning);
                return;
            }
            else if (!task.IsCompleted)
            {
                sink.WriteMessageLine(string.Format("Task {0} is still running.", cmdParts[0]), SeverityKind.Warning);
                return;
            }

            List<Flag> flags;
            IEnumerable<ProofTree> proofs;
            if (kind == TaskKind.Query)
            {
                var result = ((System.Threading.Tasks.Task<QueryResult>)task).Result;
                var goal = cmdParts.Length == 1 ? string.Format("{0}.requires", result.Source.Node.Name) : cmdParts[1];
                proofs = result.EnumerateProofs(goal, out flags);
            }
            else if (kind == TaskKind.Apply)
            {
                if (cmdParts.Length == 1)
                {
                    sink.WriteMessageLine(string.Format("You must supply a goal term"), SeverityKind.Warning);
                    return;
                }

                var result = ((System.Threading.Tasks.Task<ApplyResult>)task).Result;
                proofs = result.EnumerateProofs(cmdParts[1], out flags);
            }
            else
            {
                throw new NotImplementedException();
            }

            WriteFlags(new ProgramName("CommandLine.4ml"), flags);
            sink.WriteMessageLine("");

            bool forceStopped = false;
            DigitChoiceKind choice;
            foreach (var p in proofs)
            {
                p.Debug_PrintTree();
                /*
                foreach (var loc in p.ComputeLocators())
                {
                    loc.Debug_Print(3);
                }
                */

                sink.WriteMessageLine("Press 0 to stop, or 1 to continue", SeverityKind.Info);
                while (!chooser.GetChoice(out choice) || (int)choice > 1)
                {
                    sink.WriteMessageLine("Press 0 to stop, or 1 to continue", SeverityKind.Info);
                }

                if (choice == DigitChoiceKind.Zero)
                {
                    forceStopped = true;
                    break;
                }
            }

            if (!forceStopped)
            {
                sink.WriteMessageLine("No more proofs", SeverityKind.Info);
            }
        }

        private void DoApply(string s)
        {
            var cmdLineName = new ProgramName("CommandLine.4ml");
            var parse = Factory.Instance.ParseText(
                cmdLineName,
                string.Format("transform system Dummy () returns (dummy:: Dummy) {{\n{0}.\n}}", s));
            parse.Wait();

            WriteFlags(cmdLineName, parse.Result.Flags);
            if (!parse.Result.Succeeded)
            {
                sink.WriteMessageLine("Could not parse transformation step", SeverityKind.Warning);
                return;
            }

            var step = parse.Result.Program.FindAny(
                new API.ASTQueries.NodePred[] 
                { 
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Step)
                }) as AST<Step>;

            if (step == null)
            {
                sink.WriteMessageLine(ApplyMsg, SeverityKind.Warning);
                return;
            }

            AST<Node> stepModule;
            if (!TryResolveModuleByName(step.Node.Rhs.Module.Name, out stepModule, "step module"))
            {
                return;
            }

            AST<ModApply> rhs = null;
            if (stepModule.Node.NodeKind == NodeKind.Model)
            {
                var model = ((Model)stepModule.Node);
                if (step.Node.Rhs.Args.Count != 0)
                {
                    sink.WriteMessageLine(
                        string.Format("Model {0} does not take arguments", model.Name),
                        SeverityKind.Warning);
                    return;
                }

                rhs = Factory.Instance.MkModApply(ToModuleRef(stepModule, stepModule.Node.Span, step.Node.Rhs.Module.Rename), stepModule.Node.Span);
            }
            else if (stepModule.Node.NodeKind == NodeKind.Transform ||
                     stepModule.Node.NodeKind == NodeKind.TSystem)
            {
                string name;
                var inputs = stepModule.Node.NodeKind == NodeKind.Transform 
                                    ? ((Transform)stepModule.Node).Inputs 
                                    : ((TSystem)stepModule.Node).Inputs;

                if (step.Node.Rhs.Args.Count != inputs.Count)
                {
                    stepModule.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                    sink.WriteMessageLine(
                        string.Format("Transform {0} requires {1} arguments, but got {2}",
                            name,
                            inputs.Count,
                            step.Node.Rhs.Args.Count),
                        SeverityKind.Warning);
                    return;
                }

                int i = 1;
                AST<Node> argModule;
                rhs = Factory.Instance.MkModApply(ToModuleRef(stepModule, stepModule.Node.Span, step.Node.Rhs.Module.Rename), stepModule.Node.Span);
                using (var itArgs = step.Node.Rhs.Args.GetEnumerator())
                {
                    using (var itInputs = inputs.GetEnumerator())
                    {
                        while (itArgs.MoveNext() && itInputs.MoveNext())
                        {
                            if (itInputs.Current.IsValueParam)
                            {
                                rhs = Factory.Instance.AddArg(rhs, Factory.Instance.ToAST(itArgs.Current));
                            }
                            else if (itArgs.Current.NodeKind == NodeKind.Id || itArgs.Current.NodeKind == NodeKind.ModRef)
                            {
                                name = itArgs.Current.NodeKind == NodeKind.Id ? ((Id)itArgs.Current).Name : ((ModRef)itArgs.Current).Name;
                                string rename = itArgs.Current.NodeKind == NodeKind.Id ? null : ((ModRef)itArgs.Current).Rename;
                                if (!TryResolveModuleByName(name, out argModule, "input " + i.ToString()))
                                {
                                    return;
                                }
                                else if (argModule.Node.NodeKind != NodeKind.Model)
                                {
                                    argModule.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                                    sink.WriteMessageLine(string.Format("Module {0} is not valid for this operation", name), SeverityKind.Warning);
                                    return;
                                }

                                rhs = Factory.Instance.AddArg(rhs, ToModuleRef(argModule, stepModule.Node.Span, rename));
                            }
                            else
                            {
                                sink.WriteMessageLine(string.Format("Input {0} should be a model.", i), SeverityKind.Warning);
                                return;
                            }
                           
                            ++i;
                        }
                    }
                }
            }
            else
            {
                string name;
                stepModule.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                sink.WriteMessageLine(string.Format("Module {0} is not valid for this operation", name), SeverityKind.Warning);
                return;
            }

            var resolvedStep = Factory.Instance.MkStep(rhs, rhs.Node.Span);
            foreach (var id in step.Node.Lhs)
            {
                resolvedStep = Factory.Instance.AddLhs(resolvedStep, Factory.Instance.MkId(id.Name, id.Span));
            }
            
            List<Flag> flags;
            Common.Rules.ExecuterStatistics stats;
            System.Threading.Tasks.Task<ApplyResult> task;
            var applyCancel = new CancellationTokenSource();
            var result = env.Apply(
                resolvedStep, 
                true, 
                true, 
                out flags,
                out task,
                out stats,
                applyCancel.Token,
                FireAction);

            if (!result)
            {
                sink.WriteMessageLine("Could not start operation; environment is busy", SeverityKind.Warning);
                return;
            }

            WriteFlags(cmdLineName, flags);
            if (task != null)
            {
                var id = taskManager.StartTask(task, stats, applyCancel);
                sink.WriteMessageLine(string.Format("Started apply task with Id {0}.", id), SeverityKind.Info);
            }
            else
            {
                sink.WriteMessageLine("Failed to start apply task.", SeverityKind.Warning);
            }
        }

        private void DoQuery(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length != 2)
            {
                sink.WriteMessageLine(QueryMsg, SeverityKind.Warning);
                return;
            }

            var cmdLineName = new ProgramName("CommandLine.4ml");
            var parse = Factory.Instance.ParseText(
                cmdLineName,
                string.Format("domain Dummy {{q :-\n{0}\n.}}", cmdParts[1]));
            parse.Wait();

            WriteFlags(cmdLineName, parse.Result.Flags); 
            if (!parse.Result.Succeeded)
            {
                sink.WriteMessageLine("Could not parse goal", SeverityKind.Warning);
                return;
            }

            var rule = parse.Result.Program.FindAny(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Rule),
                });
            Contract.Assert(rule != null);
            var bodies = ((Rule)rule.Node).Bodies;

            AST<Node> module;
            if (!TryResolveModuleByName(cmdParts[0], out module))
            {
                return;
            }

            ProgramName progName = null;
            foreach (var p in module.Path)
            {
                if (p.Node.NodeKind == NodeKind.Program)
                {
                    progName = ((Program)p.Node).Name;
                    break;
                }
            }

            string name;
            module.Node.TryGetStringAttribute(AttributeKind.Name, out name);

            List<Flag> flags;
            System.Threading.Tasks.Task<QueryResult> task;
            Common.Rules.ExecuterStatistics stats;
            var queryCancel = new CancellationTokenSource();
            var goals = new AST<Body>[bodies.Count];
            int i = 0;
            foreach (var b in bodies)
            {
                goals[i++] = (AST<Body>)Factory.Instance.ToAST(b);
            }

            var result = env.Query(
                progName,
                name,
                goals,
                true,
                true,
                out flags,
                out task,
                out stats,
                queryCancel.Token,
                FireAction);

            if (!result)
            {
                sink.WriteMessageLine("Could not start operation; environment is busy", SeverityKind.Warning);
                return;
            }

            WriteFlags(cmdLineName, flags);
            if (task != null)
            {
                var id = taskManager.StartTask(task, stats, queryCancel);
                sink.WriteMessageLine(string.Format("Started query task with Id {0}.", id), SeverityKind.Info);
            }
            else
            {
                sink.WriteMessageLine("Failed to start query task.", SeverityKind.Warning);
            }
        }

        private void DoSolve(string s)
        {
            var cmdParts = s.Split(cmdSplitChars, 3, StringSplitOptions.RemoveEmptyEntries);
            if (cmdParts.Length != 3)
            {
                sink.WriteMessageLine(SolveMsg, SeverityKind.Warning);
                return;
            }

            int maxSols;
            if (!int.TryParse(cmdParts[1], out maxSols) || maxSols <= 0)
            {
                sink.WriteMessageLine("Expected a positive number of solutions", SeverityKind.Warning);
                return;
            }

            var cmdLineName = new ProgramName("CommandLine.4ml");
            var parse = Factory.Instance.ParseText(
                cmdLineName,
                string.Format("domain Dummy {{q :-\n{0}\n.}}", cmdParts[2]));
            parse.Wait();

            WriteFlags(cmdLineName, parse.Result.Flags);
            if (!parse.Result.Succeeded)
            {
                sink.WriteMessageLine("Could not parse goal", SeverityKind.Warning);
                return;
            }

            var rule = parse.Result.Program.FindAny(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Rule),
                });
            Contract.Assert(rule != null);
            var bodies = ((Rule)rule.Node).Bodies;

            AST<Node> module;
            if (!TryResolveModuleByName(cmdParts[0], out module))
            {
                return;
            }

            ProgramName progName = null;
            foreach (var p in module.Path)
            {
                if (p.Node.NodeKind == NodeKind.Program)
                {
                    progName = ((Program)p.Node).Name;
                    break;
                }
            }

            string name;
            module.Node.TryGetStringAttribute(AttributeKind.Name, out name);

            List<Flag> flags;
            System.Threading.Tasks.Task<SolveResult> task;
            var solveCancel = new CancellationTokenSource();
            var goals = new AST<Body>[bodies.Count];
            int i = 0;
            foreach (var b in bodies)
            {
                goals[i++] = (AST<Body>)Factory.Instance.ToAST(b);
            }

            var result = env.Solve(
                progName,
                name,
                goals,
                maxSols,
                out flags,
                out task,
                solveCancel.Token);

            if (!result)
            {
                sink.WriteMessageLine("Could not start operation; environment is busy", SeverityKind.Warning);
                return;
            }

            WriteFlags(cmdLineName, flags);
            if (task != null)
            {
                var id = taskManager.StartTask(task, new Common.Rules.ExecuterStatistics(null), solveCancel);
                sink.WriteMessageLine(string.Format("Started solve task with Id {0}.", id), SeverityKind.Info);
            }
            else
            {
                sink.WriteMessageLine("Failed to start solved task.", SeverityKind.Warning);
            }
        }

        private void DoDetails(string s)
        {
            AST<Node> module;
            if (!TryResolveModuleByName(s, out module))
            {
                return;
            }

            AST<Node> redmodule;
            if (!Compiler.Compiler.TryGetReducedForm(module, out redmodule))
            {
                sink.WriteMessageLine("Could not find a reduced module", SeverityKind.Warning);
                return;
            }

            sink.WriteMessageLine("Reduced form", SeverityKind.Info);
            redmodule.Print(sink.Writer, canceler.Token, env.Parameters);

            Common.Terms.SymbolTable symbolTable;
            if (!Compiler.Compiler.TryGetSymbolTable(module, out symbolTable))
            {
                sink.WriteMessageLine("Could not find symbol table", SeverityKind.Warning);
                return;
            }

            sink.WriteMessageLine("Symbol table", SeverityKind.Info);
            List<string[]> rows = null;
            int[] colWidths = null;
            CollectSymbols(symbolTable.Root, ref rows, ref colWidths);
            WriteTable(rows, colWidths);

            sink.WriteMessageLine("");
            sink.WriteMessage("Type constants: ", SeverityKind.Info);
            PrintTypeConstants(symbolTable.Root);

            sink.WriteMessageLine("");
            sink.WriteMessage("Symbolic constants: ", SeverityKind.Info);
            PrintSymbolicConstants(symbolTable.Root);

            sink.WriteMessageLine("");
            sink.WriteMessage("Rationals: ", SeverityKind.Info);
            foreach (var r in symbolTable.RationalCnsts)
            {
                sink.WriteMessage(r.Raw.ToString() + " ");
            }

            sink.WriteMessageLine("");
            sink.WriteMessage("Strings: ", SeverityKind.Info);
            foreach (var str in symbolTable.StringCnsts)
            {
                sink.WriteMessage(Formula.API.ASTQueries.ASTSchema.Instance.Encode((string)str.Raw) + " ");
            }

            sink.WriteMessageLine("");
            sink.WriteMessage("Variables: ", SeverityKind.Info);
            foreach (var symb in symbolTable.Root.Symbols)
            {
                if (symb.Kind != Common.Terms.SymbolKind.UserCnstSymb ||
                    symb.IsMangled ||
                    ((Common.Terms.UserCnstSymb)symb).UserCnstKind != Common.Terms.UserCnstSymbKind.Variable)
                {
                    continue;
                }

                sink.WriteMessage(symb.Name + " ");
            }

            sink.WriteMessageLine("");
        }

        private void DoGenerate(string s)
        {
            AST<Node> module;
            if (!TryResolveModuleByName(s, out module))
            {
                return;
            }

            ProgramName progName = null;
            foreach (var p in module.Path)
            {
                if (p.Node.NodeKind == NodeKind.Program)
                {
                    progName = ((Program)p.Node).Name;
                    break;
                }
            }

            string name;
            module.Node.TryGetStringAttribute(AttributeKind.Name, out name);
            System.Threading.Tasks.Task<GenerateResult> task;
            var options = new API.Generators.GeneratorOptions(
                API.Generators.GeneratorOptions.Language.CSharp,
                true,
                false,
                name,
                "DataModels");

            if (!env.Generate(progName, name, sink.Writer, options, out task))
            {
                sink.WriteMessageLine("Could not start operation; environment is busy", SeverityKind.Warning);
                return;
            }

            task.Wait();
            WriteFlags(progName, task.Result.Flags);
            if (!task.Result.Succeeded)
            {
                sink.WriteMessageLine("Generate operation failed", SeverityKind.Error);
            }
        }

        private void DoTypes(string s)
        {
            AST<Node> module;
            if (!TryResolveModuleByName(s, out module))
            {
                return;
            }

            AST<Node> redmodule;
            if (!Compiler.Compiler.TryGetReducedForm(module, out redmodule))
            {
                sink.WriteMessageLine("Could not find a reduced module", SeverityKind.Warning);
                return;
            }

            //// TODO: Will need to be generalized to print other type environments
            var envs = new API.ASTQueries.NodePred[]
            {
                API.ASTQueries.NodePredFactory.Instance.Star,
                API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Rule)
            };

            TypeEnvironment env;
            redmodule.FindAll(
                envs,
                (ch, n) =>
                {
                    if (!Compiler.Compiler.TryGetTypeEnvironment(n, out env))
                    {
                        return;
                    }

                    WriteEnvironment(env, 0);
                },
                canceler.Token);
        }

        private void WriteEnvironment(TypeEnvironment env, int indent)
        {
            sink.WriteMessageLine(string.Format(
                "{0}+ Type environment at ({1}, {2})",
                indent == 0 ? string.Empty : new string(' ', 3 * indent),
                env.Node.Span.StartLine,
                env.Node.Span.StartCol));

            Term type;
            var indentStr = new string(' ', 3 * indent + 2);
            foreach (var t in env.Terms)
            {
                if (!t.Symbol.IsVariable ||
                    !env.TryGetType(t, out type))
                {
                    continue;
                }

                sink.WriteMessage(string.Format("{0}{1}: ", indentStr, t.Symbol.PrintableName));
                type.PrintTypeTerm(sink.Writer);
                sink.WriteMessageLine("");
            }

            foreach (var crc in env.Coercions)
            {
                sink.WriteMessageLine(string.Format(
                    "{0}Coerced arg {1} of ({2}, {3}): {4} --> {5}",
                    indentStr,
                    crc.Item2 + 1,
                    crc.Item1.Span.StartLine,
                    crc.Item1.Span.StartCol,
                    crc.Item3,
                    crc.Item4));
            }

            foreach (var c in env.Children)
            {
                WriteEnvironment(c, indent + 1);
            }
        }

        private void WriteTable(List<string[]> rows, int[] colWidths)
        {
            int pre, post;
            bool printedHeader = false;
            foreach (var r in rows)
            {
                for (int i = 0; i < colWidths.Length; ++i)
                {
                    pre = (int)Math.Floor(((double)(colWidths[i] - r[i].Length)) / 2D);
                    post = (int)Math.Ceiling(((double)(colWidths[i] - r[i].Length)) / 2D);
                    sink.WriteMessage(new string(' ', pre + 1));
                    sink.WriteMessage(r[i]);
                    sink.WriteMessage(new string(' ', post + 1));
                    if (i < colWidths.Length - 1)
                    {
                        sink.WriteMessage("|");
                    }
                }

                sink.WriteMessageLine("");
                if (!printedHeader)
                {
                    printedHeader = true;
                    for (int i = 0; i < colWidths.Length; ++i)
                    {
                        sink.WriteMessage(new string('-', colWidths[i] + 2));
                        if (i < colWidths.Length - 1)
                        {
                            sink.WriteMessage("|");
                        }
                    }

                    sink.WriteMessageLine("");
                }
            }
        }

        private void PrintTypeConstants(Common.Terms.Namespace nspace)
        {
            UserCnstSymb cnst;
            foreach (var s in nspace.Symbols)
            {
                cnst = s as UserCnstSymb;
                if (cnst == null || !cnst.IsTypeConstant || cnst.IsMangled)
                {
                    continue;
                }

                sink.WriteMessage(" " + cnst.FullName);
            }

            foreach (var n in nspace.Children)
            {
                PrintTypeConstants(n);
            }
        }

        private void PrintSymbolicConstants(Common.Terms.Namespace nspace)
        {
            UserCnstSymb cnst;
            foreach (var s in nspace.Symbols)
            {
                cnst = s as UserCnstSymb;
                if (cnst == null || !cnst.IsSymbolicConstant || cnst.IsMangled)
                {
                    continue;
                }

                sink.WriteMessage(" " + cnst.FullName);
            }

            foreach (var n in nspace.Children)
            {
                PrintSymbolicConstants(n);
            }
        }

        private void CollectSymbols(Common.Terms.Namespace nspace, 
                                    ref List<string[]> rows,
                                    ref int[] colWidths)
        {
            if (rows == null)
            {
                var header = new string[] { "Space", "Name", "Arity", "Kind" };
                rows = new List<string[]>();
                rows.Add(header);
                colWidths = new int[header.Length];
                for (int i = 0; i < header.Length; ++i)
                {
                    colWidths[i] = header[i].Length;
                }
            }

            UserCnstSymb uc;
            foreach (var s in nspace.Symbols)
            {
                if (s.Kind == Common.Terms.SymbolKind.UserCnstSymb)
                {
                    uc = (UserCnstSymb)s;
                    if (uc.UserCnstKind == Common.Terms.UserCnstSymbKind.Variable ||
                        uc.IsTypeConstant ||
                        uc.IsSymbolicConstant)
                    {
                        continue;
                    }
                }
                else if (s.IsMangled)
                {
                    continue;
                }

                var column = new string[] 
                {
                    nspace.FullName,
                    s.Name,
                    s.Arity.ToString(),
                    GetSymbolDescription(s) 
                };

                rows.Add(column);

                for (int i = 0; i < column.Length; ++i)
                {
                    colWidths[i] = Math.Max(colWidths[i], column[i].Length);
                }
            }

            foreach (var n in nspace.Children)
            {
                CollectSymbols(n, ref rows, ref colWidths);
            }
        }

        private static string GetSymbolDescription(Common.Terms.Symbol symb)
        {
            var kind = symb.Kind;
            switch (kind)
            {
                case Common.Terms.SymbolKind.BaseCnstSymb:
                    return "bcnst";
                case Common.Terms.SymbolKind.BaseOpSymb:
                    return "bop";
                case Common.Terms.SymbolKind.ConSymb:
                    return "con";
                case Common.Terms.SymbolKind.MapSymb:
                    return "map";
                case Common.Terms.SymbolKind.BaseSortSymb:
                    return "sort";
                case Common.Terms.SymbolKind.UnnSymb:
                    return "unn";
                case Common.Terms.SymbolKind.UserCnstSymb:
                    var uckind = ((Common.Terms.UserCnstSymb)symb).UserCnstKind;
                    switch (uckind)
                    {
                        case Common.Terms.UserCnstSymbKind.Derived:
                            return "dcnst";
                        case Common.Terms.UserCnstSymbKind.New:
                            return "ncnst";
                        case Common.Terms.UserCnstSymbKind.Variable:
                            return "var";
                        default:
                            throw new NotImplementedException();
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        private void DoRender(string s)
        {
            AST<Node> module;
            if (!TryResolveModuleByName(s, out module))
            {
                return;
            }

            var progName = ((Program)module.GetPathParent()).Name;
            string modName;
            module.Node.TryGetStringAttribute(AttributeKind.Name, out modName);
            System.Threading.Tasks.Task<RenderResult> renderTask;
            if (!env.Render(progName, modName, out renderTask))
            {
                sink.WriteMessageLine(BusyMsg, SeverityKind.Warning);
                return;
            }

            renderTask.Wait();           
            if (renderTask.Result.Succeeded)
            {
                renderTask.Result.Module.Print(sink.Writer, CancellationToken.None, env.Parameters);
            }

            WriteFlags(progName, renderTask.Result.Flags);
        }

        private void DoSave(string s)
        {
            AST<Node> module;
            var cmd = s.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmd.Length != 2)
            {
                sink.WriteMessageLine(SaveMsg, SeverityKind.Warning);
                return;
            }

            if (!TryResolveModuleByName(cmd[0].Trim(), out module))
            {
                return;
            }

            try
            {
                //// Print just this module.
                module = Factory.Instance.ToAST(module.Node);
                module.SaveAs(
                    cmd[1],
                    default(CancellationToken), 
                    EnvParams.SetParameter(env.Parameters, EnvParamKind.Printer_ReferencePrintKind, ReferencePrintKind.Absolute));

                sink.WriteMessageLine(string.Format("Wrote to {0}", cmd[1]), SeverityKind.Info);
            }
            catch (Exception e)
            {
                sink.WriteMessageLine(e.Message, SeverityKind.Warning);
            }
        }

        private void DoLoad(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                sink.WriteMessageLine(LoadMsg, SeverityKind.Info);
                return;
            }

            try
            {
                var progName = new ProgramName(s);
                bool isNewFile = true;
                foreach (var file in loadOrder)
                {
                    if (file.Equals(progName))
                    {
                        isNewFile = false;
                        break;
                    }
                }

                if (isNewFile)
                {
                    loadOrder.AddLast(progName);
                }
            }
            catch
            {
            }

            InstallResult result;
            env.Install(s, out result);
            foreach (var kv in result.Touched)
            {
                sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
                //// kv.Program.Print(Console.Out, canceler.Token);
            }

            foreach (var f in result.Flags)
            {
                sink.WriteMessageLine(
                    string.Format("{0} ({1}, {2}): {3}",
                    f.Item1.Node.Name.ToString(env.Parameters),
                    f.Item2.Span.StartLine,
                    f.Item2.Span.StartCol,
                    f.Item2.Message), f.Item2.Severity);
            }
        }

        private void DoHelp(string s)
        {
            int max = 0;
            foreach (var cmd in cmdMap.Values)
            {
                max = Math.Max(max, cmd.Name.Length + cmd.ShortName.Length + 3);
            }

            max += 3;
            foreach (var kv in cmdMap)
            {
                if (kv.Key == kv.Value.ShortName)
                {
                    continue;
                }

                sink.WriteMessageLine(string.Format(
                    "{0} ({1}){2}- {3}", 
                    kv.Value.Name, 
                    kv.Value.ShortName, 
                    new string(' ', max - kv.Key.Length - kv.Value.ShortName.Length), 
                    kv.Value.Description));
            }
        }

        private void DoDel(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                sink.WriteMessageLine(DelInfoMsg, SeverityKind.Info);
                return;
            }

            AST<Node> crntVal;
            if (cmdVars.TryGetValue(s, out crntVal))
            {
                cmdVars.Remove(s);
                sink.WriteMessageLine(string.Format(DelVarMsg, s), SeverityKind.Info);
            }
            else
            {
                sink.WriteMessageLine(string.Format(NotDefVarMsg, s), SeverityKind.Warning);
            }
        }

        private void DoPrint(string s)
        {
            AST<API.Nodes.Program> program;
            if (TryResolveProgramByName(s, out program))
            {
                program.Print(sink.Writer, canceler.Token, env.Parameters);
            }
        }

        private void DoReload(string s)
        {
            AST<API.Nodes.Program> program;
            if (s != null && s.Trim() == "*")
            {
                InstallResult result;
                if (!env.Reinstall(loadOrder.ToArray(), out result))
                {
                    sink.WriteMessageLine("Cannot perform operation; environment is busy", SeverityKind.Warning);
                    return;
                }

                foreach (var kv in result.Touched)
                {
                    sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
                }

                foreach (var f in result.Flags)
                {
                    sink.WriteMessageLine(
                        string.Format("{0} ({1}, {2}): {3}",
                        f.Item1.Node.Name.ToString(env.Parameters),
                        f.Item2.Span.StartLine,
                        f.Item2.Span.StartCol,
                        f.Item2.Message), f.Item2.Severity);
                }
            }
            else if (TryResolveProgramByName(s, out program))
            {
                InstallResult result;
                if (!env.Reinstall(new ProgramName[] { program.Node.Name }, out result))
                {
                    sink.WriteMessageLine("Cannot perform operation; environment is busy", SeverityKind.Warning);
                    return;
                }

                foreach (var kv in result.Touched)
                {
                    sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
                }

                foreach (var f in result.Flags)
                {
                    sink.WriteMessageLine(
                        string.Format("{0} ({1}, {2}): {3}",
                        f.Item1.Node.Name.ToString(env.Parameters),
                        f.Item2.Span.StartLine,
                        f.Item2.Span.StartCol,
                        f.Item2.Message), f.Item2.Severity);
                }
            }
        }

        private void DoUnload(string s)
        {
            AST<API.Nodes.Program> program;
            if (s != null && s.Trim() == "*")
            {
                InstallResult result;
                if (!env.Uninstall(loadOrder.ToArray(), out result))
                {
                    sink.WriteMessageLine("Cannot perform operation; environment is busy", SeverityKind.Warning);
                    return;
                }

                loadOrder.Clear();
                foreach (var kv in result.Touched)
                {
                    sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
                    //// kv.Program.Print(Console.Out, canceler.Token);
                }

                foreach (var f in result.Flags)
                {
                    sink.WriteMessageLine(
                        string.Format("{0} ({1}, {2}): {3}",
                        f.Item1.Node.Name.ToString(env.Parameters),
                        f.Item2.Span.StartLine,
                        f.Item2.Span.StartCol,
                        f.Item2.Message), f.Item2.Severity);
                }
            }
            else if (TryResolveProgramByName(s, out program))
            {
                InstallResult result;
                var n = loadOrder.First;
                while (n != null)
                {
                    if (n.Value.Equals(program.Node.Name))
                    {
                        loadOrder.Remove(n);
                        break;
                    }
                    else
                    {
                        n = n.Next;
                    }
                }

                if (!env.Uninstall(new ProgramName[] { program.Node.Name }, out result))
                {
                    sink.WriteMessageLine("Cannot perform operation; environment is busy", SeverityKind.Warning);
                    return;
                }

                foreach (var kv in result.Touched)
                {
                    sink.WriteMessageLine(string.Format("({0}) {1}", kv.Status, kv.Program.Node.Name.ToString(env.Parameters)));
                    //// kv.Program.Print(Console.Out, canceler.Token);
                }

                foreach (var f in result.Flags)
                {
                    sink.WriteMessageLine(
                        string.Format("{0} ({1}, {2}): {3}",
                        f.Item1.Node.Name.ToString(env.Parameters),
                        f.Item2.Span.StartLine,
                        f.Item2.Span.StartCol,
                        f.Item2.Message), f.Item2.Severity);
                }
            }
        }

        private void DoConfigHelp(string s)
        {
            sink.WriteMessageLine("Use collections to bind plugins to names.");
            foreach (var descr in Compiler.Configuration.CollectionDescriptions)
            {
                sink.WriteMessageLine(string.Format("   {0} (members implement {1})", descr.Item1, descr.Item2.Name));
                sink.WriteMessageLine(string.Format("      {0}", descr.Item3), SeverityKind.Info);
                sink.WriteMessageLine(string.Empty);
            }

            sink.WriteMessageLine(string.Empty);
            sink.WriteMessageLine("Use settings to control the behavior of various commands.");
            foreach (var descr in Compiler.Configuration.SettingsDescriptions)
            {
                sink.WriteMessageLine(string.Format("   {0} ({1})", descr.Item1, descr.Item2.ToString().ToLowerInvariant()));
                sink.WriteMessageLine(string.Format("      {0}", descr.Item3), SeverityKind.Info);
                sink.WriteMessageLine(string.Empty);
            }

            try
            {
                foreach (var assmb in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in assmb.GetExportedTypes())
                    {
                        if (t.IsAbstract || !t.IsClass)
                        {
                            continue;
                        }
                        
                        foreach (var col in Compiler.Configuration.CollectionDescriptions)
                        {
                            if (col.Item1 != Compiler.Configuration.ModulesCollectionName && col.Item2.IsAssignableFrom(t))
                            {
                                sink.WriteMessageLine(string.Empty);
                                sink.WriteMessageLine(string.Format("{0} interface {1} ({2})", col.Item1, t.FullName, assmb.Location));
                                var con = t.GetConstructor(System.Type.EmptyTypes);
                                if (con == null)
                                {
                                    continue;
                                }

                                var inst = con.Invoke(null);
                                var descrProp = t.GetProperty("Description");
                                if (descrProp == null || !(typeof(string).IsAssignableFrom(descrProp.PropertyType)))
                                {
                                    continue;
                                }

                                sink.WriteMessageLine(string.Format("   {0}", descrProp.GetGetMethod().Invoke(inst, null)), SeverityKind.Info);
                                sink.WriteMessageLine(string.Empty);

                                var settingsProp = t.GetProperty("SuggestedSettings");
                                if (settingsProp == null || !(typeof(IEnumerable<Tuple<string, CnstKind, string>>).IsAssignableFrom(settingsProp.PropertyType)))
                                {
                                    continue;
                                }

                                var settings = (IEnumerable<Tuple<string, CnstKind, string>>)settingsProp.GetGetMethod().Invoke(inst, null);
                                foreach (var sdescr in settings)
                                {
                                    sink.WriteMessageLine(string.Format("   {0} ({1})", sdescr.Item1, sdescr.Item2.ToString().ToLowerInvariant()));
                                    sink.WriteMessageLine(string.Format("      {0}", sdescr.Item3), SeverityKind.Info);
                                    sink.WriteMessageLine(string.Empty);
                                }

                                sink.WriteMessageLine(string.Empty);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                sink.WriteMessageLine(string.Format("Could not examine plugins - {0}", e.Message), SeverityKind.Warning);
            }
        }

        private void DoSet(string s)
        {
            var cmdSplit = s.Trim().Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
            if (cmdSplit.Length <= 1)
            {
                if (cmdSplit.Length == 0 || string.IsNullOrWhiteSpace(cmdSplit[0]))
                {
                    sink.WriteMessageLine(SetInfoMsg, SeverityKind.Info);
                    return;
                }

                AST<Node> crntVal;
                if (cmdVars.TryGetValue(cmdSplit[0], out crntVal))
                {
                    using (var stringWriter = new System.IO.StringWriter())
                    {
                        crntVal.Print(stringWriter, CancellationToken.None, env.Parameters);
                        sink.WriteMessageLine(string.Format("{0} = {1}", cmdSplit[0], stringWriter), SeverityKind.Info);
                    }
                }
                else
                {
                    sink.WriteMessageLine(string.Format(NotDefVarMsg, cmdSplit[0]), SeverityKind.Warning);
                }

                return;
            }

            ImmutableCollection<Flag> flags;
            var val = Factory.Instance.ParseDataTerm(cmdSplit[1], out flags);
            if (val == null)
            {
                sink.WriteMessageLine(ParseErrMsg, SeverityKind.Warning);
                foreach (var f in flags)
                {
                    sink.WriteMessageLine(f.Message, SeverityKind.Warning);
                }
            }
            else
            {
                val = val.Substitute(
                            API.ASTQueries.NodePredFactory.Instance.MkPredicate(IsCmdVar) &
                            API.ASTQueries.NodePredFactory.Instance.MkPredicate(ChildContextKind.Args),
                            (x) => cmdVars[((Id)x.Last<ChildInfo>().Node).Name].Node.NodeKind,
                            (x) => cmdVars[((Id)x.Last<ChildInfo>().Node).Name].Node);

                cmdVars[cmdSplit[0]] = val;
                using (var stringWriter = new System.IO.StringWriter())
                {
                    val.Print(stringWriter, CancellationToken.None, env.Parameters);
                    sink.WriteMessageLine(string.Format("{0} = {1}", cmdSplit[0], stringWriter), SeverityKind.Info);
                }
            }
        }

        private void DoLS(string s)
        {
            int max, id;
            if (string.IsNullOrWhiteSpace(s) || s == "vars")
            {
                sink.WriteMessageLine("");
                sink.WriteMessageLine("Environment variables");
                max = 0;
                foreach (var v in cmdVars.Keys)
                {
                    max = Math.Max(max, v.Length);
                }

                max += 3;
                id = 1;
                foreach (var kv in cmdVars)
                {
                    sink.WriteMessage(string.Format(" {0} {1}{2}= ", id++, kv.Key, new string(' ', max - kv.Key.Length)));
                    kv.Value.Print(sink.Writer, CancellationToken.None, env.Parameters);
                    sink.WriteMessageLine("");
                }
            }

            if (string.IsNullOrWhiteSpace(s) || s == "progs")
            {
                sink.WriteMessageLine("");
                sink.WriteMessageLine("Programs in file root", SeverityKind.Info);

                var path = new Stack<string>();
                env.FileRoot.Compute<bool>(
                    (x) =>
                    {
                        return PrintPrograms(x, path, true);
                    },
                    (x, y) => 
                    {
                        if (x.NodeKind == NodeKind.Folder)
                        {
                            path.Pop();
                        }

                        return true; 
                    },
                    canceler.Token);

                sink.WriteMessageLine("");
                sink.WriteMessageLine("Programs in env root", SeverityKind.Info);
                env.EnvRoot.Compute<bool>(
                    (x) =>
                    {
                        return PrintPrograms(x, path, false);
                    },
                    (x, y) =>
                    {
                        if (x.NodeKind == NodeKind.Folder)
                        {
                            path.Pop();
                        }

                        return true;
                    },
                    canceler.Token);
            }

            if (string.IsNullOrWhiteSpace(s) || s == "tasks")
            {
                sink.WriteMessageLine("");
                sink.WriteMessageLine("All tasks", SeverityKind.Info);

                List<string[]> rows;
                int[] colWidths;
                taskManager.MkTaskTable(out rows, out colWidths);
                WriteTable(rows, colWidths);
            }
        }

        private IEnumerable<Node> PrintPrograms(Node n, Stack<string> path, bool isFilePath)
        {
            if (n.NodeKind == NodeKind.Program)
            {
                var pathName = path.Peek();
                var progName = ((Microsoft.Formula.API.Nodes.Program)n).Name.Uri.AbsoluteUri;
                Contract.Assert(progName.StartsWith(pathName));
                sink.WriteMessageLine(
                    string.Format("{0}| {1}",
                    new string(' ', path.Count),
                    progName.Substring(pathName.Length)));
            }
            else if (n.NodeKind == NodeKind.Folder)
            {
                var f = (Folder)n;
                if (path.Count == 0)
                {
                        path.Push(
                            isFilePath 
                              ? ProgramName.FileScheme.AbsoluteUri
                              : ProgramName.EnvironmentScheme.AbsoluteUri);
                }
                else
                {
                    path.Push(path.Peek() + f.Name + "/");
                }

                sink.WriteMessageLine(
                    string.Format("{0}+-- {1}{2}",
                    new string(' ', path.Count),
                    f.Name,
                    f.Programs.Count == 0 ?
                         string.Empty :
                         string.Format(" [{0} file(s)]", f.Programs.Count)));

                foreach (var m in f.Programs)
                {
                    yield return m;
                }

                foreach (var m in f.SubFolders)
                {
                    yield return m;
                }
            }
        }

        private bool TryResolveModuleByName(string partialModAndProgName, out AST<Node> module, string prompt = null)
        {
            bool result;
            if (string.IsNullOrWhiteSpace(partialModAndProgName))
            {
                result = TryResolveModuleByName(null, null, out module, prompt);
            }
            else
            {
                var parts = partialModAndProgName.Split(cmdSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    result = TryResolveModuleByName(parts[0], null, out module, prompt);
                }
                else
                {
                    result = TryResolveModuleByName(parts[0], parts[1], out module, prompt);
                }
            }

            return result;
        }

        private bool TryResolveModuleByName(string partialModName, string partialProgName, out AST<Node> module, string prompt)
        {
            var candidates = GetModulesByName(partialModName, partialProgName);
            if (candidates.Length == 0)
            {
                sink.WriteMessageLine("No module with that name", SeverityKind.Warning);
                module = null;
                return false;
            }
            else if (candidates.Length == 1 || exeOptions)
            {
                module = candidates[0];
                return true;
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                sink.WriteMessageLine("Choose:");
            }
            else
            {
                sink.WriteMessageLine(string.Format("Choose {0}:", prompt));
            }

            var max = Math.Min((int)DigitChoiceKind.Nine, candidates.Length - 1);
            string canProgName, canModName;
            for (var i = 0; i <= max; ++i)
            {
                canProgName = ((Program)candidates[i].GetPathParent()).Name.ToString();
                candidates[i].Node.TryGetStringAttribute(AttributeKind.Name, out canModName);                
                sink.WriteMessageLine(string.Format("  {0}. {1} at \"{2}\"", i, canModName, canProgName));
            }

            if (max < candidates.Length - 1)
            {
                sink.WriteMessageLine(
                    string.Format("{0} choice(s) not shown. Provide a more specific name.",
                        candidates.Length - 1 - max),
                    SeverityKind.Warning);
            }

            DigitChoiceKind choice;
            if (!chooser.GetChoice(out choice) ||
                ((int)choice) < 0 ||
                ((int)choice) > max)
            {
                sink.WriteMessageLine("Bad choice", SeverityKind.Warning);
                module = null;
                return false;
            }

            module = candidates[(int)choice];
            return true;
        }
        
        private bool TryResolveProgramByName(string partialName, out AST<API.Nodes.Program> program)
        {
            var candidates = GetProgramsByName(partialName);
            if (candidates.Length == 0)
            {
                sink.WriteMessageLine("No file with that name", SeverityKind.Warning);
                program = null;
                return false;
            }
            else if (candidates.Length == 1 || exeOptions)
            {
                program = candidates[0];
                return true;
            }

            sink.WriteMessageLine("Choose:");
            var max = Math.Min((int)DigitChoiceKind.Nine, candidates.Length - 1);
            for (var i = 0; i <= max; ++i)
            {
                sink.WriteMessageLine(string.Format("  {0}. {1}", i, candidates[i].Node.Name));
            }

            if (max < candidates.Length - 1)
            {
                sink.WriteMessageLine(
                    string.Format("{0} choice(s) not shown. Provide a more specific name.",
                        candidates.Length - 1 - max), 
                    SeverityKind.Warning);
            }

            DigitChoiceKind choice;
            if (!chooser.GetChoice(out choice) ||
                ((int)choice) < 0 ||
                ((int)choice) > max)
            {
                sink.WriteMessageLine("Bad choice", SeverityKind.Warning);
                program = null;
                return false;
            }

            program = candidates[(int)choice];
            return true;
        }

        private AST<Node>[] GetModulesByName(string partialModuleName, string partialProgramName)
        {
            partialProgramName = partialProgramName == null ? string.Empty : partialProgramName.ToLowerInvariant();
            partialModuleName = partialModuleName == null ? string.Empty : partialModuleName;
            var sorted = new Set<AST<Node>>((x, y) => CompareModules(x, y));
            var root = env.FileRoot;

            root.FindAll(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.Module,
                },
                (path, node) =>
                {
                    var progName = ((Program)((LinkedList<ChildInfo>)path).Last.Previous.Value.Node).Name;
                    string modName;
                    node.TryGetStringAttribute(AttributeKind.Name, out modName);

                    if (!string.IsNullOrEmpty(partialProgramName) &&
                        !progName.ToString().Contains(partialProgramName))
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(partialModuleName) &&
                        !modName.Contains(partialModuleName))
                    {
                        return;
                    }

                    sorted.Add(Factory.Instance.FromAbsPositions(root.Node, path));
                },
                canceler.Token);

            root = env.EnvRoot;
            root.FindAll(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.Module
                },
                (path, node) =>
                {
                    var progName = ((Program)((LinkedList<ChildInfo>)path).Last.Previous.Value.Node).Name;
                    string modName;
                    node.TryGetStringAttribute(AttributeKind.Name, out modName);

                    if (!string.IsNullOrEmpty(partialProgramName) &&
                        !progName.ToString().Contains(partialProgramName))
                    {
                        return;
                    }

                    if (!string.IsNullOrEmpty(partialModuleName) &&
                        !modName.Contains(partialModuleName))
                    {
                        return;
                    }

                    sorted.Add(Factory.Instance.FromAbsPositions(root.Node, path));
                },
                canceler.Token);

            return sorted.ToArray();
        }

        private AST<API.Nodes.Program>[] GetProgramsByName(string partialName)
        {            
            partialName = partialName == null ? string.Empty : partialName.ToLowerInvariant();
            var sorted = new Set<AST<API.Nodes.Program>>((x, y) => ProgramName.Compare(x.Node.Name, y.Node.Name));
            env.FileRoot.FindAll(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Program),
                },
                (path, node) =>
                {
                    if (string.IsNullOrEmpty(partialName) ||
                        ((API.Nodes.Program)node).Name.ToString().Contains(partialName))
                    {
                        sorted.Add((AST<API.Nodes.Program>)Factory.Instance.ToAST(node));
                    }
                },
                canceler.Token);

            env.EnvRoot.FindAll(
                new API.ASTQueries.NodePred[]
                {
                    API.ASTQueries.NodePredFactory.Instance.Star,
                    API.ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Program),
                },
                (path, node) =>
                {
                    if (string.IsNullOrEmpty(partialName) ||
                        ((API.Nodes.Program)node).Name.ToString().Contains(partialName))
                    {
                        sorted.Add((AST<API.Nodes.Program>)Factory.Instance.ToAST(node));
                    }
                },
                canceler.Token);

            return sorted.ToArray();
        }

        private bool IsCmdVar(AttributeKind kind, object o)
        {
            if (kind != AttributeKind.Name)
            {
                return false;
            }

            return cmdVars.ContainsKey((string)o);
        }

        private void PrintPrompt()
        {
            var foc = Focus;
            sink.WriteMessageLine("");
            sink.WriteMessage(string.Format("[{0}]> ", foc == null ? "" : foc.Name.ToString()));
        }

        private void WriteFlags(ProgramName name, IEnumerable<Flag> flags)
        {
            if (flags == null)
            {
                return;
            }

            foreach (var f in flags)
            {
                sink.WriteMessageLine(
                    string.Format("{0} ({1}, {2}): {3}",
                    name.ToString(env.Parameters),
                    f.Span.StartLine,
                    f.Span.StartCol,
                    f.Message), f.Severity);
            }
        }

        private void ReleaseCommandLock()
        {
            bool gotLock = false;
            try
            {
                cmdLock.Enter(ref gotLock);
                isCmdLocked = false;
            }
            finally
            {
                if (gotLock)
                {
                    cmdLock.Exit();
                }
            }
        }

        private void FireAction(Term t, ProgramName p, Node n, CancellationToken c)
        {
            var wl = WatchLevel;
            if (wl == WatchLevelKind.Off)
            {
                return;
            }

            if (wl == WatchLevelKind.Prompt)
            {
                promptStepEvent.WaitOne();
            }

            t.PrintTerm(sink.Writer);
            sink.WriteMessageLine(string.Empty);
            sink.WriteMessageLine(string.Format(
                "   :- {0} ({1}, {2})",
                p == null ? "?" : p.ToString(),
                n == null ? "?" : n.Span.StartLine.ToString(),
                n == null ? "?" : n.Span.StartCol.ToString()));
        }

        private bool GetCommandLock()
        {
            bool gotLock = false;
            try
            {
                cmdLock.Enter(ref gotLock);
                if (!isCmdLocked)
                {
                    isCmdLocked = true;
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
                    cmdLock.Exit();
                }
            }
        }

        private IEnumerable<Term> ExpandTypeTerm(Term t, Stack<string> terminators)
        {
            if (t.Symbol.IsDataConstructor)
            {
                for (int i = 0; i < t.Args.Length; ++i)
                {
                    terminators.Push(i < t.Args.Length - 1 ? ", " : ")");
                    yield return t.Args[i];
                }

                yield break;
            }
            else if (!t.Symbol.IsTypeUnn)
            {
                yield break;
            }

            using (var it = t.Enumerate(x => x.Symbol.IsTypeUnn ? x.Args : null).GetEnumerator())
            {
                Term crnt = null;
                while (it.MoveNext())
                {
                    if (!it.Current.Symbol.IsTypeUnn)
                    {
                        crnt = it.Current;
                        break;
                    }
                }

                while (it.MoveNext())
                {
                    if (!it.Current.Symbol.IsTypeUnn)
                    {
                        terminators.Push(" + ");
                        yield return crnt;
                        crnt = it.Current;
                    }
                }

                terminators.Push(string.Empty);
                yield return crnt;
            }                       
        }

        static System.Reflection.Assembly LoadFromSameFolder(object sender, ResolveEventArgs args)
        {
            try
            {
                var folderPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var assemblyPath = System.IO.Path.Combine(folderPath, new System.Reflection.AssemblyName(args.Name).Name + ".dll");

                if (System.IO.File.Exists(assemblyPath))
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
                    return assembly;
                }

                assemblyPath = System.IO.Path.Combine(System.IO.Path.Combine(folderPath, "Formula1.4"), new System.Reflection.AssemblyName(args.Name).Name + ".dll");
                if (System.IO.File.Exists(assemblyPath))
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
                    return assembly;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static AST<ModRef> ToModuleRef(AST<Node> module, Span span, string rename = null)
        {
            Contract.Requires(module != null && module.Node.IsModule);
            foreach (var n in module.Path.Reverse<ChildInfo>())
            {
                if (n.Node.NodeKind == NodeKind.Program)
                {
                    string name;
                    module.Node.TryGetStringAttribute(AttributeKind.Name, out name);
                    return Factory.Instance.MkModRef(name, rename, ((Program)n.Node).Name.ToString(), span);
                }
            }

            //// Module should have a path to its program.
            throw new InvalidOperationException();
        }

        private static int CompareModules(AST<Node> mod1, AST<Node> mod2)
        {
            var prog1 = (Program)mod1.GetPathParent();
            var prog2 = (Program)mod2.GetPathParent();
            var cmp = ProgramName.Compare(prog1.Name, prog2.Name);
            if (cmp != 0)
            {
                return cmp;
            }

            string name1, name2;
            mod1.Node.TryGetStringAttribute(AttributeKind.Name, out name1);
            mod2.Node.TryGetStringAttribute(AttributeKind.Name, out name2);
            return string.CompareOrdinal(name1, name2);
        }

        private class Command
        {
            public Action<string> Action
            {
                get;
                private set;
            }

            public string Name
            {
                get;
                private set;
            }

            public string ShortName
            {
                get;
                private set;
            }

            public string Description
            {
                get;
                private set;
            }

            public Command(string name, string shortName, Action<string> action, string description)
            {
                Action = action;
                Name = name;
                ShortName = shortName;
                Description = description;
            }
        }
    }
}
