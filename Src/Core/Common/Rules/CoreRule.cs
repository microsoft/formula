namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Solver;
    using Compiler;
    using Extras;
    using Terms;

    internal class CoreRule
    {
        private static readonly char[] ClassesSplits = new char[] { ',' };

        private enum ConstrainNodeKind { Ground, Nonground, TypeRel, EqRel }
        private enum InitStatusKind { Uninit, Success, Fail }
        public enum RuleKind { Regular, Sub }

        private Set<Term> initConstrs;
        private Map<Term, ConstraintNode> nodes = new Map<Term, ConstraintNode>(Term.Compare);
        private Matcher matcher1;
        private ConstraintNode[] projVector1;
        private Matcher matcher2;
        private ConstraintNode[] projVector2;
        private InitStatusKind initStatus = InitStatusKind.Uninit;
        protected int stratum = -1;

        public virtual RuleKind Kind
        {
            get { return RuleKind.Regular; }
        }

        public TermIndex Index
        {
            get;
            private set;
        }

        /// <summary>
        /// True if the rule computes an unconstrained product of two non-ground matches
        /// </summary>
        public bool IsProductRule
        {
            get;
            private set;
        }

        /// <summary>
        /// Can be null if this rule is a partial rule.
        /// </summary>
        public Node Node
        {
            get;
            private set;
        }

        /// <summary>
        /// The program where this rule occured. Can be null if partial rule.
        /// </summary>
        public ProgramName ProgramName
        {
            get;
            private set;
        }

        public Term Head
        {
            get;
            private set;
        }

        public Term HeadType
        {
            get;
            private set;
        }

        public Term Trigger1
        {
            get;
            private set;
        }

        public FindData Find1
        {
            get;
            private set;
        }

        public Term Trigger2
        {
            get;
            private set;
        }

        public FindData Find2
        {
            get;
            private set;
        }

        /// <summary>
        /// The set of constraints provided to this rule.
        /// </summary>
        public IEnumerable<Term> Constraints
        {
            get { return initConstrs; }
        }

        public int RuleId
        {
            get;
            private set;
        }

        public int Stratum
        {
            get
            {
                Contract.Assert(stratum >= 0);
                return stratum;
            }

            set            
            {
                Contract.Assert(stratum == -1);
                stratum = value;
            }
        }

        public Set<Symbol> ComprehensionSymbols
        {
            get;
            private set;
        }

        /// <summary>
        /// True if this rule is a clone of another rule.
        /// </summary>
        public bool IsClone
        {
            get;
            private set;
        }

        /// <summary>
        /// Can be null if the rule has no classes.
        /// </summary>
        public Set<string> RuleClasses
        {
            get;
            private set;
        }

        /// <summary>
        /// If true, then an event is generated whenever this rule produces a new value
        /// </summary>
        public bool IsWatched
        {
            get;
            private set;
        }

        /// <summary>
        /// Contains an additional set of equalities of the form x = t that were implied by the rule body.
        /// Can be null.
        /// </summary>
        public Map<Term, Set<Term>> AdditionalVarDefs
        {
            get;
            private set;
        }

        public CoreRule(
            int ruleId, 
            Term head, 
            FindData f1, 
            FindData f2, 
            IEnumerable<Term> constrs, 
            Predicate<Symbol> isCompr,
            Term headType = null, 
            Node node = null,
            ProgramName programName = null,
            ConstraintSystem environment = null)
        {
            Contract.Requires(head != null && constrs != null);
            Index = head.Owner;
            RuleId = ruleId;
            Head = head;
            HeadType = headType == null ? head.Owner.FalseValue : headType;
            Find1 = f1;
            Find2 = f2;
            Node = node;
            ProgramName = programName;
            ComprehensionSymbols = new Set<Symbol>(Symbol.Compare);

            initConstrs = new Set<Term>(Term.Compare, constrs);
            foreach (var c in constrs)
            {
                AddConstraint(c, isCompr);
            }

            //// Comprehensions in the head do not count as reads
            AddConstraint(head, x => false);

            bool wasAdded;
            if (!f1.IsNull)
            {
                matcher1 = new Matcher(f1.Pattern);
                if (f1.Binding.Symbol.IsReservedOperation)
                {
                    AddConstraint(Index.MkApply(Index.SymbolTable.GetOpSymbol(RelKind.Eq), new Term[] { f1.Pattern, f1.Pattern }, out wasAdded), isCompr);
                }
                else
                {
                    AddConstraint(Index.MkApply(Index.SymbolTable.GetOpSymbol(RelKind.Eq), new Term[] { f1.Binding, f1.Pattern }, out wasAdded), isCompr);
                }
            }
            else
            {
                matcher1 = null;
                projVector1 = null;
            }

            if (!f2.IsNull)
            {
                matcher2 = new Matcher(f2.Pattern);
                if (f2.Binding.Symbol.IsReservedOperation)
                {
                    AddConstraint(Index.MkApply(Index.SymbolTable.GetOpSymbol(RelKind.Eq), new Term[] { f2.Pattern, f2.Pattern }, out wasAdded), isCompr);
                }
                else
                {
                    AddConstraint(Index.MkApply(Index.SymbolTable.GetOpSymbol(RelKind.Eq), new Term[] { f2.Binding, f2.Pattern }, out wasAdded), isCompr);
                }
            }
            else
            {
                matcher2 = null;
                projVector2 = null;
            }

            if (!Find1.IsNull && !Find2.IsNull)
            {
                Map<Term, int> pattrnPos1, pattrnPos2;

                //// The set of vars determined by constraints alone.
                var alwaysDet1 = GetDeterminedVars(new Set<Term>(Term.Compare));
                var alwaysDet2 = new Set<Term>(Term.Compare, alwaysDet1);

                var find1Vars = GetFindVariables(Find1, out pattrnPos1);
                var find2Det = GetDeterminedVars(find1Vars);
                var find2Vars = GetFindVariables(Find2, out pattrnPos2);
                var find1Det = GetDeterminedVars(find2Vars);

                find1Det.IntersectWith(find1Vars);
                alwaysDet1.IntersectWith(find1Vars);

                Trigger1 = Executer.MkPattern(Find1.Pattern, find1Det);
                projVector1 = MkProjectionVector(pattrnPos1, find1Det);

                find2Det.IntersectWith(find2Vars);
                alwaysDet2.IntersectWith(find2Vars);

                Trigger2 = Executer.MkPattern(Find2.Pattern, find2Det);
                projVector2 = MkProjectionVector(pattrnPos2, find2Det);

                //// This is a product rule if the find vars of neither pattern contrain
                //// the find vars of the other pattern.
                IsProductRule = Find1.Pattern.Groundness != Groundness.Ground && 
                                Find2.Pattern.Groundness != Groundness.Ground &&
                                (find1Det.Count == alwaysDet1.Count) && 
                                (find2Det.Count == alwaysDet2.Count);
            }
            else if (!Find1.IsNull)
            {
                Trigger1 = Executer.MkPattern(Find1.Pattern, null);
            }
            else if (!Find2.IsNull)
            {
                Trigger2 = Executer.MkPattern(Find2.Pattern, null);
            }

            if (environment != null)
            {
                AdditionalVarDefs = new Map<Term, Set<Term>>(Term.Compare);
                foreach (var v in environment.Variables)
                {
                    var set = new Set<Term>(Term.Compare);
                    AdditionalVarDefs.Add(v, set);
                    foreach (var m in environment.GetCongruenceMembers(v))
                    {
                        if (m != v)
                        {
                            set.Add(m);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Base Constructor for CoreSubRule
        /// </summary>
        protected CoreRule(int ruleId, Term head, FindData f1)
        {
            Index = head.Owner;
            RuleId = ruleId;
            Head = head;

            //// HeadType is irrelevant
            HeadType = head.Owner.FalseValue;
            Find1 = f1;
            Trigger1 = Executer.MkPattern(Find1.Pattern, null);
            Find2 = default(FindData);
            Node = null;
            ComprehensionSymbols = new Set<Symbol>(Symbol.Compare);
            initConstrs = new Set<Term>(Term.Compare);
        }

        public virtual CoreRule Clone(
            int ruleId,
            Predicate<Symbol> isCompr,
            TermIndex index,
            Map<Term, Term> bindingReificationCache,
            Map<UserSymbol, UserSymbol> symbolTransfer,
            string renaming)
        {
            FindData cloneFind1 = default(FindData);
            if (!Find1.IsNull)
            {
                cloneFind1 = new FindData(
                    Find1.Binding.Symbol.IsReservedOperation ? bindingReificationCache[Find1.Binding] : index.MkClone(Find1.Binding, renaming, symbolTransfer),
                    index.MkClone(Find1.Pattern, renaming, symbolTransfer),
                    index.MkClone(Find1.Type, renaming, symbolTransfer));
            }

            FindData cloneFind2 = default(FindData);
            if (!Find2.IsNull)
            {
                cloneFind2 = new FindData(
                    Find2.Binding.Symbol.IsReservedOperation ? bindingReificationCache[Find2.Binding] : index.MkClone(Find2.Binding, renaming, symbolTransfer),
                    index.MkClone(Find2.Pattern, renaming, symbolTransfer),
                    index.MkClone(Find2.Type, renaming, symbolTransfer));
            }

            var cloneRule = new CoreRule(
                ruleId,
                index.MkClone(Head, renaming, symbolTransfer),
                cloneFind1,
                cloneFind2,
                CloneConstraints(index, symbolTransfer, renaming),
                isCompr,
                index.MkClone(HeadType, renaming, symbolTransfer),
                Node,
                ProgramName);

            if (AdditionalVarDefs != null)
            {
                cloneRule.AdditionalVarDefs = new Map<Term, Set<Term>>(Term.Compare);
                foreach (var kv in AdditionalVarDefs)
                {
                    var v = index.MkClone(kv.Key, renaming, symbolTransfer);
                    var set = new Set<Term>(Term.Compare);
                    cloneRule.AdditionalVarDefs.Add(v, set);
                    foreach (var rhs in kv.Value)
                    {
                        set.Add(index.MkClone(rhs, renaming, symbolTransfer));
                    }
                }
            }

            cloneRule.IsClone = true;
            cloneRule.MergeConfigurations(this);
            return cloneRule;
        }

        /// <summary>
        /// Performs the following substitution:
        /// This rule: 
        /// h :- F(t), [H(t'),] body.
        /// 
        /// Inliner:
        /// F(x1,...,xn) :- [G(s),] body'. (inliner has no more than one find and x_i's are distinct.) 
        /// 
        /// Produces:
        /// h :- [G(s)[x/t],], body, body'[x/t].
        /// 
        /// Or returns this rule if inlining cannot be applied.
        /// NOTE: Under current naming scheme, variables with identical names in rule and inliner and semantically 
        /// the same variable.
        /// </summary>
        /// <param name="eliminator"></param>
        /// <returns></returns>
        public virtual CoreRule OptInlinePartialRule(CoreRule inliner, out bool succeeded)
        {
            //// The inliner is expected to be a partial rule with at most one find.
            if (!inliner.Find2.IsNull)
            {
                succeeded = false;
                return this;
            }

            Term pattern;
            var head = inliner.Head;
            Map<Term, Term> inliner1 = null;
            if (!Find1.IsNull && (pattern = Find1.Pattern).Symbol == inliner.Head.Symbol)
            {
                inliner1 = new Map<Term,Term>(Term.Compare);
                for (int i = 0; i < head.Args.Length; ++i)
                {
                    if (!head.Args[i].Symbol.IsVariable || inliner1.ContainsKey(head.Args[i]))
                    {
                        succeeded = false;
                        return this;
                    }

                    inliner1.Add(head.Args[i], pattern.Args[i]);
                }

                if (AdditionalVarDefs != null)
                {
                    foreach (var x in AdditionalVarDefs.Keys)
                    {
                        if (!inliner1.ContainsKey(x))
                        {
                            inliner1.Add(x, x);
                        }
                    }
                }
            }

            Map<Term, Term> inliner2 = null;
            if (!Find2.IsNull && (pattern = Find2.Pattern).Symbol == inliner.Head.Symbol)
            {
                inliner2 = new Map<Term, Term>(Term.Compare);
                for (int i = 0; i < head.Args.Length; ++i)
                {
                    if (!head.Args[i].Symbol.IsVariable || inliner2.ContainsKey(head.Args[i]))
                    {
                        succeeded = false;
                        return this;
                    }

                    inliner2.Add(head.Args[i], pattern.Args[i]);
                }

                if (AdditionalVarDefs != null)
                {
                    foreach (var x in AdditionalVarDefs.Keys)
                    {
                        if (!inliner2.ContainsKey(x))
                        {
                            inliner2.Add(x, x);
                        }

                    }
                }
            }

            //// If no find matches the inliner, then fail.
            if (inliner1 == null && inliner2 == null)
            {
                succeeded = false;
                return this;
            }

            var optAddVarDefs = new Map<Term, Set<Term>>(Term.Compare);
            if (AdditionalVarDefs != null)
            {
                foreach (var kv in AdditionalVarDefs)
                {
                    AddVarDefs(optAddVarDefs, kv.Key, kv.Value);
                }
            }
         
            //// string varPrefix;
            var inlinedFind1 = Find1;
            var inlinedFind2 = Find2;
            var inlinedConstrs = new Set<Term>(Term.Compare, Constraints);
            if (inliner1 != null)
            {
                //// Under current naming scheme, prefixing is not required.
                //// varPrefix = string.Format("~inl~{0}~1", inliner.RuleId);

                foreach (var kv in inliner1)
                {
                    AddVarDef(optAddVarDefs, kv.Key, kv.Value);
                }

                foreach (var c in inliner.Constraints)
                {
                    inlinedConstrs.Add(Substitute(c, inliner1));
                }

                if (!inliner.Find1.IsNull)
                {
                    Term b, p;
                    inlinedFind1 = new FindData(
                        b = Substitute(inliner.Find1.Binding, inliner1),
                        p = Substitute(inliner.Find1.Pattern, inliner1),
                        Substitute(inliner.Find1.Type, inliner1));

                    if (b.Symbol.IsVariable)
                    {
                        AddVarDef(optAddVarDefs, b, p);
                    }
                }
                else
                {
                    inlinedFind1 = new FindData();
                }

                if (inliner.AdditionalVarDefs != null)
                {
                    foreach (var kv in inliner.AdditionalVarDefs)
                    {
                        var x = Substitute(kv.Key, inliner1);
                        if (!x.Symbol.IsVariable)
                        {
                            continue;
                        }

                        foreach (var rhs in kv.Value)
                        {
                            AddVarDef(optAddVarDefs, x, Substitute(rhs, inliner1));
                        }
                    }
                }
            }

            if (inliner2 != null)
            {
                //// Under current naming scheme, prefixing is not required.
                //// varPrefix = string.Format("~inl~{0}~2", inliner.RuleId);

                foreach (var kv in inliner2)
                {
                    AddVarDef(optAddVarDefs, kv.Key, kv.Value);
                }

                foreach (var c in inliner.Constraints)
                {
                    inlinedConstrs.Add(Substitute(c, inliner2));
                }

                if (!inliner.Find1.IsNull)
                {
                    Term b, p;
                    inlinedFind2 = new FindData(
                        b = Substitute(inliner.Find1.Binding, inliner2),
                        p = Substitute(inliner.Find1.Pattern, inliner2),
                        Substitute(inliner.Find1.Type, inliner2));

                    if (b.Symbol.IsVariable)
                    {
                        AddVarDef(optAddVarDefs, b, p);
                    }
                }
                else
                {
                    inlinedFind2 = new FindData();
                }

                if (inliner.AdditionalVarDefs != null)
                {
                    foreach (var kv in inliner.AdditionalVarDefs)
                    {
                        var x = Substitute(kv.Key, inliner2);
                        if (!x.Symbol.IsVariable)
                        {
                            continue;
                        }

                        foreach (var rhs in kv.Value)
                        {
                            AddVarDef(optAddVarDefs, x, Substitute(rhs, inliner2));
                        }
                    }
                }
            }

            succeeded = true;
            CoreRule optRule;
            if (inlinedFind1.IsNull && !inlinedFind2.IsNull)
            {
                optRule = new CoreRule(
                    RuleId,
                    Head,
                    inlinedFind2,
                    inlinedFind1,
                    inlinedConstrs,
                    x => (ComprehensionSymbols.Contains(x) || inliner.ComprehensionSymbols.Contains(x)),
                    HeadType,
                    Node,
                    ProgramName);
            }
            else
            {
                optRule = new CoreRule(
                    RuleId,
                    Head,
                    inlinedFind1,
                    inlinedFind2,
                    inlinedConstrs,
                    x => (ComprehensionSymbols.Contains(x) || inliner.ComprehensionSymbols.Contains(x)),
                    HeadType,
                    Node,
                    ProgramName);
            }

            optRule.AdditionalVarDefs = optAddVarDefs;
            optRule.MergeConfigurations(this, inliner);
            return optRule;
        }

        public void MergeConfigurations(params CoreRule[] rules)
        {
            foreach (var r in rules)
            {
                if (r == this)
                {
                    continue;
                }

                //// Step 1. Merge rule classes
                if (r.RuleClasses != null)
                {
                    if (RuleClasses == null)
                    {
                        RuleClasses = new Set<string>(string.Compare);
                    }

                    foreach (var cls in r.RuleClasses)
                    {
                        RuleClasses.Add(cls);
                    }
                }

                //// Step 2. Merge IsWatched
                IsWatched = IsWatched || r.IsWatched;
            }
        }

        public void MergeConfigurations(Node node)
        {
            if (node == null)
            {
                return;
            }

            Config configNode = null;
            switch (node.NodeKind)
            {
                case NodeKind.Rule:
                    configNode = ((Rule)node).Config;
                    break;
                case NodeKind.ContractItem:
                    configNode = ((ContractItem)node).Config;
                    break;
                default:
                    return;
            }

            if (configNode == null)
            {
                return;
            }

            var config = (Configuration)configNode.CompilerData;

            //// (1) Try to configure rule classes
            Cnst setting;
            if (config.TryGetSetting(Configuration.Rule_ClassesSetting, out setting))
            {
                var classArr = setting.GetStringValue().Split(ClassesSplits, StringSplitOptions.RemoveEmptyEntries);

                if (RuleClasses == null)
                {
                    RuleClasses = new Set<string>(string.Compare);
                }

                foreach (var cls in classArr)
                {
                    var clsTrimmed = cls.Trim();
                    if (clsTrimmed != string.Empty)
                    {
                        RuleClasses.Add(clsTrimmed);
                    }
                }
            }

            //// (2) Try to configure IsWatched
            if (config.TryGetSetting(Configuration.Rule_WatchSetting, out setting) && 
                setting.GetStringValue() == ASTSchema.Instance.ConstNameTrue)
            {
                IsWatched = true;
            }
        }

        private static void Debug_Print(Map<Term, Set<Term>> optAddVarDefs)
        {
            foreach (var kv in optAddVarDefs)
            {
                Console.WriteLine("{0} =", kv.Key.Debug_GetSmallTermString());
                foreach (var eq in kv.Value)
                {
                    Console.WriteLine("   {0}", eq.Debug_GetSmallTermString());
                }
            }
        }

        public virtual void Execute(
            Term binding,
            int findNumber,
            SymExecuter index,
            Set<Term> pending)
        {
            if (initStatus == InitStatusKind.Uninit)
            {
                initStatus = Initialize(index) ? InitStatusKind.Success : InitStatusKind.Fail;
            }

            if (initStatus == InitStatusKind.Fail)
            {
                return;
            }

            //// Case 1. There are no finds.
            ConstraintNode headNode = nodes[Head];
            if (Find1.IsNull && Find2.IsNull)
            {
                Contract.Assert(headNode.Binding != null);
                Pend(index, pending, headNode.Binding, Index.FalseValue, Index.FalseValue);
                return;
            }

            //// Case 2. There is a least one find.
            Matcher matcher;
            switch (findNumber)
            {
                case 0:
                    matcher = matcher1;
                    break;
                case 1:
                    matcher = matcher2;
                    break;
                default:
                    throw new Impossible();
            }

            if (!ApplyMatch(index, matcher, binding, ConstraintNode.BLFirst))
            {
                return;
            }
            else if (Find1.IsNull || Find2.IsNull)
            {
                Contract.Assert(headNode.Binding != null);
                Pend(
                    index,
                    pending,
                    headNode.Binding,
                    findNumber == 0 ? binding : Index.FalseValue,
                    findNumber == 1 ? binding : Index.FalseValue);

                UndoPropagation(ConstraintNode.BLFirst);

                return;
            }
        }

        public virtual void Execute(
            Term binding, 
            int findNumber, 
            Executer index, 
            bool keepDerivations,
            Map<Term, Set<Derivation>> pending)
        {
            ActivationStatistics astats = null;
            if (index.Statistics != null)
            {
                astats = index.Statistics.GetActivations(this);
                astats.BeginActivation();
            }

            if (initStatus == InitStatusKind.Uninit)
            {
                initStatus = Initialize(index) ? InitStatusKind.Success : InitStatusKind.Fail;
            }

            if (initStatus == InitStatusKind.Fail)
            {
                if (astats != null)
                {
                    astats.IncFailCount();
                    astats.EndActivation();
                }

                return;
            }

            //// Debug_PrintRule();
            //// Case 1. There are no finds.
            ConstraintNode headNode = nodes[Head];
            if (Find1.IsNull && Find2.IsNull)
            {
                Contract.Assert(headNode.Binding != null);
                Pend(keepDerivations, index, pending, headNode.Binding, Index.FalseValue, Index.FalseValue);
                if (astats != null)
                {
                    astats.IncPendCount();
                    astats.EndActivation();
                }

                return;
            }

            //// Case 2. There is a least one find.
            Matcher matcher;
            switch (findNumber)
            {
                case 0:
                    matcher = matcher1;
                    break;
                case 1:
                    matcher = matcher2;
                    break;
                default:
                    throw new Impossible();
            }

            //// Console.Write("\tExecuting {0} with {1} bound to {2}...", RuleId, findNumber, binding.Debug_GetSmallTermString());

            if (!ApplyMatch(index, matcher, binding, ConstraintNode.BLFirst))
            {
                if (astats != null)
                {
                    astats.IncFailCount();
                    astats.EndActivation();
                }

                //// Console.WriteLine("binding failed");
                return;
            }
            else if (Find1.IsNull || Find2.IsNull)
            {
                //// Console.WriteLine("binding succeeded");
                
                Contract.Assert(headNode.Binding != null);
                Pend(
                    keepDerivations,
                    index,
                    pending,
                    headNode.Binding,
                    findNumber == 0 ? binding : Index.FalseValue,
                    findNumber == 1 ? binding : Index.FalseValue);

                UndoPropagation(ConstraintNode.BLFirst);
                if (astats != null)
                {
                    astats.IncPendCount();
                    astats.EndActivation();
                }

                return;
            }

            //// Console.WriteLine("binding partially succeeded");
            //// Case 3. There are two finds. Need to join with another pattern.
            Term pattern;
            FindData otherFind;
            Matcher otherMatcher;
            ConstraintNode[] projVector;
            switch (findNumber)
            {
                case 0:
                    pattern = Trigger2;
                    otherFind = Find2;
                    projVector = projVector2;
                    otherMatcher = matcher2;
                    break;
                case 1:
                    pattern = Trigger1;
                    otherFind = Find1;
                    projVector = projVector1;
                    otherMatcher = matcher1;
                    break;
                default:
                    throw new Impossible();
            }

            //// Query the pattern and enumerate over all bindings.
            IEnumerable<Term> queryResults;
            if (pattern.Symbol.IsVariable)
            {
                queryResults = index.Query(otherFind.Type, nodes[otherFind.Pattern].Binding);
            }
            else
            {
                var projection = new Term[projVector.Length];
                for (int i = 0; i < projection.Length; ++i)
                {
                    Contract.Assert(projVector[i].Binding != null);
                    projection[i] = projVector[i].Binding;
                }

                queryResults = index.Query(pattern, projection);
            }

            foreach (var tp in queryResults)
            {
                //// Console.Write("\tExecuting {0} with {1} bound to {2}...", RuleId, findNumber == 0 ? 1 : 0, tp.Debug_GetSmallTermString());

                if (ApplyMatch(index, otherMatcher, tp, ConstraintNode.BLSecond))
                {
                    //// Console.WriteLine("binding succeeded");
                    Contract.Assert(headNode.Binding != null);
                    Pend(
                        keepDerivations,
                        index,
                        pending,
                        headNode.Binding,
                        findNumber == 0 ? binding : tp,
                        findNumber == 1 ? binding : tp);

                    UndoPropagation(ConstraintNode.BLSecond);
                    if (astats != null)
                    {
                        astats.IncPendCount();
                    }
                }
                else
                {
                    if (astats != null)
                    {
                        astats.IncFailCount();
                    }
                }
            }

            UndoPropagation(ConstraintNode.BLFirst);
            if (astats != null)
            {
                astats.EndActivation();
            }
        }

        public static int Compare(CoreRule r1, CoreRule r2)
        {
            return r1.RuleId - r2.RuleId;
        }

        public virtual void Debug_DumpRule(StringBuilder builder)
        {
            Contract.Requires(builder != null);
            builder.AppendFormat("{2}ID: {0}, Stratum: {1}\n",
                RuleId,
                stratum < 0 ? "?" : stratum.ToString(),
                IsProductRule ? "(PROD) " : string.Empty);

            if (Head.Symbol.PrintableName.StartsWith(SymbolTable.ManglePrefix))
            {
                builder.AppendFormat("{0}\n", Head.Debug_GetSmallTermString());
            }
            else
            {
                builder.AppendFormat(
                    "{0}: {1}\n",
                    Head.Debug_GetSmallTermString(),
                    HeadType.Debug_GetSmallTermString());
            }

            builder.AppendFormat("  :-\n");
            if (!Find1.IsNull)
            {
                if (Find1.Binding.Symbol.IsReservedOperation)
                {
                    builder.AppendFormat("    [{0}]\n", Find1.Pattern.Debug_GetSmallTermString());
                }
                else
                {
                    builder.AppendFormat(
                        "    {0}[{1}: {2}]\n",
                        Find1.Binding.Debug_GetSmallTermString(),
                        Find1.Pattern.Debug_GetSmallTermString(),
                        Find1.Type.Debug_GetSmallTermString());
                }
            }

            if (!Find2.IsNull)
            {
                if (Find2.Binding.Symbol.IsReservedOperation)
                {
                    builder.AppendFormat("    [{0}]\n", Find2.Pattern.Debug_GetSmallTermString());
                }
                else
                {
                    builder.AppendFormat(
                        "    {0}[{1}: {2}]\n",
                        Find2.Binding.Debug_GetSmallTermString(),
                        Find2.Pattern.Debug_GetSmallTermString(),
                        Find2.Type.Debug_GetSmallTermString());
                }
            }

            foreach (var c in initConstrs)
            {
                builder.AppendFormat("    {0}\n", c.Debug_GetSmallTermString());
            }

            builder.AppendFormat("  .\n\n");
        }

        public virtual void Debug_PrintRule()
        {
            Console.WriteLine("{2}ID: {0}, Stratum: {1}", 
                RuleId, 
                stratum < 0 ? "?" : stratum.ToString(),
                IsProductRule ? "(PROD) " : string.Empty);

            if (Head.Symbol.PrintableName.StartsWith(SymbolTable.ManglePrefix))
            {
                Console.WriteLine(Head.Debug_GetSmallTermString());
            }
            else
            {
                Console.WriteLine(
                    "{0}: {1}",
                    Head.Debug_GetSmallTermString(),
                    HeadType.Debug_GetSmallTermString());
            }

            Console.WriteLine("  :-");
            if (!Find1.IsNull)
            {
                if (Find1.Binding.Symbol.IsReservedOperation)
                {
                    Console.WriteLine("    [{0}]", Find1.Pattern.Debug_GetSmallTermString());
                }
                else
                {
                    Console.WriteLine(
                        "    {0}[{1}: {2}]",
                        Find1.Binding.Debug_GetSmallTermString(),
                        Find1.Pattern.Debug_GetSmallTermString(),
                        Find1.Type.Debug_GetSmallTermString());
                }
            }

            if (!Find2.IsNull)
            {
                if (Find2.Binding.Symbol.IsReservedOperation)
                {
                    Console.WriteLine("    [{0}]", Find2.Pattern.Debug_GetSmallTermString());
                }
                else
                {
                    Console.WriteLine(
                        "    {0}[{1}: {2}]",
                        Find2.Binding.Debug_GetSmallTermString(),
                        Find2.Pattern.Debug_GetSmallTermString(),
                        Find2.Type.Debug_GetSmallTermString());
                }
            }

            foreach (var c in initConstrs)
            {
                Console.WriteLine("    {0}", c.Debug_GetSmallTermString());
            }

            Console.WriteLine("    .");
        }

        protected void Pend(
            SymExecuter index,
            Set<Term> pending,
            Term t,
            Term bind1,
            Term bind2)
        {
            if (!index.Exists(t) && !pending.Contains(t))
            {
                pending.Add(t);
            }

        }

        protected void Pend(
            bool keepDerivations,
            Executer index,
            Map<Term, Set<Derivation>> pending,
            Term t,
            Term bind1,
            Term bind2)
        {
            if (!keepDerivations)
            {
                if (!index.Exists(t) && !pending.ContainsKey(t))
                {
                    pending.Add(t, null);
                }

                return;
            }

            var d = new Derivation(this, bind1, bind2);
            if (index.IfExistsThenDerive(t, d))
            {
                return;
            }

            Set<Derivation> dervs;
            if (!pending.TryFindValue(t, out dervs))
            {
                dervs = new Set<Derivation>(Derivation.Compare);
                pending.Add(t, dervs);
            }

            dervs.Add(d);
        }

        private static void AddVarDef(Map<Term, Set<Term>> varDefs, Term vt, Term def)
        {
            Contract.Requires(varDefs != null && vt != null && def != null);
            Contract.Requires(vt.Symbol.IsVariable);
            if (vt == def)
            {
                return;
            }

            Set<Term> eqs;
            if (!varDefs.TryFindValue(vt, out eqs))
            {
                eqs = new Set<Term>(Term.Compare);
                varDefs.Add(vt, eqs);
            }

            eqs.Add(def);
        }

        private static void AddVarDefs(Map<Term, Set<Term>> varDefs, Term vt, Set<Term> defs)
        {
            Contract.Requires(varDefs != null && vt != null && defs != null);
            Contract.Requires(vt.Symbol.IsVariable);

            Set<Term> eqs;
            if (!varDefs.TryFindValue(vt, out eqs))
            {
                eqs = new Set<Term>(Term.Compare);
                varDefs.Add(vt, eqs);
            }

            foreach (var def in defs)            
            {
                if (vt != def)
                {
                    eqs.Add(def);
                }
            }
        }

        private bool ApplyMatch(SymExecuter facts, Matcher m, Term t, int bindingLevel)
        {
            Term pattern = m.Pattern;
            Map<Term, Term> bindings = new Map<Term, Term>(Term.Compare);
            bool result = true;
            if (Unifier.IsUnifiable(pattern, t, true, bindings))
            {
                foreach (var kv in bindings)
                {
                    if (!Propagate(facts, kv.Key, kv.Value, bindingLevel))
                    {
                        result = false;
                        break;
                    }
                }
            }

            if (!result)
            {
                UndoPropagation(bindingLevel);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Unbinds only if the application fails constraints
        /// </summary>
        private bool ApplyMatch(Executer facts, Matcher m, Term t, int bindingLevel)
        {
            var result = m.TryMatch(t);
            Contract.Assert(result);
            result = true;
            foreach (var kv in m.CurrentBindings)
            {
                if (!Propagate(facts, kv.Key, kv.Value, bindingLevel))
                {
                    result = false;
                    break;
                }
            }

            if (!result)
            {
                UndoPropagation(bindingLevel);
                return false;
            }

            return true;
        }

        private ConstraintNode[] MkProjectionVector(Map<Term, int> patternPos, Set<Term> boundVars)
        {
            var projMap = new Map<int, ConstraintNode>((x, y) => x - y);
            int pos;
            foreach (var v in boundVars)
            {
                if (patternPos.TryFindValue(v, out pos))
                {
                    projMap.Add(pos, nodes[v]);
                }
            }

            var arr = new ConstraintNode[projMap.Count];
            int i = 0;
            foreach (var kv in projMap)
            {
                arr[i++] = kv.Value;
            }

            return arr;
        }

        private IEnumerable<Term> CloneConstraints(TermIndex index, Map<UserSymbol, UserSymbol> symbolTransfer, string renaming)
        {
            foreach (var c in initConstrs)
            {
                yield return index.MkClone(c, renaming, symbolTransfer);
            }
        }

        private Term Substitute(Term t, Map<Term, Term> substitution)
        {
            Contract.Requires(t != null && substitution != null);

            int i;
            Term sub;
            bool wasAdded;
            return t.Compute<Term>(
                (x, s) => x.Groundness != Groundness.Ground ? x.Args : null,
                (x, ch, s) =>
                {
                    if (x.Groundness == Groundness.Ground)
                    {
                        return x;
                    }
                    else if (x.Symbol.IsVariable)
                    {
                        if (substitution.TryFindValue(x, out sub))
                        {
                            return sub;
                        }
                        else
                        {
                            return x;
                        }
                    }

                    i = 0;
                    foreach (var tp in ch)
                    {
                        if (tp != x.Args[i])
                        {
                            break;
                        }
                        else
                        {
                            ++i;
                        }
                    }

                    if (i == x.Args.Length)
                    {
                        return x;
                    }

                    var args = new Term[x.Args.Length];
                    i = 0;
                    foreach (var tp in ch)
                    {
                        args[i++] = tp; 
                    }

                    return Index.MkApply(x.Symbol, args, out wasAdded);
                });
        }

        private void UndoPropagation(int bindingLevel)
        {
            foreach (var kv in nodes)
            {
                kv.Value.Undo(bindingLevel);
            }
        }

        private void AddConstraint(Term t, Predicate<Symbol> isCompr)
        {
            if (nodes.ContainsKey(t))
            {
                return;
            }

            int i;
            ConstraintNode n;
            ConstraintNode first, second;
            var eqSymbol = Index.SymbolTable.GetOpSymbol(RelKind.Eq);
            t.Compute<ConstraintNode>(
                (x, s) =>
                {
                    if (nodes.ContainsKey(x))
                    {
                        return null;
                    }
                    else if (x.Symbol == x.Owner.TypeRelSymbol)
                    {
                        return Extras.EnumerableMethods.GetEnumerable<Term>(x.Args[0]);
                    }
                    else if (x.Groundness == Groundness.Ground && x.Symbol != eqSymbol)
                    {
                        foreach (var tp in x.Enumerate(y => y.Args))
                        {
                            if (tp.Symbol.IsDataConstructor && isCompr(tp.Symbol))
                            {
                                ComprehensionSymbols.Add(tp.Symbol);
                            }
                        }

                        return null;
                    }
                    else
                    {
                        if (x.Symbol.IsDataConstructor && isCompr(x.Symbol))
                        {
                            ComprehensionSymbols.Add(x.Symbol);
                        }

                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    if (nodes.TryFindValue(x, out n))
                    {
                        return n;
                    }
                    else if (x.Symbol == x.Owner.TypeRelSymbol)
                    {
                        first = ch.First<ConstraintNode>();
                        n = new TypeRelNode(x, first);
                        first.AddUseList(n, 0);
                        nodes.Add(x, n);
                        return n;
                    }
                    else if (x.Symbol == eqSymbol)
                    {
                        using (var it = ch.GetEnumerator())
                        {
                            it.MoveNext();
                            first = it.Current;
                            it.MoveNext();
                            second = it.Current;
                        }

                        n = new EqNode(x, first, second);
                        first.AddUseList(n, 0);
                        second.AddUseList(n, 1);
                        nodes.Add(x, n);
                        return n;
                    }
                    else if (x.Groundness == Groundness.Ground)
                    {
                        n = new GroundNode(x);
                        nodes.Add(x, n);
                        return n;
                    }
                    else
                    {
                        n = new NongroundNode(x, ch.ToArray<ConstraintNode>());
                        i = 0;
                        foreach (var m in ch)
                        {
                            m.AddUseList(n, i++);
                        }

                        nodes.Add(x, n);
                        return n;
                    }
                });
        }

        /// <summary>
        /// Execute and propagate all ground nodes. 
        /// </summary>
        private bool Initialize(SymExecuter facts)
        {
            var stack = new Stack<ConstraintNode>();
            foreach (var kv in nodes)
            {
                kv.Value.BindingLevel = ConstraintNode.BLUnbound;
                kv.Value.EvaluationLevel = ConstraintNode.BLUnbound;
                if (kv.Value.Kind == ConstrainNodeKind.Ground)
                {
                    if (!kv.Value.TryEval(facts, ConstraintNode.BLInit))
                    {
                        return false;
                    }

                    stack.Push(kv.Value);
                }
            }

            return true;
        }

        /// <summary>
        /// Execute and propagate all ground nodes. 
        /// </summary>
        private bool Initialize(Executer facts)
        {
            var stack = new Stack<ConstraintNode>();
            foreach (var kv in nodes)
            {
                kv.Value.BindingLevel = ConstraintNode.BLUnbound;
                kv.Value.EvaluationLevel = ConstraintNode.BLUnbound;
                if (kv.Value.Kind == ConstrainNodeKind.Ground)
                {
                    if (!kv.Value.TryEval(facts, ConstraintNode.BLInit))
                    {
                        return false;
                    }

                    stack.Push(kv.Value);
                }
            }

            bool canEval;
            ConstraintNode top, arg;
            while (stack.Count > 0)
            {
                top = stack.Pop();
                foreach (var u in top.UseList)
                {
                    if (u.Item1.IsEvaluated)
                    {
                        continue;
                    }

                    if (u.Item1.Kind == ConstrainNodeKind.EqRel)
                    {
                        arg = u.Item1[u.Item2 == 0 ? 1 : 0];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding, ConstraintNode.BLInit))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                    }

                    canEval = true;
                    for (int i = 0; i < u.Item1.NArgs; ++i)
                    {
                        if (u.Item1[i].Binding == null)
                        {
                            canEval = false;
                            break;
                        }
                    }

                    if (canEval)
                    {
                        if (!u.Item1.TryEval(facts, ConstraintNode.BLInit))
                        {
                            return false;
                        }

                        stack.Push(u.Item1);
                    }
                }

                if (top.Term.Groundness == Groundness.Variable && 
                    top.Term.Symbol.IsDataConstructor)
                {
                    for (int i = 0; i < top.NArgs; ++i)
                    {
                        arg = top[i];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding.Args[i], ConstraintNode.BLInit))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                        else if (!arg.TryBind(top.Binding.Args[i], ConstraintNode.BLInit))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;           
        }

        private bool Propagate(SymExecuter facts, Term varTerm, Term binding, int bindingLevel)
        {
            Contract.Requires(facts != null && binding != null);
            Contract.Requires(varTerm != null && varTerm.Symbol.IsVariable);

            var stack = new Stack<ConstraintNode>();
            var varNode = nodes[varTerm];
            if (varNode.Binding != null)
            {
                if (varNode.TryBind(binding, bindingLevel))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if (!varNode.TryBind(binding, bindingLevel))
            {
                return false;
            }

            stack.Push(varNode);

            bool canEval;
            ConstraintNode top, arg;
            while (stack.Count > 0)
            {
                top = stack.Pop();
                foreach (var u in top.UseList)
                {
                    if (u.Item1.IsEvaluated)
                    {
                        continue;
                    }

                    if (u.Item1.Kind == ConstrainNodeKind.EqRel)
                    {
                        arg = u.Item1[u.Item2 == 0 ? 1 : 0];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding, bindingLevel))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                    }

                    canEval = true;
                    for (int i = 0; i < u.Item1.NArgs; ++i)
                    {
                        if (u.Item1[i].Binding == null)
                        {
                            canEval = false;
                            break;
                        }
                    }

                    if (canEval)
                    {
                        if (!u.Item1.TryEval(facts, bindingLevel))
                        {
                            return false;
                        }

                        stack.Push(u.Item1);
                    }
                }

                if (top.Term.Groundness == Groundness.Variable &&
                    top.Term.Symbol.IsDataConstructor)
                {
                    if (top.NArgs != top.Binding.Args.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < top.NArgs; ++i)
                    {
                        arg = top[i];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding.Args[i], bindingLevel))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                        else if (!arg.TryBind(top.Binding.Args[i], bindingLevel))
                        {
                            return false;
                        }
                    }
                }
            }

                return true;
        }

            /// <summary>
            /// Propagate a variable binding.
            /// </summary>
        private bool Propagate(Executer facts, Term varTerm, Term binding, int bindingLevel)
        {
            Contract.Requires(facts != null && binding != null);
            Contract.Requires(varTerm != null && varTerm.Symbol.IsVariable);

            var stack = new Stack<ConstraintNode>();
            var varNode = nodes[varTerm];
            if (varNode.Binding != null)
            {
                if (varNode.TryBind(binding, bindingLevel))
                {
                    return true;
                }
                else
                {
                    return false;
                }         
            }
            else if (!varNode.TryBind(binding, bindingLevel))
            {
                return false;
            }

            stack.Push(varNode);

            bool canEval;
            ConstraintNode top, arg;
            while (stack.Count > 0)
            {
                top = stack.Pop();
                foreach (var u in top.UseList)
                {
                    if (u.Item1.IsEvaluated)
                    {
                        continue;
                    }

                    if (u.Item1.Kind == ConstrainNodeKind.EqRel)
                    {
                        arg = u.Item1[u.Item2 == 0 ? 1 : 0];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding, bindingLevel))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                    }

                    canEval = true;
                    for (int i = 0; i < u.Item1.NArgs; ++i)
                    {
                        if (u.Item1[i].Binding == null)
                        {
                            canEval = false;
                            break;
                        }
                    }

                    if (canEval)
                    {
                        if (!u.Item1.TryEval(facts, bindingLevel))
                        {
                            return false;
                        }

                        stack.Push(u.Item1);
                    }
                }

                if (top.Term.Groundness == Groundness.Variable &&
                    top.Term.Symbol.IsDataConstructor)
                {
                    if (top.NArgs != top.Binding.Args.Length)
                    {
                        return false;
                    }

                    for (int i = 0; i < top.NArgs; ++i)
                    {
                        arg = top[i];
                        if (arg.Binding == null)
                        {
                            if (!arg.TryBind(top.Binding.Args[i], bindingLevel))
                            {
                                return false;
                            }

                            stack.Push(arg);
                        }
                        else if (!arg.TryBind(top.Binding.Args[i], bindingLevel))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private Set<Term> GetFindVariables(FindData d, out Map<Term, int> pattrnPos)
        {
            Contract.Requires(!d.IsNull);
            var lclPos = new Map<Term, int>(Term.Compare);
            var vars = new Set<Term>(Term.Compare);
            d.Pattern.Visit(
                x => x.Groundness == Groundness.Variable ? x.Args : null,
                x =>
                {
                    if (x.Symbol.IsVariable)
                    {
                        vars.Add(x);
                        if (!lclPos.ContainsKey(x))
                        {
                            lclPos.Add(x, lclPos.Count);
                        }
                    }
                });

            if (!d.Binding.Symbol.IsReservedOperation && d.Binding.Symbol.IsVariable)
            {
                vars.Add(d.Binding);
            }

            pattrnPos = lclPos;
            return vars;
        }

        /// <summary>
        /// Computes the set of all variables that are determined, assuming
        /// all constraints are satisfied by boundVars;
        /// </summary>
        private Set<Term> GetDeterminedVars(Set<Term> boundVars)
        {
            Contract.Requires(boundVars != null);

            var stack = new Stack<ConstraintNode>();
            var detVars = new Set<Term>(Term.Compare);
            foreach (var kv in nodes)
            {
                if (kv.Value.Kind == ConstrainNodeKind.Ground)
                {
                    kv.Value.BindingLevel = ConstraintNode.BLInit;
                    stack.Push(kv.Value);
                }
                else if (kv.Key.Symbol.IsVariable && boundVars.Contains(kv.Key))
                {
                    kv.Value.BindingLevel = ConstraintNode.BLFirst;
                    stack.Push(kv.Value);
                }
                else
                {
                    kv.Value.BindingLevel = ConstraintNode.BLUnbound;                
                }
            }

            ConstraintNode top, arg;
            bool isDetermined;
            while (stack.Count > 0)
            {
                top = stack.Pop();
                if (top.Term.Symbol.IsVariable)
                {
                    detVars.Add(top.Term);
                }

                foreach (var u in top.UseList)
                {
                    if (u.Item1.BindingLevel != ConstraintNode.BLUnbound)
                    {
                        continue;
                    }

                    if (u.Item1.Kind == ConstrainNodeKind.EqRel)
                    {
                        u.Item1.BindingLevel = ConstraintNode.BLFirst;
                        stack.Push(u.Item1);
                        arg = u.Item1[u.Item2 == 0 ? 1 : 0];
                        if (arg.BindingLevel == ConstraintNode.BLUnbound)
                        {
                            arg.BindingLevel = ConstraintNode.BLFirst;
                            stack.Push(arg);
                        }

                        continue;
                    }

                    isDetermined = true;
                    for (int i = 0; i < u.Item1.NArgs; ++i)
                    {
                        if (u.Item1[i].BindingLevel == ConstraintNode.BLUnbound)
                        {
                            isDetermined = false;
                            break;
                        }
                    }

                    if (isDetermined)
                    {
                        u.Item1.BindingLevel = ConstraintNode.BLFirst;
                        stack.Push(u.Item1);
                    }
                }

                if (top.Term.Groundness == Groundness.Variable && top.Term.Symbol.IsDataConstructor)
                {
                    for (int i = 0; i < top.NArgs; ++i)
                    {
                        arg = top[i];
                        if (arg.BindingLevel == ConstraintNode.BLUnbound)
                        {
                            arg.BindingLevel = ConstraintNode.BLFirst;
                            stack.Push(arg);
                        }
                    }
                }
            }

            return detVars;
        }

        private abstract class ConstraintNode : Bindable
        {
            public const int BLUnbound = -1;
            public const int BLInit = 0;
            public const int BLFirst = 1;
            public const int BLSecond = 2;

            protected LinkedList<Tuple<ConstraintNode, int>> useList =
                new LinkedList<Tuple<ConstraintNode, int>>();

            public Term Term
            {
                get;
                private set;
            }

            /// <summary>
            /// The level at which this node was bound.
            /// </summary>
            public int BindingLevel
            {
                get;
                set;
            }

            /// <summary>
            /// The level at which this node was evaluated. 
            /// If evaluation caused binding, then equals the binding level.
            /// </summary>
            public int EvaluationLevel
            {
                get;
                set;
            }

            public bool IsEvaluated
            {
                get { return EvaluationLevel != BLUnbound; }
            }

            public abstract ConstrainNodeKind Kind
            {
                get;
            }

            public abstract int NArgs
            {
                get;
            }

            public abstract ConstraintNode this[int i]
            {
                get;
            }

            public virtual void Undo(int bindingLevel)
            {
                Contract.Requires(bindingLevel > BLUnbound);
                Contract.Assert(EvaluationLevel == BLUnbound || EvaluationLevel >= BindingLevel);
                if (BindingLevel >= bindingLevel)
                {
                    Binding = null;
                    BindingLevel = BLUnbound;
                }

                if (EvaluationLevel >= bindingLevel)
                {
                    EvaluationLevel = BLUnbound;
                }
            }

            public IEnumerable<Tuple<ConstraintNode, int>> UseList
            {
                get { return useList; }
            }

            public ConstraintNode(Term t)
            {
                Contract.Requires(t != null);
                Term = t;
                BindingLevel = BLUnbound;
                EvaluationLevel = BLUnbound;
            }

            public void AddUseList(ConstraintNode n, int index)
            {
                Contract.Requires(n != null && index >= 0 && index < n.Term.Args.Length);
                useList.AddLast(new Tuple<ConstraintNode, int>(n, index));
            }

            /// <summary>
            /// Try to set of the expected result of this constraint node.
            /// </summary>
            public abstract bool TryBind(Term t, int bindingLevel);

            /// <summary>
            /// Try to evaluate this node at the binding level.
            /// </summary>
            public abstract bool TryEval(Executer facts, int bindingLevel);

            /// <summary>
            /// Try to evaluate this node at the binding level.
            /// </summary>
            public abstract bool TryEval(SymExecuter facts, int bindingLevel);
        }

        private class TypeRelNode : ConstraintNode
        {
            private ConstraintNode arg;

            private Term zero, emptyString;
            /// <summary>
            /// Decomposes the type term for quick lookup and testing.
            /// All numeric subtypes are placed in zero. 
            /// All string subtypes are placed in empty string.
            /// </summary>
            private Map<Symbol, Term> typeBins = new Map<Symbol, Term>(Symbol.Compare);

            public override int NArgs
            {
                get { return 1; }
            }

            public override ConstraintNode this[int i]
            {
                get 
                {
                    Contract.Assert(i == 0);
                    return arg;
                }
            }

            public override ConstrainNodeKind Kind
            {
                get { return ConstrainNodeKind.TypeRel; }
            }

            public TypeRelNode(Term t, ConstraintNode arg)
                : base(t)
            {
                Contract.Requires(arg != null && t.Symbol == t.Owner.TypeRelSymbol);
                this.arg = arg;
                ConstructTypeBins(t.Args[1]);
            }

            public override bool TryBind(Term t, int bindingLevel)
            {
                if (Binding == null)
                {
                    if (t != Term.Owner.TrueValue)
                    {
                        return false;
                    }

                    Binding = t;
                    BindingLevel = bindingLevel;
                    return true;
                }

                return Binding == t;
            }

            public override bool TryEval(SymExecuter facts, int bindingLevel)
            {
                return true;
            }

            public override bool TryEval(Executer facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;

                Term typeTerm;
                BaseCnstSymb cs;
                var bindSymb = arg.Binding.Symbol;
                switch (bindSymb.Kind)
                {
                    case SymbolKind.UserCnstSymb:
                        break;
                    case SymbolKind.ConSymb:
                        bindSymb = ((ConSymb)bindSymb).SortSymbol;
                        break;
                    case SymbolKind.MapSymb:
                        bindSymb = ((MapSymb)bindSymb).SortSymbol;
                        break;
                    case SymbolKind.BaseCnstSymb:
                        cs = (BaseCnstSymb)bindSymb;
                        if (cs.CnstKind == CnstKind.Numeric)
                        {
                            bindSymb = zero.Symbol;
                        }
                        else if (cs.CnstKind == CnstKind.String)
                        {
                            bindSymb = emptyString.Symbol;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        break;
                }

                if (!typeBins.TryFindValue(bindSymb, out typeTerm))
                {
                    return false;
                }
                else if (!typeTerm.Owner.IsGroundMember(typeTerm, arg.Binding))
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = typeTerm.Owner.TrueValue;
                    BindingLevel = bindingLevel;
                    return true;
                }
                else
                {
                    return Binding == typeTerm.Owner.TrueValue;
                }
            }

            private void ConstructTypeBins(Term type)
            {
                BaseCnstSymb cs;
                BaseSortSymb bs;
                bool wasAdded;
                zero = type.Owner.MkCnst(Rational.Zero, out wasAdded);
                emptyString = type.Owner.MkCnst(string.Empty, out wasAdded);

                type.Visit(
                    x => x.Symbol == x.Owner.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == x.Owner.TypeUnionSymbol)
                        {
                            return;
                        }

                        switch (x.Symbol.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                                cs = (BaseCnstSymb)x.Symbol;
                                if (cs.CnstKind == CnstKind.Numeric)
                                {
                                    AddToTypeBin(zero.Symbol, x);
                                }
                                else if (cs.CnstKind == CnstKind.String)
                                {
                                    AddToTypeBin(emptyString.Symbol, x);
                                }
                                else
                                {
                                    throw new NotImplementedException();
                                }

                                break;
                            case SymbolKind.BaseSortSymb:
                                bs = (BaseSortSymb)x.Symbol;
                                if (bs.SortKind == BaseSortKind.String)
                                {
                                    AddToTypeBin(emptyString.Symbol, x);
                                }
                                else
                                {
                                    AddToTypeBin(zero.Symbol, x);
                                }

                                break;
                            case SymbolKind.BaseOpSymb:
                                Contract.Assert(x.Symbol == x.Owner.RangeSymbol);
                                AddToTypeBin(zero.Symbol, x);
                                break;
                            case SymbolKind.UserCnstSymb:
                                AddToTypeBin(x.Symbol, x);
                                break;
                            case SymbolKind.ConSymb:
                                AddToTypeBin(((ConSymb)x.Symbol).SortSymbol, x);
                                break;
                            case SymbolKind.MapSymb:
                                AddToTypeBin(((MapSymb)x.Symbol).SortSymbol, x);
                                break;
                            case SymbolKind.UserSortSymb:
                                AddToTypeBin(x.Symbol, x);
                                break;
                            default:
                                throw new NotImplementedException();
                        }                        
                    });
            }

            private void AddToTypeBin(Symbol binLabel, Term type)
            {
                Contract.Assert(type.Groundness != Groundness.Variable);
                Term binType;
                if (!typeBins.TryFindValue(binLabel, out binType))
                {
                    typeBins.Add(binLabel, type);
                }
                else
                {
                    bool wasAdded;
                    binType = binType.Owner.MkApply(binType.Owner.TypeUnionSymbol, new Term[] { type, binType }, out wasAdded);
                    typeBins[binLabel] = binType;
                }
            }
        }

        private class EqNode : ConstraintNode
        {
            private ConstraintNode lhs;
            private ConstraintNode rhs;

            public override int NArgs
            {
                get { return 2; }
            }

            public override ConstraintNode this[int i]
            {
                get
                {
                    Contract.Assert(i == 0 || i == 1);
                    return i == 0 ? lhs : rhs;
                }
            }

            public override ConstrainNodeKind Kind
            {
                get { return ConstrainNodeKind.EqRel; }
            }

            public EqNode(Term t, ConstraintNode lhs, ConstraintNode rhs)
                : base(t)
            {
                Contract.Requires(lhs != null && rhs != null);
                this.lhs = lhs;
                this.rhs = rhs;
            }

            public override bool TryBind(Term t, int bindingLevel)
            {
                if (t != t.Owner.TrueValue)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = t;
                    BindingLevel = bindingLevel;
                }

                return true;
            }

            public override bool TryEval(SymExecuter facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;

                if (lhs.Binding != rhs.Binding)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = facts.Index.TrueValue;
                    BindingLevel = bindingLevel;
                }

                return true;
            }

            public override bool TryEval(Executer facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;

                if (lhs.Binding != rhs.Binding)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = facts.TermIndex.TrueValue;
                    BindingLevel = bindingLevel;
                }

                return true;
            }
        }

        private class NongroundNode : ConstraintNode
        {
            private ConstraintNode[] argNodes;

            public override int NArgs
            {
                get { return argNodes.Length; }
            }

            public override ConstraintNode this[int i]
            {
                get
                {
                    return argNodes[i];
                }
            }

            public override ConstrainNodeKind Kind
            {
                get { return ConstrainNodeKind.Nonground; }
            }

            /// <summary>
            /// If t is a non-ground node, then it must be provided
            /// with all the constraint nodes of its arguments.
            /// </summary>
            public NongroundNode(Term t, ConstraintNode[] argNodes)
                : base(t)
            {
                Contract.Requires(argNodes != null && t.Groundness == Groundness.Variable);
                Contract.Requires(t.Args.Length == argNodes.Length);
                this.argNodes = argNodes;
            }

            public override bool TryBind(Term t, int bindingLevel)
            {
                if (Binding != null)
                {
                    return Binding == t;
                }
                else if (t == t.Owner.FalseValue &&
                         Term.Symbol.Kind == SymbolKind.BaseOpSymb &&
                         ((BaseOpSymb)Term.Symbol).OpKind is RelKind)
                {
                    return false;
                }

                if (Term.Symbol.IsVariable)
                {
                    Contract.Assert(!IsEvaluated);
                    EvaluationLevel = bindingLevel;
                }

                Binding = t;
                BindingLevel = bindingLevel;
                return true;
            }

            public override bool TryEval(SymExecuter facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;
                Term result = null;
                if (Term.Symbol.IsDataConstructor)
                {
                    var us = (UserSymbol)Term.Symbol;
                    for (int i = 0; i < argNodes.Length; ++i)
                    {
                        // TODO: determine if type check is required here
                    }

                    var args = new Term[argNodes.Length];
                    for (int i = 0; i < argNodes.Length; ++i)
                    {
                        args[i] = argNodes[i].Binding;
                    }

                    bool wasAdded;
                    result = facts.Index.MkApply(Term.Symbol, args, out wasAdded);
                }
                else if (Term.Symbol.Kind == SymbolKind.BaseOpSymb)
                {
                    var bos = (BaseOpSymb)Term.Symbol;

                    result = bos.SymEvaluator(facts, argNodes);
                    if (result == facts.Index.FalseValue && bos.OpKind is RelKind)
                    {
                        //// Relational symbols can never be bound to false.
                        result = null;
                    }
                }

                if (result == null)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = result;
                    BindingLevel = bindingLevel;
                    return true;
                }
                else
                {
                    return result == Binding;
                }
            }

            public override bool TryEval(Executer facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;
                Term result = null;
                if (Term.Symbol.IsDataConstructor)
                {
                    var us = (UserSymbol)Term.Symbol;
                    for (int i = 0; i < argNodes.Length; ++i)
                    {
                        if (!argNodes[i].Term.Symbol.IsDataConstructor &&
                            !facts.TermIndex.IsGroundMember(us, i, argNodes[i].Binding))
                        {
                            return false;
                        }
                    }

                    var args = new Term[argNodes.Length];
                    for (int i = 0; i < argNodes.Length; ++i)
                    {
                        args[i] = argNodes[i].Binding;
                    }

                    bool wasAdded;
                    result = facts.TermIndex.MkApply(Term.Symbol, args, out wasAdded);
                }
                else if (Term.Symbol.Kind == SymbolKind.BaseOpSymb)
                {
                    var bos = (BaseOpSymb)Term.Symbol;

                    result = bos.Evaluator(facts, argNodes);
                    if (result == facts.TermIndex.FalseValue && bos.OpKind is RelKind)
                    {
                        //// Relational symbols can never be bound to false.
                        result = null;
                    }
                }

                if (result == null)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = result;
                    BindingLevel = bindingLevel;
                    return true;
                }
                else
                {
                    return result == Binding;
                }
            }
        }

        private class GroundNode : ConstraintNode
        {
            public override ConstrainNodeKind Kind
            {
                get { return ConstrainNodeKind.Ground; }
            }

            public override int NArgs
            {
                get { return 0; }
            }

            public override ConstraintNode this[int i]
            {
                get
                {
                    throw new ArgumentOutOfRangeException();
                }
            }

            public override bool TryBind(Term t, int bindingLevel)
            {
                if (Binding == null)
                {
                    if (Term.Symbol.Kind == SymbolKind.BaseOpSymb &&
                             ((BaseOpSymb)Term.Symbol).OpKind is RelKind &&
                             Binding == Term.Owner.FalseValue)
                    {
                        return false;
                    }

                    Binding = t;
                    BindingLevel = bindingLevel;
                    return true;
                }

                return Binding == t;
            }

            public override bool TryEval(SymExecuter facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;

                int i;
                Term computed;
                UserSymbol us;
                bool wasAdded;
                var success = new SuccessToken();
                var result = this.Term.Compute<Term>(
                    (x, s) => x.Args,
                    (x, ch, s) =>
                    {
                        if (x.Symbol.IsNonVarConstant)
                        {
                            return x;
                        }

                        if (x.Symbol.IsDataConstructor)
                        {
                            i = 0;
                            us = (UserSymbol)x.Symbol;
                            var args = new Term[x.Args.Length];
                            foreach (var a in ch)
                            {
                                if (!x.Args[i].Symbol.IsDataConstructor &&
                                    !x.Args[i].Symbol.IsNonVarConstant &&
                                    !x.Owner.IsGroundMember(us, i, a))
                                {
                                    s.Failed();
                                    return null;
                                }

                                args[i++] = a;
                            }

                            return x.Owner.MkApply(us, args, out wasAdded);
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                        {
                            computed = ((BaseOpSymb)x.Symbol).SymEvaluator(facts, ToBindables(x.Symbol.Arity, ch));
                            if (computed == null)
                            {
                                s.Failed();
                            }

                            return computed;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        return null;
                    },
                    success);

                Contract.Assert(!success.Result || result != null);
                if (!success.Result)
                {
                    return false;
                }
                else if (Term.Symbol.Kind == SymbolKind.BaseOpSymb &&
                         ((BaseOpSymb)Term.Symbol).OpKind is RelKind &&
                         result == Term.Owner.FalseValue)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = result;
                    BindingLevel = bindingLevel;
                    return true;
                }
                else
                {
                    return result == Binding;
                }
            }

            public override bool TryEval(Executer facts, int bindingLevel)
            {
                Contract.Assert(!IsEvaluated);
                EvaluationLevel = bindingLevel;

                int i;
                Term computed;
                UserSymbol us;
                bool wasAdded;
                var success = new SuccessToken();
                var result = Term.Compute<Term>(
                    (x, s) => x.Args,
                    (x, ch, s) =>
                    {
                        if (x.Symbol.IsNonVarConstant)
                        {
                            return x;
                        }

                        if (x.Symbol.IsDataConstructor)
                        {
                            /*
                            needCompute = false;
                            foreach (var y in x.Args)
                            {
                                if (!y.Symbol.IsDataConstructor &&
                                    !y.Symbol.IsNonVarConstant)
                                {
                                    needCompute = true;
                                    break;
                                }
                            }

                            if (!needCompute)
                            {
                                return x;
                            }
                            */

                            i = 0;
                            us = (UserSymbol)x.Symbol;
                            var args = new Term[x.Args.Length];
                            foreach (var a in ch)
                            {
                                if (!x.Args[i].Symbol.IsDataConstructor &&
                                    !x.Args[i].Symbol.IsNonVarConstant &&
                                    !x.Owner.IsGroundMember(us, i, a))
                                {
                                    s.Failed();
                                    return null;
                                }

                                args[i++] = a;
                            }

                            return x.Owner.MkApply(us, args, out wasAdded);
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                        {
                            computed = ((BaseOpSymb)x.Symbol).Evaluator(facts, ToBindables(x.Symbol.Arity, ch));
                            if (computed == null)
                            {
                                s.Failed();
                            }

                            return computed;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }                        
                    },
                    success);

                Contract.Assert(!success.Result || result != null);
                if (!success.Result)
                {
                    return false;
                }
                else if (Term.Symbol.Kind == SymbolKind.BaseOpSymb &&
                         ((BaseOpSymb)Term.Symbol).OpKind is RelKind &&
                         result == Term.Owner.FalseValue)
                {
                    return false;
                }
                else if (Binding == null)
                {
                    Binding = result;
                    BindingLevel = bindingLevel;
                    return true;
                }
                else
                {
                    return result == Binding;
                }
            }

            public GroundNode(Term t)
                : base(t)
            {
                Contract.Requires(t.Groundness == Groundness.Ground);
            }

            private static Bindable[] ToBindables(int arity, IEnumerable<Term> args)
            {
                var nodeArgs = new Bindable[arity];
                int i = 0;
                foreach (var t in args)
                {
                    nodeArgs[i++] = new Bindable(t);
                }

                return nodeArgs;
            }
        }   
    }
}
