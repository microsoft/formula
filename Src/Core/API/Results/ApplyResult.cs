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
        /// TODO: Support cancellation
        /// </summary>
        public Task<AST<Program>> GetOutputModel(
            string outModelName,
            ProgramName outProgName, 
            string aliasPrefix,
            CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(outModelName));
            Contract.Requires(outProgName != null && !string.IsNullOrWhiteSpace(aliasPrefix));

            Set<Term> facts;
            if (!modelOutputs.TryFindValue(outModelName, out facts))
            {
                return null;
            }

            aliasPrefix = aliasPrefix.Trim();
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
                var exe = new Executer(copyRules, modelInputs, valueInputs, stats, KeepDerivations);
                exe.Execute();

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
            var myAlias = ToAliasName((UserSymbol)t.Symbol, removeRenaming, aliasPrefix, aliases.Count);
            bldr.PushId(myAlias);

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

            bldr.PushModelFact();
            bldr.Load(modelRef);
            bldr.AddModelFact(true);
            bldr.Pop();
            aliases.Add(t, myAlias);
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
    }
}
