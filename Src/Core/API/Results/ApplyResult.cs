namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Common;
    using Common.Rules;
    using Common.Terms;
    using Compiler;

    using Nodes;

    public sealed class ApplyResult
    {
        private TermIndex index;
        private CancellationToken cancel;
        private ExecuterStatistics stats;

        /// <summary>
        /// After an operation is completed, then API clients may indirectly create terms in the index of this application.
        /// These operations need to locked.
        /// </summary>
        private SpinLock termIndexLock = new SpinLock();

        /// <summary>
        /// This is non-null if the application applies a basic transform 
        /// </summary>
        private Executer basicTransExe = null;

        /// <summary>
        /// The apply target
        /// </summary>
        ModuleData applyTarget;

        /// <summary>
        /// The value parameters passed to the transform.
        /// </summary>
        private Map<string, Term> valueInputs;

        /// <summary>
        /// The model parameters passed to the transform.
        /// </summary>
        private Map<string, FactSet> modelInputs;

        /// <summary>
        /// The models created by the transform. Create when Start is called.
        /// </summary>
        private Map<string, Set<Term>> modelOutputs = null;

        public bool KeepDerivations
        {
            get;
            private set;
        }

        public DateTime StopTime
        {
            get;
            private set;
        }

        public bool WasCancelled
        {
            get;
            private set;
        }

        public ImmutableCollection<Id> OutputNames
        {
            get;
            private set;
        }

        internal ApplyResult(
            FactSet copy,
            ImmutableCollection<Id> outputNames,
            TermIndex index,
            ExecuterStatistics stats,
            bool keepDers,
            CancellationToken cancel)
        {
            Contract.Requires(copy != null && outputNames != null && index != null);
            this.cancel = cancel;
            this.stats = stats;
            this.index = index;

            applyTarget = (ModuleData)copy.Model.Node.CompilerData;
            KeepDerivations = keepDers;
            OutputNames = outputNames;

            valueInputs = new Map<string, Term>(string.Compare);
            modelInputs = new Map<string, FactSet>(string.Compare);
            modelInputs.Add(string.Empty, copy);
        }

        internal ApplyResult(
            ModuleData transform,
            Map<string, FactSet> modelInputs,
            Map<string, Term> valueInputs,
            ImmutableCollection<Id> outputNames,
            TermIndex index,
            ExecuterStatistics stats,
            bool keepDers,
            CancellationToken cancel)
        {
            Contract.Requires(transform != null);
            Contract.Requires(transform.Reduced.Node.NodeKind == NodeKind.Transform || transform.Reduced.Node.NodeKind == NodeKind.TSystem);
            Contract.Requires(modelInputs != null && valueInputs != null && outputNames != null);
            Contract.Requires(index != null);

            this.cancel = cancel;
            this.stats = stats;
            this.index = index;
            this.modelInputs = modelInputs;
            this.valueInputs = valueInputs;

            applyTarget = transform;
            KeepDerivations = keepDers;
            OutputNames = outputNames;
        }

        /// <summary>
        /// Returns a task that builds an output model. Returns null if there is no output model named outModelName.
        /// If aliasPrefix is null, then no aliasing.
        /// TODO: Support cancellation
        /// </summary>
        public Task<AST<Program>> GetOutputModel(
            string outModelName,
            ProgramName outProgName, 
            string aliasPrefix,
            CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outModelName));
            Contract.Requires(outProgName != null);

            Set<Term> facts;
            if (!modelOutputs.TryFindValue(outModelName, out facts))
            {
                return null;
            }

            aliasPrefix = aliasPrefix == null ? null : aliasPrefix.Trim();
            return Task.Factory.StartNew<AST<Program>>(() =>
                {
                    var bldr = new Builder();
                    var modelRef = MkModelDecl(outModelName, bldr);
                    var removeRenaming = applyTarget.Source.AST.Node.NodeKind == NodeKind.Transform;
                    var aliases = new Map<Term, string>(Term.Compare);
                    foreach (var t in facts)
                    {
                        BuildFactBody(facts, t, bldr, modelRef, aliasPrefix, aliases, removeRenaming);
                    }

                    int count;
                    bldr.GetStackCount(out count);
                    Contract.Assert(count == 0);
                    bldr.Load(modelRef);
                    bldr.Close();
                   
                    ImmutableArray<AST<Node>> asts;
                    bldr.GetASTs(out asts);

                    var prog = Factory.Instance.MkProgram(outProgName);
                    return Factory.Instance.AddModule(prog, asts[0]);
                });
        }

        /// <summary>
        /// Determines if a matching term was derived. 
        /// (Only if applied to a basic transform)
        /// </summary>
        /// <returns></returns>
        public LiftedBool IsDerivable(string t, out List<Flag> flags)
        {
            flags = new List<Flag>();
            if (applyTarget.Reduced.Node.NodeKind != NodeKind.Transform)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    applyTarget.Reduced.Node,
                    Constants.BadSyntax.ToString("This operation cannot be performed on this type of application."),
                    Constants.BadSyntax.Code));
                return LiftedBool.Unknown;
            }

            Term grndTerm;
            if (!ParseGoalWithDontCares(t, flags, out grndTerm))
            {
                return LiftedBool.Unknown;
            }

            return basicTransExe.IsDerived(grndTerm);
        }

        /// <summary>
        /// Enumerates all values that were derived by the query operation. 
        /// (Only if applied to a basic transform)
        /// 
        /// (If sort is true, then items are sorted and enumerated in sorted order.)
        /// </summary>
        /// <returns></returns>
        public IEnumerable<AST<Node>> EnumerateDerivations(string t, out List<Flag> flags, bool isSorted = false)
        {
            flags = new List<Flag>();
            return EnumerateDerivationsUsingFlags(t, flags, isSorted);
        }

        /// <summary>
        /// Enumerates proof trees (only if applied to a basic transform). 
        /// </summary>
        public IEnumerable<ProofTree> EnumerateProofs(string t, out List<Flag> flags, int proofsPerTerm = 0)
        {
            flags = new List<Flag>();
            return EnumerateProofsUsingFlags(t, flags, proofsPerTerm);
        }

        private IEnumerable<ProofTree> EnumerateProofsUsingFlags(string t, List<Flag> flags, int proofsPerTerm)
        {
            if (!basicTransExe.KeepDerivations)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.BadSyntax.ToString("Proofs were not stored."),
                    Constants.BadSyntax.Code));
                yield break;
            }
            else if (applyTarget.Reduced.Node.NodeKind != NodeKind.Transform)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    applyTarget.Reduced.Node,
                    Constants.BadSyntax.ToString("This operation cannot be performed on this type of application."),
                    Constants.BadSyntax.Code));
                yield break;
            }

            Term goalTerm;
            if (!ParseGoalWithDontCares(t, flags, out goalTerm))
            {
                yield break;
            }

            int count;
            foreach (var dt in basicTransExe.GetDerivedTerms(goalTerm))
            {
                count = 0;
                foreach (var p in basicTransExe.GetProofs(dt))
                {
                    ++count;
                    yield return p;

                    if (proofsPerTerm > 0 && count >= proofsPerTerm)
                    {
                        break;
                    }
                }
            }
        }

        private IEnumerable<AST<Node>> EnumerateDerivationsUsingFlags(string t, List<Flag> flags, bool isSorted)
        {
            Term goalTerm;
            if (!ParseGoalWithDontCares(t, flags, out goalTerm))
            {
                yield break;
            }

            Symbol s;
            if (isSorted)
            {
                var sorted = new Set<Term>(basicTransExe.TermIndex.LexicographicCompare);
                foreach (var dt in basicTransExe.GetDerivedTerms(goalTerm))
                {
                    s = dt.Symbol;
                    if ((s.Kind == SymbolKind.UserCnstSymb || s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb) &&
                        ((UserSymbol)s).Name.StartsWith(SymbolTable.ManglePrefix))
                    {
                        continue;
                    }

                    sorted.Add(dt);
                }

                foreach (var dt in sorted)
                {
                    yield return Factory.Instance.ToAST(dt);
                }
            }
            else
            {
                foreach (var dt in basicTransExe.GetDerivedTerms(goalTerm))
                {
                    s = dt.Symbol;
                    if ((s.Kind == SymbolKind.UserCnstSymb || s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb) &&
                        ((UserSymbol)s).Name.StartsWith(SymbolTable.ManglePrefix))
                    {
                        continue;
                    }

                    yield return Factory.Instance.ToAST(dt);
                }
            }
        }

        internal void Start()
        {
            Contract.Assert(modelOutputs == null);
            modelOutputs = new Map<string, Set<Term>>(string.Compare);

            //// Then this is a copy operation.
            if (applyTarget.Reduced.Node.NodeKind == NodeKind.Model)
            {
                var copySet = modelInputs[string.Empty];
                var copy = new Set<Term>(Term.Compare);
                foreach (var f in copySet.Facts)
                {
                    copy.Add(index.MkClone(f));
                }

                modelOutputs.Add(OutputNames.First<Id>().Name, copy);
            }
            else if (applyTarget.Reduced.Node.NodeKind == NodeKind.Transform)
            {
                var copyRules = ((RuleTable)applyTarget.FinalOutput).CloneTransformTable(index);
                var exe = new Executer(copyRules, modelInputs, valueInputs, stats, KeepDerivations, cancel);
                exe.Execute();
                basicTransExe = exe;

                var transToUserMap = MkTransToUserMap();
                foreach (var kv in transToUserMap)
                {
                    modelOutputs.Add(kv.Value, new Set<Term>(Term.Compare));
                }

                Symbol s;
                UserSymbol us;
                Namespace ns;
                Set<Term> output;
                string userName;
                foreach (var kv in exe.Fixpoint)
                {
                    s = kv.Key.Symbol;
                    if (!s.IsDataConstructor)
                    {
                        continue;
                    }

                    us = (UserSymbol)s;
                    if (us.Namespace.Parent == null || us.IsAutoGen || (s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew))
                    {
                        continue;
                    }

                    ns = us.Namespace;
                    while (ns.Parent.Parent != null)
                    {
                        ns = ns.Parent;
                    }

                    if (transToUserMap.TryFindValue(ns.Name, out userName) && 
                        modelOutputs.TryFindValue(userName, out output))
                    {
                        output.Add(kv.Key);
                    }
                }
            }
            else if (applyTarget.Reduced.Node.NodeKind == NodeKind.TSystem)
            {
                var task = ((Common.Composites.CoreTSystem)applyTarget.FinalOutput).Execute(modelInputs, valueInputs, cancel);
                task.Wait();
                var results = task.Result.Results; 
                using (var userOutIt = OutputNames.GetEnumerator())
                {
                    using (var transOutIt = ((TSystem)applyTarget.Reduced.Node).Outputs.GetEnumerator())
                    {
                        while (userOutIt.MoveNext() && transOutIt.MoveNext())
                        {
                            modelOutputs.Add(userOutIt.Current.Name, results[((ModRef)transOutIt.Current.Type).Rename].Facts);
                        }
                    }
                }

                results.Dispose();
            }
            else
            {
                throw new NotImplementedException();
            }

            StopTime = DateTime.Now;
            if (cancel.IsCancellationRequested)
            {
                WasCancelled = true;
            }
        }

        private void BuildFactBody(
            Set<Term> facts, 
            Term t, 
            Builder bldr, 
            BuilderRef modelRef,
            string aliasPrefix,
            Map<Term, string> aliases,
            bool removeRenaming)
        {
            Contract.Assert(t.Symbol.Kind == SymbolKind.ConSymb || t.Symbol.Kind == SymbolKind.MapSymb);
            string myAlias;
            if (aliasPrefix == null)
            {
                myAlias = null;
            }
            else
            {
                myAlias = ToAliasName((UserSymbol)t.Symbol, removeRenaming, aliasPrefix, aliases.Count);
                bldr.PushId(myAlias);
            }

            string alias;
            BaseCnstSymb bc;
            UserCnstSymb uc;
            var nsStack = new Stack<Namespace>();       
            t.Compute<Unit>(
                (x, s) =>
                {
                    if (aliases.ContainsKey(x))
                    {
                        return null;
                    }
                    else
                    {
                        if (x.Symbol.Kind == SymbolKind.ConSymb || x.Symbol.Kind == SymbolKind.MapSymb)
                        {
                            nsStack.Push(((UserSymbol)x.Symbol).Namespace);
                        }

                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            {
                                bc = (BaseCnstSymb)x.Symbol;
                                switch (bc.CnstKind)
                                {
                                    case CnstKind.Numeric:
                                        bldr.PushCnst((Rational)bc.Raw);
                                        break;
                                    case CnstKind.String:
                                        bldr.PushCnst((string)bc.Raw);
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }

                                break;
                            }
                        case SymbolKind.UserCnstSymb:
                            {
                                uc = (UserCnstSymb)x.Symbol;
                                Contract.Assert(uc.IsNewConstant && !uc.IsSymbolicConstant);
                                bldr.PushId(ToIdString(uc, nsStack.Peek()));
                                break;
                            }
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            {
                                if (aliases.TryFindValue(x, out alias))
                                {
                                    bldr.PushId(alias);
                                }
                                else
                                {
                                    nsStack.Pop();
                                    bldr.PushId(x != t ? ToIdString((UserSymbol)x.Symbol, nsStack.Peek()) : ToIdString((UserSymbol)x.Symbol, removeRenaming));
                                    bldr.PushFuncTerm();
                                    for (int i = 0; i < x.Args.Length; ++i)
                                    {
                                        bldr.AddFuncTermArg();
                                    }
                                }

                                break;
                            }
                        default:
                            throw new NotImplementedException();
                    }

                    return default(Unit);
                });

            if (aliasPrefix == null)
            {
                bldr.PushAnonModelFact();
                bldr.Load(modelRef);
                bldr.AddModelFact(true);
                bldr.Pop();
            }
            else
            {
                bldr.PushModelFact();
                bldr.Load(modelRef);
                bldr.AddModelFact(true);
                bldr.Pop();
                aliases.Add(t, myAlias);
            }
        }

        private Map<string, string> MkTransToUserMap()
        {
            var transToUserMap = new Map<string, string>(string.Compare);
            string userName, transName;
            using (var userOutIt = OutputNames.GetEnumerator())
            {
                using (var transOutIt = ((Transform)applyTarget.Reduced.Node).Outputs.GetEnumerator())
                {
                    while (userOutIt.MoveNext() && transOutIt.MoveNext())
                    {
                        userName = userOutIt.Current.Name;
                        transName = ((ModRef)transOutIt.Current.Type).Rename;
                        transToUserMap.Add(transName, userName);
                    }
                }
            }

            return transToUserMap;
        }

        private BuilderRef MkModelDecl(string modelName, Builder bldr)
        {
            BuilderRef result;
            if (applyTarget.Reduced.Node.NodeKind == NodeKind.Model)
            {
                var domLoc = (Location)((Model)applyTarget.Reduced.Node).Domain.CompilerData;
                bldr.PushModRef(((Domain)domLoc.AST.Node).Name, null, ((Program)domLoc.AST.Root).Name.ToString());
                bldr.PushModel(modelName, false, ComposeKind.None);
                bldr.Store(out result);
                return result;
            }
            else if (applyTarget.Reduced.Node.NodeKind == NodeKind.Transform)
            {
                using (var userOutIt = OutputNames.GetEnumerator())
                {
                    using (var transOutIt = ((Transform)applyTarget.Reduced.Node).Outputs.GetEnumerator())
                    {
                        while (userOutIt.MoveNext() && transOutIt.MoveNext())
                        {
                            if (userOutIt.Current.Name != modelName)
                            {
                                continue;
                            }

                            var domLoc = (Location)((ModRef)transOutIt.Current.Type).CompilerData;
                            bldr.PushModRef(((Domain)domLoc.AST.Node).Name, null, ((Program)domLoc.AST.Root).Name.ToString());
                            bldr.PushModel(modelName, false, ComposeKind.None);
                            bldr.Store(out result);
                            return result;
                        }
                    }
                }

                throw new Impossible();
            }
            else if (applyTarget.Reduced.Node.NodeKind == NodeKind.TSystem)
            {
                using (var userOutIt = OutputNames.GetEnumerator())
                {
                    using (var transOutIt = ((TSystem)applyTarget.Reduced.Node).Outputs.GetEnumerator())
                    {
                        while (userOutIt.MoveNext() && transOutIt.MoveNext())
                        {
                            if (userOutIt.Current.Name != modelName)
                            {
                                continue;
                            }

                            var domLoc = (Location)((ModRef)transOutIt.Current.Type).CompilerData;
                            bldr.PushModRef(((Domain)domLoc.AST.Node).Name, null, ((Program)domLoc.AST.Root).Name.ToString());
                            bldr.PushModel(modelName, false, ComposeKind.None);
                            bldr.Store(out result);
                            return result;
                        }
                    }
                }

                throw new Impossible();
            }

            throw new NotImplementedException();
        }

        private static string ToEscapedName(string name)
        {
            var firstPrime = name.IndexOf('\'');
            if (firstPrime < 0 || firstPrime >= name.Length)
            {
                return name;
            }

            return string.Format("{0}_{1}Prime", name.Substring(0, firstPrime), name.Length - firstPrime); 
        }

        private static string ToAliasName(UserSymbol symb, bool removeRenaming, string aliasPrefix, int id)
        {
            string name = string.Format("{0}__0x{1:x}", ToEscapedName(symb.Name), id);
            var ns = symb.Namespace;
            while (ns.Parent != null && (!removeRenaming || ns.Parent.Parent != null))
            {
                name = ToEscapedName(ns.Name) + "__" + name;
                ns = ns.Parent;
            }

            return string.Format("{0}__{1}", ToEscapedName(aliasPrefix), name);
        }

        private static string ToIdString(UserSymbol symb, bool removeRenaming)
        {
            //// If this is in the root namespace, then the string is always just the name.
            if (symb.Namespace.Parent == null)
            {
                return symb.Name;
            }

            //// Otherwise, drop the topmost renaming if requested.
            string name = symb.Name;
            var ns = symb.Namespace;
            while (ns.Parent != null && (!removeRenaming || ns.Parent.Parent != null))
            {
                name = ns.Name + "." + name;
                ns = ns.Parent;
            }

            return name;
        }

        private static string ToIdString(UserSymbol symb, Namespace parent)
        {
            //// If this is in the root namespace, then the string is always just the name.
            if (symb.Namespace.Parent == null)
            {
                return symb.Name;            
            }

            //// Otherwise, factor out the namespace included in the parent.
            string name = symb.Name;
            var ns = symb.Namespace;
            while (ns != parent)
            {
                name = ns.Name + "." + name;
                ns = ns.Parent;
            }

            return name;
        }

        private bool ParseGoalWithDontCares(string t, List<Flag> flags, out Term goalTerm)
        {
            Contract.Requires(basicTransExe != null);

            ImmutableCollection<Flag> parseFlags;
            var ast = Factory.Instance.ParseDataTerm(t, out parseFlags, index.Env.Parameters);
            flags.AddRange(parseFlags);
            if (ast == null)
            {
                goalTerm = null;
                return false;
            }
            else if (WasCancelled)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ast.Node,
                    Constants.BadSyntax.ToString("Application operation was cancelled; derivation is unknown."),
                    Constants.BadSyntax.Code));
                goalTerm = null;
                return false;
            }

            var simplified = Compiler.EliminateQuotations((Configuration)((Transform)applyTarget.Reduced.Node).Config.CompilerData, ast, flags);
            if (simplified.NodeKind != NodeKind.Id && simplified.NodeKind != NodeKind.Cnst && simplified.NodeKind != NodeKind.FuncTerm)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    simplified,
                    Constants.BadSyntax.ToString("Expected an identifier, constant, or function"),
                    Constants.BadSyntax.Code));
                goalTerm = null;
                return false;
            }

            goalTerm = Expand(Factory.Instance.ToAST(simplified), flags);
            Contract.Assert(goalTerm == null || goalTerm.Groundness != Groundness.Type);
            return goalTerm != null;
        }

        /// <summary>
        /// Converts a term AST to a term, and expands symbolic constants as much as possible. 
        /// Returns null if there are errors.
        /// Should only be called after the set has been successfully compiled.
        /// </summary>
        private Term Expand(AST<Node> ast, List<Flag> flags)
        {
            bool gotLock = false;
            try
            {
                termIndexLock.Enter(ref gotLock);

                UserSymbol other;
                var symTable = index.SymbolTable;
                var valParamToValue = new Map<UserCnstSymb, Term>(Symbol.Compare);                
                foreach (var kv in valueInputs)
                {
                    valParamToValue.Add(
                        (UserCnstSymb)symTable.Resolve(string.Format("%{1}", kv.Key), out other, symTable.ModuleSpace),
                        kv.Value);
                }

                var nextDcVarId = new MutableTuple<int>(0);
                var success = new SuccessToken();
                var symbStack = new Stack<Tuple<Namespace, Symbol>>();
                symbStack.Push(new Tuple<Namespace, Symbol>(index.SymbolTable.Root, null));
                var result = ast.Compute<Tuple<Term, Term>>(
                    x => Expand_Unfold(x, symbStack, nextDcVarId, success, flags),
                    (x, y) => Expand_Fold(x, y, symbStack, valParamToValue, success, flags));
                return result == null ? null : result.Item1;
            }
            finally
            {
                if (gotLock)
                {
                    termIndexLock.Exit();
                }
            }
        }

        private IEnumerable<Node> Expand_Unfold(Node n,
                                                Stack<Tuple<Namespace, Symbol>> symbStack,
                                                MutableTuple<int> nextDcVarId,
                                                SuccessToken success,
                                                List<Flag> flags)
        {
            var space = symbStack.Peek().Item1;
            switch (n.NodeKind)
            {
                case NodeKind.Cnst:
                    {
                        bool wasAdded;
                        var cnst = (Cnst)n;
                        BaseCnstSymb symb;
                        switch (cnst.CnstKind)
                        {
                            case CnstKind.Numeric:
                                symb = (BaseCnstSymb)index.MkCnst((Rational)cnst.Raw, out wasAdded).Symbol;
                                break;
                            case CnstKind.String:
                                symb = (BaseCnstSymb)index.MkCnst((string)cnst.Raw, out wasAdded).Symbol;
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
                        if (index.SymbolTable.HasRenamingPrefix(id))
                        {
                            if (!Resolve(id.Name, "constant", id, space, x => x.IsNonVarConstant, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }
                        else if (id.Fragments.Length == 1 && id.Name == API.ASTQueries.ASTSchema.Instance.DontCareName)
                        {
                            bool wasAdded;
                            var fresh = index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "dc", nextDcVarId.Item1), true, out wasAdded);
                            ++nextDcVarId.Item1;
                            symbStack.Push(new Tuple<Namespace, Symbol>(space, fresh.Symbol));
                            return null;
                        }
                        else if (!Resolve(id.Fragments[0], "variable or constant", id, space, x => x.Kind == SymbolKind.UserCnstSymb, out symb, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else if (id.Fragments.Length > 1 && symb.IsNonVarConstant)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                id,
                                Constants.BadSyntax.ToString("constants do not have fields"),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else if (symb.IsVariable)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                id,
                                Constants.BadSyntax.ToString("Variables cannot appear here."),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }

                        symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                        return null;
                    }
                case NodeKind.FuncTerm:
                    {
                        var ft = (FuncTerm)n;
                        if (ft.Function is Id)
                        {
                            UserSymbol symb;
                            var ftid = (Id)ft.Function;
                            if (ValidateUse_UserFunc(ft, space, out symb, flags, true))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            }
                            else
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            return ft.Args;
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

        private Tuple<Term, Term> Expand_Fold(
                   Node n,
                   IEnumerable<Tuple<Term, Term>> args,
                   Stack<Tuple<Namespace, Symbol>> symbStack,
                   Map<UserCnstSymb, Term> valParamToValue,
                   SuccessToken success,
                   List<Flag> flags)
        {
            bool wasAdded;
            var space = symbStack.Peek().Item1;
            var symb = symbStack.Pop().Item2;

            if (symb == null)
            {
                return null;
            }
            if (symb.IsNonVarConstant)
            {
                var cnst = symb as UserCnstSymb;
                if (cnst != null && cnst.IsSymbolicConstant)
                {
                    var expDef = valParamToValue[cnst];
                    Contract.Assert(expDef != null);
                    return new Tuple<Term, Term>(expDef, index.MkDataWidenedType(expDef));
                }
                else
                {
                    var valTerm = index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                    return new Tuple<Term, Term>(valTerm, valTerm);
                }
            }
            else if (symb.IsVariable)
            {
                var varTerm = index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                return new Tuple<Term, Term>(varTerm, varTerm);
            }
            else if (symb.IsDataConstructor)
            {
                var con = (UserSymbol)symb;
                var sort = symb.Kind == SymbolKind.ConSymb ? ((ConSymb)con).SortSymbol : ((MapSymb)con).SortSymbol;

                var i = 0;
                var vargs = new Term[con.Arity];
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
                    else if (a.Item2.Symbol.IsNonVarConstant)
                    {
                        if (!sort.DataSymbol.CanonicalForm[i].AcceptsConstant(a.Item2.Symbol))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadArgType.ToString(i + 1, sort.DataSymbol.FullName),
                                Constants.BadArgType.Code));
                            success.Failed();
                            typed = false;
                            continue;
                        }
                    }
                    else if (a.Item2.Symbol.Kind == SymbolKind.UserSortSymb)
                    {
                        if (!sort.DataSymbol.CanonicalForm[i].Contains(a.Item2.Symbol))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadArgType.ToString(i + 1, sort.DataSymbol.FullName),
                                Constants.BadArgType.Code));
                            success.Failed();
                            typed = false;
                            continue;
                        }
                    }
                    else if (!a.Item2.Symbol.IsVariable)
                    {
                        //// Only don't care variables are allowed, which always type check.
                        throw new NotImplementedException();
                    }

                    vargs[i] = a.Item1;
                    ++i;
                }

                if (!typed)
                {
                    success.Failed();
                    return null;
                }

                return new Tuple<Term, Term>(
                            index.MkApply(con, vargs, out wasAdded),
                            index.MkApply(sort, TermIndex.EmptyArgs, out wasAdded));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private bool Resolve(
                        string id,
                        string kind,
                        Node n,
                        Namespace space,
                        Predicate<UserSymbol> validator,
                        out UserSymbol symbol,
                        List<Flag> flags)
        {
            UserSymbol other = null;

            symbol = index.SymbolTable.Resolve(id, out other, space);
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

        private bool ValidateUse_UserFunc(FuncTerm ft, Namespace space, out UserSymbol symbol, List<Flag> flags, bool allowDerived = false)
        {
            Contract.Assert(ft.Function is Id);
            var result = true;
            var id = (Id)ft.Function;

            if (!Resolve(id.Name, "constructor", id, space, x => x.IsDataConstructor, out symbol, flags))
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

            if (symbol.Kind == SymbolKind.ConSymb && !allowDerived && !((ConSymb)symbol).IsNew)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.ModelNewnessError.ToString(),
                    Constants.ModelNewnessError.Code));
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
    }
}
