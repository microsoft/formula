namespace Microsoft.Formula.Common.Composites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Compiler;
    using Extras;
    using Terms;

    internal class CoreTSystem
    {
        private Map<Location, IndexData> indices =
            new Map<Location, IndexData>(Location.Compare);

        private Map<string, Tuple<Node, Location>> modelVars = 
            new Map<string, Tuple<Node, Location>>(string.Compare);

        private CoreStep[] executionOrder = null;

        internal ModuleData ModuleData
        {
            get;
            private set;
        }

        /// <summary>
        /// Maps a model variable to the node that introduced it,
        /// and the domain over which the model is defined.
        /// </summary>
        public IEnumerable<KeyValuePair<string, Tuple<Node, Location>>> ModelVariables
        {
            get { return modelVars; }
        }

        /// <summary>
        /// A term index created over the signature of the system
        /// </summary>
        public TermIndex SignatureIndex
        {
            get;
            private set;
        }

        internal CoreTSystem(ModuleData data)
        {
            Contract.Requires(data != null && data.Reduced != null && data.SymbolTable != null);
            Contract.Requires(data.Reduced.Node.NodeKind == NodeKind.TSystem);
            ModuleData = data;
            SignatureIndex = new TermIndex(data.SymbolTable);

            bool wasAdded;
            UserSymbol pSymbol, pTypeSymbol;
            foreach (var p in ((TSystem)data.Reduced.Node).Inputs)
            {
                if (!p.IsValueParam)
                {
                    continue;
                }

                var result = data.SymbolTable.ModuleSpace.TryGetSymbol("%" + p.Name, out pSymbol);
                Contract.Assert(result);

                result = data.SymbolTable.ModuleSpace.TryGetSymbol("%" + p.Name + "~Type", out pTypeSymbol);
                Contract.Assert(result);

                SignatureIndex.RegisterSymbCnstType(
                    SignatureIndex.MkApply(pSymbol, TermIndex.EmptyArgs, out wasAdded),
                    SignatureIndex.GetCanonicalTerm(pTypeSymbol, 0));
            }
        }

        internal Task<StepResult> Execute(
                      Map<string, FactSet> modelParams,
                      Map<string, Term> valueParams,
                      CancellationToken cancel)
        {
            Task<StepResult> depTask;
            var results = new StepResultMap(this);
            foreach (var kv in modelParams)
            {
                results.SetResult(kv.Key, kv.Value);
            }

            var varToTask = new Map<string, Task<StepResult>>(string.Compare);
            var tasks = new Task<StepResult>[executionOrder.Length];
            for (int i = 0; i < executionOrder.Length; ++i)
            {
                var cstep = executionOrder[i];
                var depSet = new HashSet<Task<StepResult>>();
                foreach (var arg in cstep.AppArgs)
                {
                    if (arg is string && varToTask.TryFindValue((string)arg, out depTask))
                    {
                        depSet.Add(depTask);
                    }
                }

                if (depSet.Count == 0)
                {
                    tasks[i] = Task.Factory.StartNew<StepResult>(() =>
                        {
                            var sr = new StepResult(cstep.Step, this, valueParams, results, cancel);
                            sr.Start();
                            return sr;
                        });
                }
                else
                {
                    tasks[i] = Task.Factory.ContinueWhenAll<StepResult>(
                                    depSet.ToArray(),
                                    (ts) =>
                                    {
                                        var sr = new StepResult(cstep.Step, this, valueParams, results, cancel);
                                        sr.Start();
                                        return sr;
                                    });
                }

                foreach (var lhs in cstep.Step.Lhs)
                {
                    varToTask.Add(lhs.Name, tasks[i]);
                }
            }

            return Task.Factory.ContinueWhenAll<StepResult>(tasks, (ts) => { return new StepResult(results); });
        }

        internal bool Compile(List<Flag> flags, CancellationToken cancel = default(CancellationToken))
        {
            ModRef mr;
            var tsystem = (TSystem)ModuleData.Reduced.Node;
            var succeeded = true;
            //// First, register the input parameters.
            foreach (var i in tsystem.Inputs)
            {
                if (i.IsValueParam)
                {
                    continue;
                }

                mr = (ModRef)i.Type;
                Contract.Assert(mr.CompilerData is Location);
                modelVars.Add(mr.Rename, new Tuple<Node, Location>(mr, (Location)mr.CompilerData));
            }

            //// Second, check that model variables are well-defined.
            CoreStep cstep;
            var stepId = 0;
            foreach (var step in tsystem.Steps)
            {
                Contract.Assert(step.CompilerData == null);
                cstep = new CoreStep(stepId++, this, step);
                step.CompilerData = cstep;
                succeeded = cstep.AddLHSDefinitions(flags, cancel) && succeeded;
            }

            if (!succeeded)
            {
                return false;
            }

            //// Third, check that transform applications are correct.
            foreach (var step in tsystem.Steps)
            {
                succeeded = ((CoreStep)step.CompilerData).CreateAppArgs(flags, cancel) && succeeded;
            }

            if (!succeeded)
            {
                return false;
            }

            //// Fourth, check that all outputs are determined.
            Tuple<Node, Location> def;
            foreach (var o in tsystem.Outputs)
            {
                mr = (ModRef)o.Type;
                if (!modelVars.TryFindValue(mr.Rename, out def))
                {
                    succeeded = false;
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        o,
                        Constants.TransUnorientedError.ToString(mr.Rename),
                        Constants.TransUnorientedError.Code));
                }
                else if (Location.Compare((Location)mr.CompilerData, def.Item2) != 0)
                {
                    succeeded = false;
                    var expectedLoc = (Location)mr.CompilerData;
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        def.Item1,
                        Constants.BadTransOutputType.ToString(
                            mr.Rename,
                            string.Format("{0} at {1}", ((Domain)def.Item2.AST.Node).Name, def.Item2.GetCodeLocationString(ModuleData.Env.Parameters)),
                            string.Format("{0} at {1}", ((Domain)expectedLoc.AST.Node).Name, expectedLoc.GetCodeLocationString(ModuleData.Env.Parameters))),
                        Constants.BadTransOutputType.Code));
                }
            }

            if (!succeeded)
            {
                return false;
            }

            //// Fifth, compute step dependency graph.
            //// Collect the rules defining each variable.
            var stepDefs = new Map<string, CoreStep>(string.Compare);
            var stepDependencies = new DependencyCollection<CoreStep, string>((x, y) => x.Id - y.Id, string.Compare);
            foreach (var step in tsystem.Steps)
            {
                cstep = (CoreStep)step.CompilerData;
                stepDependencies.Add(cstep);
                foreach (var a in step.Lhs)
                {
                    stepDefs.Add(a.Name, cstep);
                }
            }

            CoreStep definer;
            foreach (var step in tsystem.Steps)
            {
                cstep = (CoreStep)step.CompilerData;
                foreach (var a in cstep.AppArgs)
                {
                    if (a is string && stepDefs.TryFindValue((string)a, out definer))
                    {
                        stepDependencies.Add(definer, cstep, (string)a);
                    }
                }
            }

            int n;
            var sccs = stepDependencies.GetSCCs(out n);
            n = 0;
            foreach (var scc in sccs)
            {
                if (scc.Kind == DependencyNodeKind.Normal)
                {
                    continue;
                }

                foreach (var node in scc.InternalNodes)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        node.Resource.Step,
                        Constants.BadDepCycle.ToString("transform", n),
                        Constants.BadDepCycle.Code));
                    succeeded = false;
                }

                ++n;
            }

            if (!succeeded)
            {
                return false;
            }

            var topo = stepDependencies.GetTopologicalSort(out n);
            executionOrder = new CoreStep[n];
            n = 0;
            foreach (var node in topo)
            {
                executionOrder[n++] = node.Resource;
            }

            return succeeded;
        }

        /// <summary>
        /// Constructs the value parameters for s under appIndex given system params
        /// </summary>
        internal Map<string, FactSet> InstantiateModelParams(Step step, StepResultMap results)
        {
            Contract.Requires(step != null && results != null);
            var cstep = (CoreStep)step.CompilerData;
            Contract.Assert(cstep.Owner == this);
            return cstep.InstantiateModelParams(results);
        }

        /// <summary>
        /// Constructs the value parameters for s under appIndex given system params
        /// </summary>
        internal Map<string, Term> InstantiateValueParams(Step step, TermIndex appIndex, Map<string, Term> systemParams)
        {
            Contract.Requires(step != null && appIndex != null && systemParams != null);
            var cstep = (CoreStep)step.CompilerData;
            Contract.Assert(cstep.Owner == this);
            return cstep.InstantiateValueParams(appIndex, systemParams);
        }

        private IndexData GetIndexData(Location loc)
        {            
            IndexData index;
            if (!indices.TryFindValue(loc, out index))
            {
                if (((ModuleData)loc.AST.Node.CompilerData).SymbolTable == null)
                {
                    return null;
                }

                index = new IndexData(ModuleData.Env, loc, ModuleData.Source);
            }

            return index;
        }

        private void Print(Namespace n)
        {
            foreach (var s in n.Symbols)
            {
                Console.WriteLine(s.FullName);
            }

            foreach (var m in n.Children)
            {
                Print(m);
            }
        }

        private class CoreStep
        {
            private CoreTSystem owner;
            private object[] appArgs = null;

            public CoreTSystem Owner
            {
                get { return owner; }
            }

            public Step Step
            {
                get;
                private set;
            }

            public int Id
            {
                get;
                private set;
            }

            /// <summary>
            /// If the arg is a model variable, then holds a string of the name.
            /// If the arg is a value (with paramters), then holds a ValueArg object.
            /// </summary>
            public object[] AppArgs
            {
                get { return appArgs; }
            }

            public CoreStep(int id, CoreTSystem owner, Step step)
            {
                Step = step;
                this.owner = owner;
                Id = id;
            }

            internal Map<string, FactSet> InstantiateModelParams(StepResultMap results)
            {
                var mod = Step.Rhs.Module;
                var loc = (Location)mod.CompilerData;
                if (loc.AST.Node.NodeKind == NodeKind.Model)
                {
                    return new Map<string, FactSet>(string.Compare);
                }

                IEnumerable<Param> inputs;
                if (loc.AST.Node.NodeKind == NodeKind.Transform)
                {
                    inputs = ((Transform)loc.AST.Node).Inputs;
                }
                else if (loc.AST.Node.NodeKind == NodeKind.TSystem)
                {
                    inputs = ((TSystem)loc.AST.Node).Inputs;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var i = 0;
                var modelParams = new Map<string, FactSet>(string.Compare);
                foreach (var p in inputs)
                {
                    if (p.IsValueParam)
                    {
                        ++i;
                        continue;
                    }

                    modelParams.Add(((ModRef)p.Type).Rename, results[(string)appArgs[i]]);
                    ++i;
                }

                return modelParams;
            }

            internal Map<string, Term> InstantiateValueParams(TermIndex appIndex, Map<string, Term> systemParams)
            {
                var mod = Step.Rhs.Module;
                var loc = (Location)mod.CompilerData;
                if (loc.AST.Node.NodeKind == NodeKind.Model)
                {
                    return new Map<string, Term>(string.Compare);
                }

                IEnumerable<Param> inputs;
                if (loc.AST.Node.NodeKind == NodeKind.Transform)
                {
                    inputs = ((Transform)loc.AST.Node).Inputs;
                }
                else if (loc.AST.Node.NodeKind == NodeKind.TSystem)
                {
                    inputs = ((TSystem)loc.AST.Node).Inputs;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var i = 0;
                var instParams = new Map<string, Term>(string.Compare);
                foreach (var p in inputs)
                {
                    if (!p.IsValueParam)
                    {
                        ++i;
                        continue;
                    }

                    instParams.Add(p.Name, ((ValueArg)appArgs[i]).Instantiate(owner.ModuleData.SymbolTable, appIndex, systemParams));
                    ++i;
                }

                return instParams;
            }

            public bool AddLHSDefinitions(List<Flag> flags, CancellationToken cancel)
            {
                var mod = Step.Rhs.Module;
                var loc = (Location)mod.CompilerData;
                var succeeded = true;
                if (loc.AST.Node.NodeKind == NodeKind.Model)
                {
                    var model = (Model)loc.AST.Node;
                    if (Step.Rhs.Args.Count != 0)
                    {
                        succeeded = false;
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            mod,
                            Constants.BadSyntax.ToString("Model identity function does not take arguments"),
                            Constants.BadSyntax.Code));
                    }

                    if (model.IsPartial)
                    {
                        succeeded = false;
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            mod,
                            Constants.BadComposition.ToString(mod.Name, "model cannot be partial"),
                            Constants.BadSyntax.Code));
                    }

                    if (Step.Lhs.Count != 1)
                    {
                        succeeded = false;
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            Step,
                            Constants.BadSyntax.ToString(string.Format("LHS assigns {0} model variables, but expects {1}", Step.Lhs.Count, 1)),
                            Constants.BadSyntax.Code));
                    }
                    else 
                    {
                        var defNode = Step.Lhs.First<Id>();
                        succeeded = TryDefineLHS(defNode.Name, (Location)model.Domain.CompilerData, defNode, flags);
                    }

                    return succeeded;
                }

                ImmutableCollection<Param> outputs = null;
                if (loc.AST.Node.NodeKind == NodeKind.Transform)
                {
                    var trans = (Transform)loc.AST.Node;                        
                    if (Step.Rhs.Args.Count != trans.Inputs.Count)
                    {
                        succeeded = false;
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            mod,
                            Constants.BadSyntax.ToString(string.Format("transform got {0} arguments, but expects {1}", Step.Rhs.Args.Count, trans.Inputs.Count)),
                            Constants.BadSyntax.Code));
                    }

                    outputs = trans.Outputs;
                }
                else if (loc.AST.Node.NodeKind == NodeKind.TSystem)
                {
                    var trans = (TSystem)loc.AST.Node;
                    if (Step.Rhs.Args.Count != trans.Inputs.Count)
                    {
                        succeeded = false;
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            mod,
                            Constants.BadSyntax.ToString(string.Format("transform got {0} arguments, but expects {1}", Step.Rhs.Args.Count, trans.Inputs.Count)),
                            Constants.BadSyntax.Code));
                    
                    }
                    outputs = trans.Outputs;
                }
                else
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        mod,
                        Constants.BadComposition.ToString(mod.Name, "module must be a model, transform, or transform system"),
                        Constants.BadSyntax.Code));
                    return false;
                }

                if (Step.Lhs.Count != outputs.Count)
                {
                    succeeded = false;
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        Step,
                        Constants.BadSyntax.ToString(string.Format("LHS assigns {0} model variables, but expects {1}", Step.Lhs.Count, outputs.Count)),
                        Constants.BadSyntax.Code));
                }
                else
                {
                    ModRef mr;
                    using (var lhsIt = Step.Lhs.GetEnumerator())
                    {
                        using (var outIt = outputs.GetEnumerator())
                        {
                            while (lhsIt.MoveNext() && outIt.MoveNext())
                            {
                                mr = (ModRef)outIt.Current.Type;
                                succeeded = TryDefineLHS(lhsIt.Current.Name, (Location)mr.CompilerData, lhsIt.Current, flags) && succeeded;
                            }
                        }
                    }
                }

                return succeeded;
            }

            public bool CreateAppArgs(List<Flag> flags, CancellationToken cancel)
            {
                Contract.Assert(appArgs == null);

                var mod = Step.Rhs.Module;
                var loc = (Location)mod.CompilerData;
                if (loc.AST.Node.NodeKind == NodeKind.Model)                
                {
                    appArgs = new object[0];
                    return true;
                }

                appArgs = new object[Step.Rhs.Args.Count];
                ImmutableCollection<Param> inputs = null;
                if (loc.AST.Node.NodeKind == NodeKind.Transform)
                {
                    inputs = ((Transform)loc.AST.Node).Inputs;
                }
                else if (loc.AST.Node.NodeKind == NodeKind.TSystem)
                {
                    inputs = ((TSystem)loc.AST.Node).Inputs;
                }
                else
                {
                    throw new NotImplementedException();
                }

                var indData = owner.GetIndexData(loc);
                if (indData == null)
                {
                    return false;
                }

                var i = 0;
                var succeeded = true;
                using (var paramIt = inputs.GetEnumerator())
                {
                    using (var argIt = Step.Rhs.Args.GetEnumerator())
                    {
                        while (paramIt.MoveNext() && argIt.MoveNext())
                        {
                            if (paramIt.Current.IsValueParam)
                            {
                                succeeded = SetValueArg(i, argIt.Current, paramIt.Current, indData, flags) && succeeded;
                            }
                            else
                            {
                                succeeded = SetModelArg(i, argIt.Current, paramIt.Current, flags) && succeeded;
                            }

                            ++i;
                        }
                    }
                }

                return succeeded;
            }

            private bool SetValueArg(int index, Node arg, Param expectedType, IndexData indData, List<Flag> flags)
            {
                UserSymbol paramTypeSymb;
                var result = indData.Index.SymbolTable.ModuleSpace.TryGetSymbol(string.Format("%{0}~Type", expectedType.Name), out paramTypeSymb);
                Contract.Assert(result);

                var valueArg = new ValueArg();
                if (!CreateValueArg(indData, arg, flags, valueArg))
                {
                    return false;
                }

                if (valueArg.ValueTerm.Symbol.IsVariable)
                {
                    string fromSpace, toSpace;
                    if (indData.Coerce(
                            paramTypeSymb,
                            0,
                            valueArg.GetParam(valueArg.ValueTerm),
                            indData.Index.SymbolTable.ModuleSpace.FullName,
                            index,
                            arg,
                            flags,
                            out fromSpace,
                            out toSpace))
                    {
                        valueArg.SetRelabeling(valueArg.ValueTerm, fromSpace, toSpace);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (valueArg.ValueTerm.Symbol.IsNonVarConstant)
                {
                    if (valueArg.ValueTerm.Symbol.Kind == SymbolKind.UserCnstSymb && 
                        ((UserCnstSymb)valueArg.ValueTerm.Symbol).IsSymbolicConstant)
                    {
                        throw new Impossible();
                    }
                    else
                    {
                        if (!paramTypeSymb.CanonicalForm[0].AcceptsConstant(valueArg.ValueTerm.Symbol))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                arg,
                                Constants.BadTransValueArgType.ToString(index + 1, Step.Rhs.Module.Name),
                                Constants.BadTransValueArgType.Code));
                            return false;
                        }
                    }
                }
                else if (valueArg.ValueTerm.Symbol.IsDataConstructor)
                {
                    var usrSort = valueArg.ValueTerm.Symbol.Kind == SymbolKind.ConSymb
                                    ? ((ConSymb)valueArg.ValueTerm.Symbol).SortSymbol
                                    : ((MapSymb)valueArg.ValueTerm.Symbol).SortSymbol;
                    if (!paramTypeSymb.CanonicalForm[0].Contains(usrSort))
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            arg,
                            Constants.BadTransValueArgType.ToString(index + 1, Step.Rhs.Module.Name),
                            Constants.BadTransValueArgType.Code));
                        return false;
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }

                appArgs[index] = valueArg;
                return true;
            }

            private bool SetModelArg(int index, Node arg, Param expectedType, List<Flag> flags)
            {
                if (arg.NodeKind != NodeKind.Id)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        arg,
                        Constants.BadSyntax.ToString("Expected an indentifier"),
                        Constants.BadSyntax.Code));
                    return false;
                }

                var id = (Id)arg;
                Tuple<Node, Location> def;
                if (!owner.modelVars.TryFindValue(id.Name, out def))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        arg,
                        Constants.UndefinedSymbol.ToString("model variable", id.Name),
                        Constants.UndefinedSymbol.Code));
                    return false;
                }

                var expectedLoc = (Location)expectedType.Type.CompilerData;
                if (Location.Compare(expectedLoc, def.Item2) != 0)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        arg,
                        Constants.BadTransModelArgType.ToString(
                            index + 1,
                            Step.Rhs.Module.Name,
                            string.Format("{0} at {1}", ((Domain)def.Item2.AST.Node).Name, def.Item2.GetCodeLocationString(owner.ModuleData.Env.Parameters)),
                            string.Format("{0} at {1}", ((Domain)expectedLoc.AST.Node).Name, expectedLoc.GetCodeLocationString(owner.ModuleData.Env.Parameters))),
                        Constants.BadTransModelArgType.Code));
                    return false;
                }

                appArgs[index] = id.Name;
                return true;
            }

            private bool TryDefineLHS(string name, Location loc, Node defNode, List<Flag> flags)
            {
                Tuple<Node, Location> def;

                var succeeded = true;
                if (owner.modelVars.TryFindValue(name, out def))
                {
                    succeeded = false;
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        defNode,
                        Constants.DuplicateDefs.ToString(
                            "model variable " + name,
                            def.Item1.GetCodeLocationString(owner.ModuleData.Env.Parameters),
                            defNode.GetCodeLocationString(owner.ModuleData.Env.Parameters)),
                        Constants.DuplicateDefs.Code));
                }

                if (!ASTSchema.Instance.IsId(name, false, false, false, false))
                {
                    succeeded = false;
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        defNode,
                        Constants.BadId.ToString(name, "model variable"),
                        Constants.BadId.Code));
                }

                if (!succeeded)
                {
                    return false;
                }

                owner.modelVars.Add(name, new Tuple<Node, Location>(defNode, loc));
                return true;
            }

            private bool CreateValueArg(IndexData indData, Node n, List<Flag> flags, ValueArg arg)
            {
                var stack = new Stack<Tuple<Namespace, Symbol>>();
                stack.Push(new Tuple<Namespace, Symbol>(null, null));
                var success = new SuccessToken();

                var t = Factory.Instance.ToAST(n).Compute<Term>(
                    (x) => CreateValueArg_Unfold(x, arg, indData, stack, success, flags),
                    (x, ch) => CreateValueArg_Fold(x, arg, ch, indData, stack, success, flags));

                if (t != null)
                {
                    arg.SetValueTerm(t);
                }

                return success.Result;
            }

            private IEnumerable<Node> CreateValueArg_Unfold(
                                                        Node n,
                                                        ValueArg valArg,
                                                        IndexData indData,
                                                        Stack<Tuple<Namespace, Symbol>> symbStack,
                                                        SuccessToken success,
                                                        List<Flag> flags)
            {
                bool wasAdded;
                var space = symbStack.Peek().Item1;
                switch (n.NodeKind)
                {
                    case NodeKind.Cnst:
                        {
                            var cnst = (Cnst)n;
                            BaseCnstSymb symb;
                            switch (cnst.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    symb = (BaseCnstSymb)indData.Index.MkCnst((Rational)cnst.Raw, out wasAdded).Symbol;
                                    break;
                                case CnstKind.String:
                                    symb = (BaseCnstSymb)indData.Index.MkCnst((string)cnst.Raw, out wasAdded).Symbol;
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            symbStack.Push(new Tuple<Namespace, Symbol>(space, symb));
                            return null;
                        }
                    case NodeKind.Id:
                        {
                            var id = (Id)n;
                            UserSymbol symb;
                            if (id.Name.Contains('%'))
                            {
                                if (Resolve(id.Name, "constant", id, null, owner.ModuleData.SymbolTable, x => x.IsNonVarConstant, out symb, flags))
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(space, valArg.MkParamUse(indData.Index, (UserCnstSymb)symb).Symbol));
                                }
                                else
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                    success.Failed();
                                }

                                return null;
                            }
                            else
                            {
                                if (Resolve(id.Name, "constant", id, space, indData.Index.SymbolTable, x => x.IsNonVarConstant, out symb, flags))
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                                }
                                else
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                    success.Failed();
                                }

                                return null;
                            }
                        }
                    case NodeKind.FuncTerm:
                        {
                            var ft = (FuncTerm)n;
                            if (ft.Function is Id)
                            {
                                UserSymbol symb;
                                if (ValidateUse_UserFunc(ft, space, indData.Index.SymbolTable, out symb, flags))
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                                    return ft.Args;
                                }
                                else
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                    success.Failed();
                                    return null;
                                }
                            }
                            else
                            {
                                var flag = new Flag(
                                    SeverityKind.Error,
                                    ft,
                                    Constants.BadSyntax.ToString("Only data constructors can appear here."),
                                    Constants.BadSyntax.Code);
                                flags.Add(flag);
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }
                    default:
                        throw new NotImplementedException();
                }
            }

            private Term CreateValueArg_Fold(
                       Node n,
                       ValueArg valArg,
                       IEnumerable<Term> args,
                       IndexData indData,
                       Stack<Tuple<Namespace, Symbol>> symbStack,
                       SuccessToken success,
                       List<Flag> flags)
            {
                bool wasAdded;
                string fromSpace, toSpace;
                var space = symbStack.Peek().Item1;
                var symb = symbStack.Pop().Item2;
                if (symb == null)
                {
                    return null;
                }

                if (symb.IsVariable)
                {
                    return indData.Index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                }
                else if (symb.IsNonVarConstant)
                {
                    if (symb.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symb).IsSymbolicConstant)
                    {
                        throw new Impossible();
                    }
                    else
                    {
                        return indData.Index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                    }
                }
                else if (symb.IsDataConstructor)
                {
                    var con = (UserSymbol)symb;
                    var targs = new Term[con.Arity];
                    var i = 0;
                    var typed = true;
                    foreach (var a in args)
                    {
                        if (a == null)
                        {
                            //// If an arg is null, then it already has errors, 
                            //// so skip it an check the rest.
                            typed = false;
                            continue;
                        }

                        targs[i] = a;
                        if (a.Symbol.IsVariable)
                        {
                            if (indData.Coerce(con, i, valArg.GetParam(a), con.FullName, i, n, flags, out fromSpace, out toSpace))
                            {
                                valArg.SetRelabeling(a, fromSpace, toSpace);
                            }
                            else
                            {
                                typed = false;
                            }
                        }
                        else if (a.Symbol.IsNonVarConstant)
                        {
                            if (a.Symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)a.Symbol).IsSymbolicConstant)
                            {
                                throw new Impossible();
                            }
                            else
                            {
                                if (!con.CanonicalForm[i].AcceptsConstant(a.Symbol))
                                {
                                    flags.Add(MkBadArgType(n, symb, i));
                                    typed = false;
                                }
                            }
                        }
                        else if (a.Symbol.IsDataConstructor)
                        {
                            var usrSort = a.Symbol.Kind == SymbolKind.ConSymb
                                            ? ((ConSymb)a.Symbol).SortSymbol
                                            : ((MapSymb)a.Symbol).SortSymbol;
                            if (!con.CanonicalForm[i].Contains(usrSort))
                            {
                                flags.Add(MkBadArgType(n, symb, i));
                                typed = false;
                            }
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        ++i;
                    }

                    if (!typed)
                    {
                        success.Failed();
                        return null;
                    }

                    return indData.Index.MkApply(con, targs, out wasAdded);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
 
            private static Flag MkBadArgType(Node n, Symbol symb, int index)
            {
                return new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.BadArgType.ToString(index + 1, symb.PrintableName),
                    Constants.BadArgType.Code);
            }

            private bool ValidateUse_UserFunc(FuncTerm ft, Namespace space, SymbolTable table, out UserSymbol symbol, List<Flag> flags)
            {
                Contract.Assert(ft.Function is Id);
                var result = true;
                var id = (Id)ft.Function;

                if (!Resolve(id.Name, "constructor", id, space, table, x => x.IsDataConstructor, out symbol, flags))
                {
                    return false;
                }
                else if (symbol.Arity != ft.Args.Count)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        ft,
                        Constants.BadSyntax.ToString(string.Format("{0} got {1} arguments but needs {2}", symbol.FullName, ft.Args.Count, symbol.Arity)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    result = false;
                }

                var i = 0;
                foreach (var a in ft.Args)
                {
                    ++i;
                    if (a.NodeKind != NodeKind.Compr)
                    {
                        continue;
                    }

                    var flag = new Flag(
                        SeverityKind.Error,
                        ft,
                        Constants.BadSyntax.ToString(string.Format("comprehension not allowed in argument {1} of {0}", symbol == null ? id.Name : symbol.FullName, i)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    result = false;
                }

                return result;
            }

            private bool Resolve(
                            string id,
                            string kind,
                            Node n,
                            Namespace space,
                            SymbolTable table,
                            Predicate<UserSymbol> validator,
                            out UserSymbol symbol,
                            List<Flag> flags)
            {
                UserSymbol other = null;
                symbol = table.Resolve(id, out other, space);
                if (symbol == null || !validator(symbol))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.UndefinedSymbol.ToString(kind, id),
                        Constants.UndefinedSymbol.Code);
                    flags.Add(flag);
                    return false;
                }
                else if (other != null)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.AmbiguousSymbol.ToString(
                            "identifier",
                            id,
                            string.Format("({0}, {1}): {2}",
                                    symbol.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                    symbol.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                    symbol.FullName),
                            string.Format("({0}, {1}): {2}",
                                    other.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                    other.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                    other.FullName)),
                        Constants.AmbiguousSymbol.Code);
                    flags.Add(flag);
                    return false;
                }

                return true;
            }
        }

        private class IndexData
        {
            private Location module;
            private Location transModule;
            private Env env;
            private SymbolTable composite = null;
            private string[] compNamespaces = null;

            public TermIndex Index
            {
                get;
                private set;
            }

            public IndexData(Env env, Location module, Location transModule)
            {
                Contract.Requires(env != null);
                this.env = env;
                this.module = module;
                this.transModule = transModule;
                Index = new TermIndex(((ModuleData)module.AST.Node.CompilerData).SymbolTable);
            }

            public bool Coerce(UserSymbol acceptorSymb,
                               int acceptorIndex,
                               UserCnstSymb paramSymb,
                               string appFun,
                               int argIndex,
                               Node appNode,
                               List<Flag> flags,
                               out string fromSpace,
                               out string toSpace)
            {
                if (composite == null)
                {
                    composite = new SymbolTable(env, new Location[] { transModule, module }, out compNamespaces);
                }

                var acceptorType = composite.Resolve(acceptorSymb, compNamespaces[1]).CanonicalForm[acceptorIndex];
                var paramCmpSymb = composite.Resolve(paramSymb, compNamespaces[0]);

                UserSymbol paramTypeSymb;
                paramCmpSymb.Namespace.TryGetSymbol(paramSymb.Name + "~Type", out paramTypeSymb);
                var paramType = paramTypeSymb.CanonicalForm[0];

                Set<Namespace> spaces;
                Namespace maxPrefix = null;
                LiftedBool isCoercable = LiftedBool.True;
                Set<UserSymbol> dataSorts = null;
                UserSymbol us;
                UserSortSymb uss;

                foreach (var pts in paramType.NonRangeMembers)
                {
                    if (pts.Kind == SymbolKind.UserSortSymb ||
                        pts.IsDataConstructor ||
                        pts.IsDerivedConstant ||
                        (pts.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)pts).IsTypeConstant))
                    {
                        if (pts.Kind == SymbolKind.UserSortSymb)
                        {
                            uss = (UserSortSymb)pts;
                            us = uss.DataSymbol;
                        }
                        else if (pts.IsDataConstructor)
                        {
                            us = (UserSymbol)pts;
                            uss = us.Kind == SymbolKind.ConSymb ? ((ConSymb)us).SortSymbol : ((MapSymb)us).SortSymbol;
                        }
                        else
                        {
                            uss = null;
                            us = (UserSymbol)pts;
                        }

                        if (maxPrefix == null)
                        {
                            maxPrefix = us.Namespace;
                        }
                        else
                        {
                            us.Namespace.TryGetPrefix(maxPrefix, out maxPrefix);
                        }

                        if (dataSorts == null)
                        {
                            dataSorts = new Set<UserSymbol>(Symbol.Compare);
                        }

                        dataSorts.Add(us);
                        if (!acceptorType.Contains(uss == null ? (Symbol)us : uss))
                        {
                            if (!acceptorType.TryGetRenamings(us.Name, out spaces))
                            {
                                var flag = new Flag(
                                    SeverityKind.Error,
                                    appNode,
                                    Constants.UnsafeArgType.ToString(
                                        argIndex + 1,
                                        appFun,
                                        GetPrintableName(pts.PrintableName)),
                                    Constants.UnsafeArgType.Code);
                                flags.Add(flag);
                                isCoercable = LiftedBool.False;
                            }
                            else if (isCoercable == LiftedBool.True)
                            {
                                isCoercable = LiftedBool.Unknown;
                            }
                        }
                    }
                    else if (pts.Kind == SymbolKind.BaseSortSymb)
                    {
                        if (!acceptorType.AcceptsConstants((BaseSortSymb)pts))
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                appNode,
                                Constants.UnsafeArgType.ToString(
                                    argIndex + 1,
                                    appFun,
                                    GetPrintableName(pts.PrintableName)),
                                Constants.UnsafeArgType.Code);
                            flags.Add(flag);
                            isCoercable = LiftedBool.False;
                        }
                    }
                    else if (!acceptorType.AcceptsConstant(pts))
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.UnsafeArgType.ToString(
                                argIndex + 1,
                                appFun,
                                GetPrintableName(pts.PrintableName)),
                            Constants.UnsafeArgType.Code);
                        flags.Add(flag);
                        isCoercable = LiftedBool.False;
                    }
                }

                foreach (var rng in paramType.RangeMembers)
                {
                    if (!acceptorType.AcceptsConstants(rng.Key, rng.Value))
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.UnsafeArgType.ToString(
                                argIndex + 1,
                                appFun,
                                rng.Key.ToString() + ".." + rng.Value.ToString()),
                            Constants.UnsafeArgType.Code);
                        flags.Add(flag);
                        isCoercable = LiftedBool.False;
                    }
                }

                if (isCoercable == false)
                {
                    fromSpace = toSpace = string.Empty;
                    return false;
                }
                else if (isCoercable == true)
                {
                    //// No coercion needed.
                    fromSpace = toSpace = string.Empty;
                    return true;
                }

                //// Step 2. Check that there is a unique coercion from the user sorts.
                Contract.Assert(dataSorts != null && maxPrefix != null);
                Set<Namespace> rnmgs = null, cndts;
                Namespace prefix;
                string[] suffix;

                foreach (var s in dataSorts)
                {
                    suffix = s.Namespace.Split(maxPrefix);
                    Contract.Assert(suffix != null);

                    acceptorType.TryGetRenamings(s.Name, out spaces);
                    cndts = new Set<Namespace>(Namespace.Compare);
                    foreach (var ns in spaces)
                    {
                        if (ns.Split(suffix, out prefix))
                        {
                            cndts.Add(prefix);
                        }
                    }

                    if (rnmgs == null)
                    {
                        rnmgs = cndts;
                    }
                    else
                    {
                        rnmgs.IntersectWith(cndts);
                    }

                    if (rnmgs.Count == 0)
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.UncoercibleArgType.ToString(
                                argIndex + 1,
                                appFun,
                                GetPrintableName(s.PrintableName)),
                            Constants.UncoercibleArgType.Code);
                        flags.Add(flag);
                        fromSpace = toSpace = string.Empty;
                        return false;
                    }
                }

                if (rnmgs.Count != 1)
                {
                    foreach (var ns in rnmgs)
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.AmbiguousCoercibleArg.ToString(
                                argIndex + 1,
                                appFun,
                                maxPrefix.FullName,
                                GetPrintableName(ns.FullName)),
                            Constants.AmbiguousCoercibleArg.Code);
                        flags.Add(flag);
                    }

                    fromSpace = toSpace = string.Empty;
                    return false;
                }

                var from = maxPrefix;
                var to = rnmgs.GetSomeElement();
                Symbol coerced;
                foreach (var ds in dataSorts)
                {
                    if (ds.Kind == SymbolKind.UserCnstSymb)
                    {
                        if (!composite.IsCoercible(ds, from, to, out coerced))
                        {
                            coerced = null;
                        }
                    }
                    else
                    {
                        uss = ds.Kind == SymbolKind.ConSymb ? ((ConSymb)ds).SortSymbol : ((MapSymb)ds).SortSymbol;
                        if (!composite.IsCoercible(uss, from, to, out coerced))
                        {
                            coerced = null;
                        }
                    }

                    if (coerced == null)
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.UncoercibleArgType.ToString(
                                argIndex + 1,
                                appFun,
                                GetPrintableName(ds.PrintableName)),
                            Constants.UncoercibleArgType.Code);
                        flags.Add(flag);
                        fromSpace = toSpace = string.Empty;
                        return false;
                    }
                }

                fromSpace = from.FullName;
                toSpace = to.FullName;
                return true;
            }

            private string GetPrintableName(string name)
            {
                for (int i = 0; i < compNamespaces.Length; ++i)
                {
                    if (name != null && name.StartsWith(compNamespaces[i] + "."))
                    {
                        return name.Substring(compNamespaces[i].Length + 1);
                    }
                }

                return name;
            }
        }

        private class ValueArg
        {
            private static char[] namespaceSep = new char[] { '.' };

            /// <summary>
            /// A term containing fresh variables standing for occurrences of value parameters.
            /// </summary>
            private Term valueTerm = null;
            public Term ValueTerm
            {
                get { return valueTerm; }
            }

            /// <summary>
            /// Maps a fresh variable standing for an occurrence of a value parameter
            /// to (1) the value parameter, (2) the input name space prefix, 
            /// (3) that should be replaced with the output namespace prefix.
            /// </summary>
            private Map<Term, MutableTuple<UserCnstSymb, string, string>> paramOccurs =
                new Map<Term, MutableTuple<UserCnstSymb, string, string>>(Term.Compare);

            public Term MkParamUse(TermIndex index, UserCnstSymb paramSymbol)
            {
                bool wasAdded;
                var freshVar = index.MkVar(string.Format("~prm{0}", paramOccurs.Count), true, out wasAdded);
                paramOccurs.Add(freshVar, new MutableTuple<UserCnstSymb, string, string>(paramSymbol, null, null));
                return freshVar;
            }

            /// <summary>
            /// Gets the parameter symbol that this variable stands for. 
            /// </summary>
            public UserCnstSymb GetParam(Term v)
            {
                return paramOccurs[v].Item1;
            }

            public Term Instantiate(SymbolTable systemTable, TermIndex appIndex, Map<string, Term> systemParams)
            {
                var paramMap = new Map<Term, Term>(Term.Compare);
                foreach (var kv in paramOccurs)
                {
                    var name = kv.Value.Item1.Name.Substring(1);
                    var cloned = MkClone(systemParams[name], systemTable, kv.Value.Item2, appIndex, kv.Value.Item3);
                    paramMap.Add(kv.Key, cloned);
                    Contract.Assert(cloned.Groundness == Groundness.Ground);
                }

                return MkClone(valueTerm, paramMap, appIndex);
            }

            public void SetRelabeling(Term freshVar, string from, string to)
            {
                var firstDot = from.IndexOf('.');
                paramOccurs[freshVar].Item2 = firstDot < 0 ? from : from.Substring(firstDot + 1);
                firstDot = to.IndexOf('.');
                paramOccurs[freshVar].Item3 = firstDot < 0 ? to : to.Substring(firstDot + 1);
            }

            public void SetValueTerm(Term t)
            {
                Contract.Requires(t != null);
                Contract.Assert(valueTerm == null);
                valueTerm = t;
            }

            private Term MkClone(Term t, Map<Term, Term> paramMap, TermIndex toIndex)
            {
                Contract.Requires(t != null);

                int i;
                Symbol sym;
                BaseCnstSymb bcs;
                bool wasAdded;
                return t.Compute<Term>(
                    (x, s) => x.Args,
                    (x, ch, s) =>
                    {
                        sym = x.Symbol;
                        switch (sym.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                                bcs = (BaseCnstSymb)sym;
                                switch (bcs.CnstKind)
                                {
                                    case CnstKind.Numeric:
                                        return toIndex.MkCnst((Rational)bcs.Raw, out wasAdded);
                                    case CnstKind.String:
                                        return toIndex.MkCnst((string)bcs.Raw, out wasAdded);
                                    default:
                                        throw new NotImplementedException();
                                }
                            case SymbolKind.UserCnstSymb:
                            case SymbolKind.ConSymb:
                            case SymbolKind.MapSymb:
                                if (sym.IsVariable)
                                {
                                    return paramMap[x];
                                }

                                i = 0;
                                var args = sym.Arity == 0 ? TermIndex.EmptyArgs : new Term[sym.Arity];
                                foreach (var a in ch)
                                {
                                    args[i++] = a;
                                }

                                return toIndex.MkApply(sym, args, out wasAdded);
                            default:
                                throw new NotImplementedException();
                        }
                    });
            }

            private Term MkClone(
                        Term t, 
                        SymbolTable fromTable, 
                        string from, 
                        TermIndex toIndex,
                        string to)
            {
                Contract.Requires(t != null);

                int i;
                Symbol sym;
                BaseCnstSymb bcs;
                Namespace tospace;
                UserSymbol us, usp;
                bool wasAdded;
                return t.Compute<Term>(
                    (x, s) => x.Args,
                    (x, ch, s) =>
                    {
                        sym = x.Symbol;
                        switch (sym.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                                bcs = (BaseCnstSymb)sym;
                                switch (bcs.CnstKind)
                                {
                                    case CnstKind.Numeric:
                                        return toIndex.MkCnst((Rational)bcs.Raw, out wasAdded);
                                    case CnstKind.String:
                                        return toIndex.MkCnst((string)bcs.Raw, out wasAdded);
                                    default:
                                        throw new NotImplementedException();
                                }
                            case SymbolKind.UserCnstSymb:
                            case SymbolKind.ConSymb:
                            case SymbolKind.MapSymb:
                                us = (UserSymbol)sym;
                                tospace = Relabel(fromTable, from, us.Namespace, toIndex.SymbolTable, to);
                                wasAdded = tospace.TryGetSymbol(us.Name, out usp);
                                Contract.Assert(wasAdded);
                                Contract.Assert(us.Arity == usp.Arity);
                                var args = us.Arity == 0 ? TermIndex.EmptyArgs : new Term[us.Arity];
                                i = 0;
                                foreach (var a in ch)
                                {
                                    args[i++] = a;
                                }

                                return toIndex.MkApply(usp, args, out wasAdded);
                            default:
                                throw new NotImplementedException();
                        }
                    });
            }

            private Namespace Relabel(SymbolTable fromTable, string from, Namespace fromTarget, SymbolTable toTable, string to)
            {
                Contract.Requires(fromTable != null && from != null && fromTarget != null);
                Contract.Requires(toTable != null && to != null);

                var space = fromTable.Root;
                var path = from.Split(namespaceSep, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < path.Length; ++i)
                {
                    if (!space.TryGetChild(path[i], out space))
                    {
                        throw new Impossible();
                    }
                }

                var suffix = fromTarget.Split(space);
                if (suffix == null)
                {
                    throw new Impossible();
                }


                space = toTable.Root;
                path = to.Split(namespaceSep, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < path.Length; ++i)
                {
                    if (!space.TryGetChild(path[i], out space))
                    {
                        throw new Impossible();
                    }
                }

                for (int i = 0; i < suffix.Length; ++i)
                {
                    if (!space.TryGetChild(suffix[i], out space))
                    {
                        throw new Impossible();
                    }
                }

                return space;
            }
        }
    }
}