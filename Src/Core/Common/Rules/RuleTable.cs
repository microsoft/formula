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
    using Compiler;
    using Extras;
    using Terms;

    public sealed class RuleTable
    {
        private const int SymbIndexFind = 0;
        private const int SymbIndexConj = 1;
        private const int SymbIndexConjR = 2;
        private const int SymbIndexDisj = 3;
        private const int SymbIndexProj = 4;
        private const int SymbIndexPRule = 5;
        private const int SymbIndexRule = 6;
        private const int SymbIndexCompr = 7;
        private const int SymbIndexCRule = 8;
        private const int NReificationSymbols = 9;

        private static NodePred[] IdQuery = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Id) &
                NodePredFactory.Instance.MkPredicate(ChildContextKind.Args)
            };

        private BaseOpSymb[] reifySymbs;
        private LiftedBool isValid = LiftedBool.Unknown;
        private TermIndex index;

        private int nextRuleId = 0;

        /// <summary>
        /// The set of symbolic constants that appear in any scvalue pattern of a rule.
        /// </summary>
        private Set<Term> allSymbCnsts = new Set<Term>(Term.Compare);

        /// <summary>
        /// Maps a compiler generated constructor to type arguments. Is null for the comprehension
        /// position of a comprehension head.
        /// </summary>
        private Map<UserSymbol, Term[]> symbolTypeMap = new Map<UserSymbol, Term[]>(Symbol.Compare);

        /// <summary>
        /// Maps the constructor/constant created for a partial rule back to its rule.
        /// </summary>
        private Map<UserSymbol, CoreRule> symbolMap = new Map<UserSymbol, CoreRule>(Symbol.Compare);

        /// <summary>
        /// A map from partial rules to core rules. Terms only contain constraints.
        /// </summary>
        private Map<Term, CoreRule> partialRules = new Map<Term, CoreRule>(Term.Compare);

        /// <summary>
        /// A map from user rules to core rules. Terms are only rules.
        /// </summary>
        private Map<Term, CoreRule> rules = new Map<Term, CoreRule>(Term.Compare);

        /// <summary>
        /// A map from subterm matchers back to rules. The CoreSubRule is the rule that executes the matcher.
        /// The remaining rules copy the result of matching under a sub constructor.
        /// </summary>
        private Map<SubtermMatcher, Tuple<CoreSubRule, LinkedList<CoreRule>>> subRules = 
            new Map<SubtermMatcher, Tuple<CoreSubRule, LinkedList<CoreRule>>>(SubtermMatcher.Compare);

        /// <summary>
        /// A map from a compr to a constructor used to hold comprehension. Note that
        /// these symbols do not appear in symbolMap, because they are not holding partial rules.
        /// </summary>
        private Map<Term, UserSymbol> comprMap = new Map<Term, UserSymbol>(Term.Compare);
        private Map<UserSymbol, Term> invComprMap = new Map<UserSymbol, Term>(Symbol.Compare);

        internal IEnumerable<CoreRule> Rules
        {
            get
            {
                foreach (var r in partialRules.Values)
                {
                    yield return r;
                }

                foreach (var r in rules.Values)
                {
                    yield return r;
                }

                foreach (var r in subRules.Values)
                {
                    yield return r.Item1;
                    foreach (var rp in r.Item2)
                    {
                        yield return rp;
                    }
                }
            }
        }

        internal ModuleData ModuleData
        {
            get;
            private set;
        }

        internal int StratificationDepth
        {
            get;
            private set;
        }

        internal TermIndex Index
        {
            get { return index; }
        }

        /// <summary>
        /// The set of symbolic constants used by rules in this table.
        /// </summary>
        internal IEnumerable<Term> SymbolicConstants
        {
            get { return allSymbCnsts; }
        }

        internal RuleTable(ModuleData modData, TermIndex index = null)
        {
            Contract.Requires(modData != null);
            ModuleData = modData;
            this.index = index == null ? new TermIndex(ModuleData.SymbolTable) : index;
            reifySymbs = MkRuleReificationSymbols(ModuleData.SymbolTable);

            if (modData.Reduced.Node.NodeKind == NodeKind.Transform)
            {
                RegisterTransParamTypes();
            }
        }

        private RuleTable(Transform transform, TermIndex index)
        {
            ModuleData = ((ModuleData)transform.CompilerData);
            this.index = index;
            reifySymbs = MkRuleReificationSymbols(ModuleData.SymbolTable);
        }

        /// <summary>
        /// If this is the rule table of a transformation, then clones the rule table under a new index.
        /// </summary>
        internal RuleTable CloneTransformTable(TermIndex index = null)
        {
            Contract.Requires(ModuleData.Reduced.Node.NodeKind == NodeKind.Transform);
            var clone = new RuleTable((Transform)ModuleData.Reduced.Node, index == null ? new TermIndex(ModuleData.SymbolTable) : index);
            clone.Import(this, null);
            CloneSubRules(clone);
            var result = clone.Stratify(new List<Flag>(), default(CancellationToken));
            Contract.Assert(result);
            return clone;
        }

        internal bool Compile(List<Flag> flags, CancellationToken cancel)
        {
            Contract.Assert(isValid == LiftedBool.Unknown);
            isValid = LiftedBool.True;
            var query = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Rule)
            };

            var result = true;
            ModuleData.Reduced.FindAll(
                query,
                (path, node) =>
                {
                    var actSet = new ActionSet(Factory.Instance.ToAST(node), index);
                    result = actSet.Validate(flags, cancel) && actSet.Compile(this, flags, cancel) && result;
                },
                cancel);

            foreach (var rule in ModuleData.SymbolTable.IntroducedRules)
            {
                var actSet = new ActionSet(rule, index);
                result = actSet.Validate(flags, cancel, true) && actSet.Compile(this, flags, cancel) && result;
            }
         
            if (!result || cancel.IsCancellationRequested)
            {
                return false;
            }

            if (!CompileSubRules(flags, ModuleData.SymbolTable.Root) || cancel.IsCancellationRequested)
            {
                return false;
            }

            query = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
            };
          
            ModuleData.Reduced.FindAll(query, (path, node) => Import((ModRef)node), cancel);
            if (result)
            {
                result = Stratify(flags, cancel);
            }

            //// Debug_PrintRuleTable();      

            return result && !cancel.IsCancellationRequested;
        }

        internal FindData CompilePartialRule(FindData f1, FindData f2, Set<Term> constrs, ConstraintSystem environment)
        {
            CoreRule rule;
            var body = MkPartialRuleLabel(f1.MkFindTerm(index), f2.MkFindTerm(index), constrs, environment);
            if (!partialRules.TryFindValue(body, out rule))
            {
                var vars = GetVariables(body);
                rule = new CoreRule(
                    GetNextRuleId(), 
                    MkHeadTerm(vars, MkTypeArray(vars, environment)), 
                    f1, 
                    f2, 
                    constrs,
                    IsComprehensionSymbol);
                symbolMap.Add((UserSymbol)rule.Head.Symbol, rule);
                partialRules.Add(body, rule);
            }

            return new FindData(body, rule.Head, rule.HeadType);         
        }

        internal void CompileRule(Term headTerm, Term headType, FindData[] parts, Node node, ConstraintSystem environment)
        {
            Contract.Requires(headTerm != null && headType != null && parts != null && parts.Length > 0);
            Term projBody, comprLabel, ruleTerm;
            FindData projFind;
            var headVars = GetVariables(headTerm);
            var projs = new Map<Term, FindData>(Term.Compare);
            foreach (var fd in parts)
            {
                projFind = MkProjectionRule(headVars, fd, environment, out projBody);
                if (!projs.ContainsKey(projBody))
                {
                    projs.Add(projBody, projFind);
                }
            }

            CoreRule rule;
            bool wasAdded;
            if (projs.Count == 1)
            {
                ///// head(..) :- proj_body(...).
                var proj_body = projs.First<KeyValuePair<Term, FindData>>();
                if (headTerm.Symbol.IsDataConstructor && 
                    invComprMap.TryFindValue((UserSymbol)headTerm.Symbol, out comprLabel))
                {
                    ruleTerm = index.MkApply(reifySymbs[SymbIndexCRule], new Term[] { headTerm.Args[headTerm.Args.Length - 1], comprLabel, proj_body.Key }, out wasAdded);
                }
                else
                {
                    ruleTerm = index.MkApply(reifySymbs[SymbIndexRule], new Term[] { headTerm, proj_body.Key }, out wasAdded);
                }

                if (!rules.TryFindValue(ruleTerm, out rule))
                {
                    rule = new CoreRule(
                        GetNextRuleId(), 
                        headTerm, 
                        proj_body.Value, 
                        default(FindData), 
                        new Set<Term>(Term.Compare), 
                        IsComprehensionSymbol,
                        headType, 
                        node);
                    rules.Add(ruleTerm, rule);
                }
            }
            else
            {
                Contract.Assert(projs.Count > 1);
                KeyValuePair<Term, FindData> proj_body1, proj_body2;
                Set<Term> vars = new Set<Term>(Term.Compare); 
                using (var it = projs.GetEnumerator())
                {
                    int i = 1;
                    it.MoveNext();
                    proj_body1 = it.Current;
                    GetVariables(proj_body1.Value.Pattern, vars);

                    do
                    {
                        ++i;
                        it.MoveNext();
                        proj_body2 = it.Current;
                        GetVariables(proj_body2.Value.Pattern, vars);

                        Term bodyTerm;
                        if (Term.Compare(proj_body1.Key, proj_body2.Key) > 0)
                        {
                            bodyTerm = index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { proj_body2.Key, proj_body1.Key, index.TrueValue }, out wasAdded);
                        }
                        else
                        {
                            bodyTerm = index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { proj_body1.Key, proj_body2.Key, index.TrueValue }, out wasAdded);
                        }

                        if (i < projs.Count)
                        {
                            if (!partialRules.TryFindValue(bodyTerm, out rule))
                            {
                                rule = new CoreRule(
                                    GetNextRuleId(), 
                                    MkHeadTerm(vars, MkTypeArray(vars, environment)), 
                                    proj_body1.Value, 
                                    proj_body2.Value, 
                                    new Set<Term>(Term.Compare),
                                    IsComprehensionSymbol);
                                symbolMap.Add((UserSymbol)rule.Head.Symbol, rule);
                                partialRules.Add(bodyTerm, rule);
                            }

                            proj_body1 = new KeyValuePair<Term, FindData>(bodyTerm, new FindData(bodyTerm, rule.Head, rule.HeadType));
                        }
                        else
                        {
                            if (headTerm.Symbol.IsDataConstructor &&
                                invComprMap.TryFindValue((UserSymbol)headTerm.Symbol, out comprLabel))
                            {
                                ruleTerm = index.MkApply(reifySymbs[SymbIndexCRule], new Term[] { headTerm.Args[headTerm.Args.Length - 1], comprLabel, bodyTerm }, out wasAdded);
                            }
                            else
                            {
                                ruleTerm = index.MkApply(reifySymbs[SymbIndexRule], new Term[] { headTerm, bodyTerm }, out wasAdded);
                            }

                            if (!rules.TryFindValue(ruleTerm, out rule))
                            {
                                rule = new CoreRule(
                                    GetNextRuleId(), 
                                    headTerm, 
                                    proj_body1.Value, 
                                    proj_body2.Value, 
                                    new Set<Term>(Term.Compare),
                                    IsComprehensionSymbol,
                                    headType, 
                                    node);
                                rules.Add(ruleTerm, rule);
                            }
                        }
                    } while (i < projs.Count);
                }
            }
        }

        /// <summary>
        /// If compilerCon was a compiler introduced constructor and the expected argument
        /// types are known, then returns those types.
        /// </summary>
        internal bool TryGetCompilerConTypes(UserSymbol compilerCon, out Term[] types)
        {
            return symbolTypeMap.TryFindValue(compilerCon, out types);
        }

        /// <summary>
        /// Makes a term representing a conjunction of partial rules.
        /// </summary>
        internal Term MkBodyTerm(FindData[] data)
        {
            Contract.Requires(data.Length > 0);
            var sorted = new Set<Term>(Term.Compare);
            foreach (var d in data)
            {
                Contract.Assert(d.Binding != null && d.Binding.Symbol.IsReservedOperation);
                sorted.Add(d.Binding);
            }

            bool wasAdded;
            var body = index.TrueValue;
            foreach (var t in sorted.Reverse)
            {
                body = index.MkApply(reifySymbs[SymbIndexConjR], new Term[] { t, body }, out wasAdded);
            }

            return body;
        }

        /// <summary>
        /// Makes a term representing a disjunction of bodies.
        /// </summary>
        internal Term MkBodiesTerm(IEnumerable<Term> bodyTerms)
        {
            Contract.Requires(bodyTerms != null);
            var sorted = new Set<Term>(Term.Compare, bodyTerms);
            bool wasAdded;
            var bodies = index.FalseValue;
            foreach (var t in sorted.Reverse)
            {
                bodies = index.MkApply(reifySymbs[SymbIndexDisj], new Term[] { t, bodies }, out wasAdded);
            }

            return bodies;
        }

        internal Term MkComprHead(ComprehensionData cdata, Term head)
        {
            int i;
            UserSymbol comprSymb;
            if (!comprMap.TryFindValue(cdata.Representation, out comprSymb))
            {
                comprSymb = index.MkFreshConstructor(cdata.ReadVars.Count + 1);
                comprMap.Add(cdata.Representation, comprSymb);
                invComprMap.Add(comprSymb, cdata.Representation);

                i = 0;
                Term type;
                bool result;
                var typeArgs = new Term[cdata.ReadVars.Count + 1];
                foreach (var kv in cdata.ReadVars)
                {
                    result = cdata.Owner.TryGetType(kv.Key, out type);
                    Contract.Assert(result);
                    typeArgs[i++] = type;
                }

                typeArgs[i] = null;
                symbolTypeMap.Add(comprSymb, typeArgs);
            }

            bool wasAdded;
            var args = new Term[cdata.ReadVars.Count + 1];
            i = 0;
            foreach (var kv in cdata.ReadVars)
            {
                args[i++] = kv.Key;
            }

            args[i] = head;
            return index.MkApply(comprSymb, args, out wasAdded);
        }

        internal void ProductivityCheck(Cnst prodCheckSetting, List<Flag> flags)
        {
            var settings = prodCheckSetting.GetStringValue().Split(',');
            var targets = new Map<ConSymb, Map<int, Term>>(Symbol.Compare);
            Map<int, Term> estimates;
            ConSymb target;
            int argIndex;
            int indexPos;
            string name, indexStr, cleaned;
            UserSymbol resolve, other;
            bool couldParse = true;
            foreach (var s in settings)
            {
                cleaned = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
                indexPos = s.IndexOf('[');
                if (indexPos >= 0)
                {
                    if (indexPos == 0 || indexPos == s.Length - 1)
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            prodCheckSetting,
                            Constants.BadSetting.ToString(
                                Configuration.Compiler_ProductivityCheckSetting,
                                cleaned,
                                string.Format("{0} has a bad format; use F or F[index]", cleaned)),
                           Constants.BadSetting.Code));
                        couldParse = false;
                        continue;
                    }

                    name = s.Substring(0, indexPos).Trim();
                    indexStr = s.Substring(indexPos + 1).Trim();
                    if (indexStr.Length <= 1 || 
                        indexStr[indexStr.Length - 1] != ']' ||
                        !int.TryParse(indexStr.Substring(0, indexStr.Length - 1).Trim(), out argIndex) ||
                        argIndex < 0)
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            prodCheckSetting,
                            Constants.BadSetting.ToString(
                                Configuration.Compiler_ProductivityCheckSetting,
                                cleaned,
                                string.Format("{0} has a bad format; use F or F[index]", cleaned)),
                           Constants.BadSetting.Code));
                        couldParse = false;
                        continue;
                    }
                }
                else
                {
                    name = s.Trim();
                    argIndex = -1;
                }

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                resolve = index.SymbolTable.Resolve(name, out other);
                if (resolve == null)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        prodCheckSetting,
                        Constants.BadSetting.ToString(
                            Configuration.Compiler_ProductivityCheckSetting,
                            cleaned,
                            string.Format("There is no symbol called {0}", name)),
                       Constants.BadSetting.Code));
                    couldParse = false;
                    continue;
                }
                else if (other == null && (resolve.Kind != SymbolKind.ConSymb || ((ConSymb)resolve).IsNew || resolve.IsAutoGen || resolve.Name.StartsWith(SymbolTable.ManglePrefix)))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        prodCheckSetting,
                        Constants.BadSetting.ToString(
                            Configuration.Compiler_ProductivityCheckSetting,
                            cleaned,
                            string.Format("The symbol {0} is not a derived-kind constructor", resolve.FullName)),
                       Constants.BadSetting.Code));
                    couldParse = false;
                    continue;
                }
                else if (other != null && resolve != null)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        prodCheckSetting,
                        Constants.BadSetting.ToString(
                            Configuration.Compiler_ProductivityCheckSetting,
                            cleaned,
                            string.Format("The name {0} ambiguous; could be {1} or {2}", name, resolve.FullName, other.FullName)),
                       Constants.BadSetting.Code));
                    couldParse = false;
                    continue;
                }

                target = (ConSymb)resolve;
                if (argIndex >= target.Arity)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        prodCheckSetting,
                        Constants.BadSetting.ToString(
                            Configuration.Compiler_ProductivityCheckSetting,
                            cleaned,
                            string.Format("The index {0} is too large for constructor {1}", argIndex, target.FullName)),
                       Constants.BadSetting.Code));
                    couldParse = false;
                    continue;
                }

                if (!targets.TryFindValue(target, out estimates))
                {
                    estimates = new Map<int, Term>((x, y) => x - y);
                    targets.Add(target, estimates);
                }

                if (argIndex < 0)
                {
                    for (int i = 0; i < target.Arity; ++i)
                    {
                        estimates[i] = null;
                    }
                }
                else
                {
                    estimates[argIndex] = null;
                }
            }

            if (!couldParse || targets.Count == 0)
            {
                return;
            }

            //// Collect type estimates for requested constructors.
            foreach (var kv in rules)
            {
                if (kv.Value.Head.Symbol.Kind != SymbolKind.ConSymb)
                {
                    continue;
                }

                target = (ConSymb)kv.Value.Head.Symbol;
                if (!targets.TryFindValue(target, out estimates))
                {
                    continue;
                }

                Contract.Assert(estimates != null && estimates.Count > 0);
                SplitTypeEstimates(kv.Value, target, kv.Value.HeadType, estimates, flags);
            }

            foreach (var kv in subRules)
            {
                foreach (var r in kv.Value.Item2)
                {
                    if (r.Head.Symbol.Kind != SymbolKind.ConSymb)
                    {
                        continue;
                    }

                    target = (ConSymb)r.Head.Symbol;
                    if (!targets.TryFindValue(target, out estimates))
                    {
                        continue;
                    }

                    Contract.Assert(estimates != null && estimates.Count > 0);
                    SplitTypeEstimates(r, target, r.HeadType, estimates, flags);
                }
            }

            Term argType;
            bool wasAdded;
            foreach (var kv in targets)
            {
                target = kv.Key;
                foreach (var kvp in kv.Value)
                {
                    var app = target.CanonicalForm[kvp.Key];
                    foreach (var nr in app.NonRangeMembers)
                    {
                        argType = index.MkApply(nr, TermIndex.EmptyArgs, out wasAdded);
                        CheckProduction(prodCheckSetting, target, kvp.Key, kvp.Value, argType, flags);
                    }

                    foreach (var rng in app.RangeMembers)
                    {
                        argType = index.MkApply(
                            index.RangeSymbol, 
                            new Term[] 
                            {
                                index.MkCnst(new Rational(rng.Key, System.Numerics.BigInteger.One), out wasAdded),
                                index.MkCnst(new Rational(rng.Value, System.Numerics.BigInteger.One), out wasAdded),
                            }, 
                            out wasAdded);
                        CheckProduction(prodCheckSetting, target, kvp.Key, kvp.Value, argType, flags);
                    }
                }
            }
        }

        private void CheckProduction(Node blame, ConSymb symb, int ind, Term estimate, Term expected, List<Flag> flags)
        {
            string expectedStr;
            Term intr, intrCan;
            if (!index.MkIntersection(expected, estimate, out intr))
            {
                if (expected.Symbol == index.RangeSymbol)
                {
                    expectedStr = string.Format(
                        "{{{0}..{1}}}", 
                        expected.Args[0].Symbol.PrintableName, 
                        expected.Args[1].Symbol.PrintableName);
                }
                else 
                {
                    expectedStr = expected.Symbol.PrintableName;
                }

                flags.Add(new Flag(
                    SeverityKind.Error,
                    blame,
                    Constants.ProductivityError.ToString(symb.FullName, expectedStr, ind),
                    Constants.ProductivityError.Code));
                return;
            }

            intrCan = index.MkCanonicalForm(intr);
            if (expected != intrCan)
            {
                if (expected.Symbol == index.RangeSymbol)
                {
                    expectedStr = string.Format(
                        "{{{0}..{1}}}",
                        expected.Args[0].Symbol.PrintableName,
                        expected.Args[1].Symbol.PrintableName);
                }
                else
                {
                    expectedStr = expected.Symbol.PrintableName;
                }

                flags.Add(new Flag(
                    SeverityKind.Error,
                    blame,
                    Constants.ProductivityPartialError.ToString(symb.FullName, expectedStr, ind),
                    Constants.ProductivityPartialError.Code));

                foreach (var t in intrCan.Enumerate(x => x.Symbol == index.TypeUnionSymbol ? x.Args : null))
                {
                    if (t.Symbol == index.TypeUnionSymbol)
                    {
                        continue;
                    }

                    flags.Add(new Flag(
                        SeverityKind.Error,
                        blame,
                        Constants.ProductivityPartialListError.ToString(symb.FullName, ind, t.Debug_GetSmallTermString()),
                        Constants.ProductivityPartialListError.Code));
                }
            }
        }

        private void SplitTypeEstimates(CoreRule srcRule, ConSymb estimatedSymbol, Term estimate, Map<int, Term> estimates, List<Flag> flags)
        {
            bool wasAdded;
            Contract.Assert(srcRule.Node != null);

            Term crntEstimate, thisCrntEstimate;
            var thisEstimate = new Map<int, Term>((x, y) => x - y);
            foreach (var t in estimate.Enumerate(x => x.Symbol == index.TypeUnionSymbol ? x.Args : null))
            {
                if (t.Symbol == estimatedSymbol.SortSymbol)
                {
                    foreach (var ind in estimates.Keys)
                    {
                        estimates.SetExistingKey(ind, index.GetCanonicalTerm(estimatedSymbol, ind));
                        thisEstimate[ind] = index.GetCanonicalTerm(estimatedSymbol, ind); 
                    }

                    return;
                }
                else if (t.Symbol == estimatedSymbol)
                {
                    foreach (var ind in estimates.Keys)
                    {
                        crntEstimate = estimates[ind];
                        if (crntEstimate == null)
                        {
                            estimates.SetExistingKey(ind, t.Args[ind]);
                        }
                        else
                        {
                            estimates.SetExistingKey(
                                ind,
                                index.MkApply(index.TypeUnionSymbol, new Term[] { t.Args[ind], crntEstimate }, out wasAdded));
                        }

                        if (!thisEstimate.TryFindValue(ind, out thisCrntEstimate))
                        {
                            thisEstimate.Add(ind, t.Args[ind]);
                        }
                        else
                        {
                            thisEstimate.SetExistingKey(
                                ind,
                                index.MkApply(index.TypeUnionSymbol, new Term[] { t.Args[ind], thisCrntEstimate }, out wasAdded));
                        }
                    }
                }
            }

            foreach (var kv in thisEstimate)
            {
                if (index.MkCanonicalForm(kv.Value) == index.GetCanonicalTerm(estimatedSymbol, kv.Key))
                {
                    flags.Add(new Flag(
                        SeverityKind.Warning,
                        srcRule.Node,
                        Constants.ProductivityWarning.ToString(estimatedSymbol.FullName, kv.Key),
                        Constants.ProductivityWarning.Code));
                }
            }
        }

        private bool CompileSubRules(List<Flag> flags, Namespace n)
        {
            ConSymb con;
            SubtermMatcher matcher;
            bool result = true;
            foreach (var s in n.Symbols)
            {
                if (s.Kind == SymbolKind.ConSymb)
                {
                    con = (ConSymb)s;
                    if (!con.IsSub || !con.IsSubRuleGenerated)
                    {
                        continue;
                    }

                    var patterns = new Term[con.Arity];
                    for (int i = 0; i < con.Arity; ++i)
                    {
                        patterns[i] = Index.GetCanonicalTerm(con, i);
                    }

                    matcher = Index.MkSubTermMatcher(true, patterns);
                    if (!matcher.IsSatisfiable)
                    {
                        flags.Add(
                            new Flag(
                                SeverityKind.Error,
                                s.Definitions.First().Node,
                                Constants.SubRuleUnsat.ToString(s.FullName),
                                Constants.SubRuleUnsat.Code));
                        result = false;
                    }
                    else if (!matcher.IsTriggerable)
                    {
                        flags.Add(
                            new Flag(
                                SeverityKind.Warning,
                                s.Definitions.First().Node,
                                Constants.SubRuleUntrig.ToString(s.FullName),
                                Constants.SubRuleUntrig.Code));
                    }
                    else
                    {
                        MkSubRule(con, matcher);
                    }
                }
            }

            foreach (var c in n.Children)
            {
                result = CompileSubRules(flags, c) && result;
            }

            return result;
        }

        private void Import(ModRef modRef)
        {
            //// Try to get the symbol table of the modRef.
            if (!(modRef.CompilerData is Location))
            {
                return;
            }

            var modData = ((Location)modRef.CompilerData).AST.Node.CompilerData as ModuleData;
            if (modData == null || modData.SymbolTable == null || !modData.SymbolTable.IsValid)
            {
                return;
            }
            else if (modData.FinalOutput is RuleTable)
            {
                Import((RuleTable)modData.FinalOutput, modRef.Rename);
            }
            else if (modData.FinalOutput is FactSet && ((FactSet)modData.FinalOutput).Rules != null)
            {
                Import(((FactSet)modData.FinalOutput).Rules, modRef.Rename);
            }
        }

        private void Import(RuleTable other, string renaming)
        {
            Contract.Requires(other != null);
            if (other == this)
            {
                return;
            }

            var termToFun = new Map<Term, UserSymbol>(Term.Compare);
            //// First, record all the symbols introduced by the rule table.
            foreach (var kv in comprMap)
            {
                termToFun.Add(kv.Key, kv.Value);
            }

            foreach (var kv in partialRules)
            {
                termToFun.Add(kv.Key, (UserSymbol)kv.Value.Head.Symbol);
            }

            foreach (var kv in rules)
            {
                if (kv.Key.Symbol != reifySymbs[SymbIndexRule])
                {
                    //// The heads of proper rules do not need to renamed.
                    termToFun.Add(kv.Key, (UserSymbol)kv.Value.Head.Symbol);
                }
            }

            //// Second, create names and record mapping to symbols introduced by the other table.
            Term imported;
            UserSymbol uss, ussp;
            var cache = new Map<Term, Term>(Term.Compare);
            var funToFun = new Map<UserSymbol, UserSymbol>(Symbol.Compare);

            //// The SCValue constructor is immune to renaming.
            Map<UserSymbol, UserSymbol> scValueMap = null;
            if (other.index.IsSCValueDefined)
            {
                Contract.Assert(index.IsSCValueDefined);
                funToFun.Add(other.index.SCValueSymbol, index.SCValueSymbol);
                scValueMap = new Map<UserSymbol, UserSymbol>(Symbol.Compare);
                scValueMap.Add(other.index.SCValueSymbol, index.SCValueSymbol);
            }

            foreach (var c in other.allSymbCnsts)
            {
                allSymbCnsts.Add(index.MkClone(c, renaming));
            }

            foreach (var kv in other.comprMap)
            {
                imported = ImportRuleLabel(kv.Key, cache, other.reifySymbs, renaming, scValueMap);
                if (!termToFun.TryFindValue(imported, out uss))
                {
                    uss = kv.Value.Arity == 0 ? (UserSymbol)index.MkFreshConstant(true) : index.MkFreshConstructor(kv.Value.Arity);
                    termToFun.Add(imported, uss);
                    funToFun.Add(kv.Value, uss);
                    
                    //// May directly import the comprehension symbol now.
                    comprMap.Add(imported, uss);
                    invComprMap.Add(uss, imported);
                }
                else
                {
                    Contract.Assert(kv.Value.Arity == uss.Arity);
                    funToFun.Add(kv.Value, uss);
                }
            }

            foreach (var kv in other.partialRules)
            {
                imported = ImportRuleLabel(kv.Key, cache, other.reifySymbs, renaming, scValueMap);
                ussp = (UserSymbol)kv.Value.Head.Symbol;
                if (!termToFun.TryFindValue(imported, out uss))
                {
                    uss = ussp.Arity == 0 ? (UserSymbol)index.MkFreshConstant(true) : index.MkFreshConstructor(ussp.Arity);
                    termToFun.Add(imported, uss);
                    funToFun.Add(ussp, uss);
                }
                else
                {
                    Contract.Assert(ussp.Arity == uss.Arity);
                    funToFun.Add(ussp, uss);
                }
            }

            //// Third, reconstruct rules as needed.
            foreach (var kv in other.partialRules)
            {
                imported = ImportRuleLabel(kv.Key, cache, other.reifySymbs, renaming, scValueMap);
                if (!partialRules.ContainsKey(imported))
                {
                    partialRules.Add(imported, kv.Value.Clone(GetNextRuleId(), IsComprehensionSymbol, index, cache, funToFun, renaming));
                }
            }

            foreach (var kv in other.rules)
            {
                imported = ImportRuleLabel(kv.Key, cache, other.reifySymbs, renaming, scValueMap);
                if (!rules.ContainsKey(imported))
                {
                    rules.Add(imported, kv.Value.Clone(GetNextRuleId(), IsComprehensionSymbol, index, cache, funToFun, renaming));
                }
            }
        }
      
        internal void Debug_PrintRuleTable()
        {
            Console.WriteLine("-----------------------------------------------------");

            Console.WriteLine("** Partial rules");
            foreach (var kv in partialRules)
            {
                //// Console.WriteLine("Rule body: {0}", kv.Key.Debug_GetSmallTermString());
                kv.Value.Debug_PrintRule();
            }

            Console.WriteLine("** Sub rules");
            foreach (var kv in subRules)
            {
                //// Console.WriteLine("Rule body: {0}", kv.Key.Debug_GetSmallTermString());
                kv.Value.Item1.Debug_PrintRule();
                foreach (var rp in kv.Value.Item2)
                {
                    rp.Debug_PrintRule();
                }
            }

            Console.WriteLine("** Rules");
            foreach (var kv in rules)
            {
                //// Console.WriteLine("Rule body: {0}", kv.Key.Debug_GetSmallTermString());
                kv.Value.Debug_PrintRule();
            }
        }

        private bool IsComprehensionSymbol(Symbol s)
        {
            if (s.Kind != SymbolKind.ConSymb)
            {
                return false;
            }

            return invComprMap.ContainsKey((UserSymbol)s);
        }

        private bool IsComprehensionSymbol(Symbol s, out Term comprLabel)
        {
            if (s.Kind != SymbolKind.ConSymb)
            {
                comprLabel = null;
                return false;
            }

            return invComprMap.TryFindValue((UserSymbol)s, out comprLabel);
        }

        private void RegisterTransParamTypes()
        {
            var trans = (Transform)ModuleData.Reduced.Node;
            bool wasAdded;
            UserSymbol symb, other;
            Term smbCnst, smbCnstType;
            foreach (var p in trans.Inputs)
            {
                if (p.Type.NodeKind == NodeKind.ModRef)
                {
                    continue;
                }

                symb = index.SymbolTable.Resolve("%" + p.Name, out other, index.SymbolTable.ModuleSpace);
                Contract.Assert(symb != null && other == null);

                smbCnst = index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);

                symb = index.SymbolTable.Resolve("%" + p.Name + "~Type", out other, index.SymbolTable.ModuleSpace);
                Contract.Assert(symb != null && other == null);
                smbCnstType = index.GetCanonicalTerm(symb, 0);

                index.RegisterSymbCnstType(smbCnst, smbCnstType);
            }
        }

        private static BaseOpSymb[] MkRuleReificationSymbols(SymbolTable table)
        {
            var symbols = new BaseOpSymb[NReificationSymbols];
            symbols[SymbIndexFind] = table.GetOpSymbol(ReservedOpKind.Find);
            symbols[SymbIndexConj] = table.GetOpSymbol(ReservedOpKind.Conj);
            symbols[SymbIndexConjR] = table.GetOpSymbol(ReservedOpKind.ConjR);
            symbols[SymbIndexProj] = table.GetOpSymbol(ReservedOpKind.Proj);
            symbols[SymbIndexPRule] = table.GetOpSymbol(ReservedOpKind.PRule);
            symbols[SymbIndexRule] = table.GetOpSymbol(ReservedOpKind.Rule);
            symbols[SymbIndexDisj] = table.GetOpSymbol(ReservedOpKind.Disj);
            symbols[SymbIndexCompr] = table.GetOpSymbol(ReservedOpKind.Compr);
            symbols[SymbIndexCRule] = table.GetOpSymbol(ReservedOpKind.CRule);

            return symbols;
        }

        /// <summary>
        /// Clones a rule label from a foreign rule table.
        /// The term should label a partial rule, projection, or comprehension symbol.
        /// </summary>
        private Term ImportRuleLabel(
            Term t, 
            Map<Term, Term> cache, 
            BaseOpSymb[] reifyForgn, 
            string renaming, 
            Map<UserSymbol, UserSymbol> symbMap)
        {
            if (t.Owner == index)
            {
                return t;
            }
        
            bool wasAdded;
            Term find1, find2, body, imported;
            return t.Compute<Term>(
                (x, s) => 
                {
                    if (x.Symbol == reifyForgn[SymbIndexPRule] ||
                        x.Symbol == reifyForgn[SymbIndexFind] ||
                        x.Symbol == reifyForgn[SymbIndexProj] ||
                        x.Symbol == reifyForgn[SymbIndexRule] ||
                        x.Symbol == reifyForgn[SymbIndexCompr] ||
                        x.Symbol == reifyForgn[SymbIndexCRule])
                    {
                        return !cache.ContainsKey(x) ? x.Args : null;
                    }

                    return null;
                },
                (x, ch, s) =>
                {
                    if (cache.TryFindValue(x, out imported))
                    {
                        return imported;
                    }
                   
                    if (x.Symbol == reifyForgn[SymbIndexConj])
                    {
                        imported = CloneAndSort(x, (ReservedOpKind)reifyForgn[SymbIndexConj].OpKind, index.TrueValue, renaming, symbMap);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexConjR])
                    {
                        imported = CloneAndSort(x, (ReservedOpKind)reifyForgn[SymbIndexConjR].OpKind, index.TrueValue, renaming, symbMap);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexDisj])
                    {
                        imported = CloneAndSort(x, (ReservedOpKind)reifyForgn[SymbIndexDisj].OpKind, index.FalseValue, renaming, symbMap);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexPRule])
                    {
                        using (var it = ch.GetEnumerator())
                        {
                            it.MoveNext();
                            find1 = it.Current;
                            it.MoveNext();
                            find2 = it.Current;
                            it.MoveNext();
                            body = it.Current;
                        }

                        if (Term.Compare(find1, find2) > 0)
                        {
                            imported = index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { find2, find1, body }, out wasAdded);
                        }
                        else
                        {
                            imported = index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { find1, find2, body }, out wasAdded);
                        }
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexFind])
                    {
                        imported = index.MkApply(reifySymbs[SymbIndexFind], ToArray(ch, reifySymbs[SymbIndexFind].Arity), out wasAdded);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexProj])
                    {
                        imported = index.MkApply(reifySymbs[SymbIndexProj], ToArray(ch, reifySymbs[SymbIndexProj].Arity), out wasAdded);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexRule])
                    {
                        imported = index.MkApply(reifySymbs[SymbIndexRule], ToArray(ch, reifySymbs[SymbIndexRule].Arity), out wasAdded);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexCRule])
                    {
                        imported = index.MkApply(reifySymbs[SymbIndexCRule], ToArray(ch, reifySymbs[SymbIndexCRule].Arity), out wasAdded);
                    }
                    else if (x.Symbol == reifyForgn[SymbIndexCompr])
                    {
                        imported = index.MkApply(reifySymbs[SymbIndexCompr], ToArray(ch, reifySymbs[SymbIndexCompr].Arity), out wasAdded);
                    }
                    else
                    {
                        imported = index.MkClone(x, renaming, symbMap);
                    }

                    cache.Add(x, imported);
                    return imported;
                });
        }

        /// <summary>
        /// Treat t as a tree of binary op-applications where leaves are non-op-applications.
        /// Then clones and sorts the leaves to produce a term in this index of the form:
        /// op(x_1, op(x_2, .... op(x_n, terminator)...)). If Clone(t) == terminator, then only returns
        /// terminator.
        /// </summary>
        private Term CloneAndSort(Term t, ReservedOpKind op, Term terminator, string renaming, Map<UserSymbol, UserSymbol> symbMap)
        {
            Contract.Requires(t != null && terminator != null);
            Contract.Requires(terminator.Owner == index);

            if (t.Owner == index)
            {
                return t;
            }

            var sForgn = t.Owner.SymbolTable.GetOpSymbol(op);
            var sLocal = index.SymbolTable.GetOpSymbol(op);
            var endForgn = t.Owner.MkClone(terminator, renaming, symbMap);
            Contract.Assert(sForgn.Arity == 2);

            Term sorted;
            int endCount = 0;
            Set<Term> leaves = new Set<Term>(Term.Compare);
            t.Visit(
                x => x.Symbol == sForgn ? x.Args : null,
                x =>
                {
                    if (x.Symbol != sForgn)
                    {
                        if (endCount < 2 && x == endForgn)
                        {
                            ++endCount;
                        }

                        leaves.Add(index.MkClone(x, renaming, symbMap));
                    }
                });

            bool wasAdded;
            sorted = terminator;
            if (endCount == 1)
            {
                //// Remove the terminator symbol from the set only if it
                //// appeared once.
                leaves.Remove(terminator);
            }

            foreach (var l in leaves.Reverse)
            {
                sorted = index.MkApply(sLocal, new Term[] { l, sorted }, out wasAdded);
            }

            return sorted;
        }

        private Term[] ToArray(IEnumerable<Term> args, int arity)
        {
            if (arity == 0)
            {
                return TermIndex.EmptyArgs;
            }

            int i = 0;
            var argsArr = new Term[arity];
            foreach (var a in args)
            {
                argsArr[i++] = a;
            }

            return argsArr;
        }

        IEnumerable<Tuple<CoreRule, int>> GetReaders(Map<Symbol, Set<Tuple<CoreRule, int>>> finds, Symbol s)
        {
            Set<Tuple<CoreRule, int>> readers;
            if (finds.TryFindValue(s, out readers))
            {
                foreach (var r in readers)
                {
                    yield return r;
                }
            }

            if (s.Kind == SymbolKind.UserSortSymb)
            {
                if (finds.TryFindValue(((UserSortSymb)s).DataSymbol, out readers))
                {
                    foreach (var r in readers)
                    {
                        yield return r;
                    }
                }
            }
            else if (s.Kind == SymbolKind.ConSymb)
            {
                var con = (ConSymb)s;
                if (con.SortSymbol != null &&  finds.TryFindValue(con.SortSymbol, out readers))
                {
                    foreach (var r in readers)
                    {
                        yield return r;
                    }
                }
            }
            else if (s.Kind == SymbolKind.MapSymb)
            {
                var map = (MapSymb)s;
                if (map.SortSymbol != null && finds.TryFindValue(map.SortSymbol, out readers))
                {
                    foreach (var r in readers)
                    {
                        yield return r;
                    }
                }
            }
        }

        /// <summary>
        /// TODO: Add cancellation
        /// </summary>
        /// <param name="cancel"></param>
        /// <returns></returns>
        private bool Stratify(List<Flag> flags, CancellationToken cancel)
        {
            //// Maps a comprehension symbol to all the rules reading this compr.
            var comprs = new Map<Symbol, Set<CoreRule>>(Symbol.Compare);

            //// Maps a non-comprehension symbol to all the rules with a find reading the symbol.
            //// The first find has index 0 and the second has index 1.
            var finds = new Map<Symbol, Set<Tuple<CoreRule, int>>>(Symbol.Compare);

            //// Comprehension dependecies have the rule of "true".
            var deps = new DependencyCollection<CoreRule, bool>(CoreRule.Compare, LiftedBool.CompareBools);

            //// First bin all the rules according to their read dependencies.
            //// This cuts down on the number of dependency candidates that need to be checked.
            Set<CoreRule> comprReaders;
            foreach (var r in Rules)
            {
                foreach (var c in r.ComprehensionSymbols)
                {
                    if (!comprs.TryFindValue(c, out comprReaders))
                    {
                        comprReaders = new Set<CoreRule>(CoreRule.Compare);
                        comprs.Add(c, comprReaders);
                    }

                    comprReaders.Add(r);
                }

                AddReads(r, 0, finds);
                AddReads(r, 1, finds);
            }

            //// Second, create dependency graph.
            UserSymbol us;
            foreach (var r in Rules)
            {
                if (r.Head.Symbol.IsDataConstructor || r.Head.Symbol.IsDerivedConstant)
                {
                    us = (UserSymbol)r.Head.Symbol;
                    if (invComprMap.ContainsKey(us))
                    {
                        if (comprs.TryFindValue(us, out comprReaders))
                        {
                            foreach (var rp in comprReaders)
                            {
                                deps.Add(r, rp, true);
                            }
                        }
                    }
                    else 
                    {
                        foreach (var kv in GetReaders(finds, us))
                        {
                            if (kv.Item2 == 0 && Unifier.IsUnifiable(r.Head, kv.Item1.Find1.Pattern))
                            {
                                deps.Add(r, kv.Item1, false);
                            }
                            else if (kv.Item2 == 1 && Unifier.IsUnifiable(r.Head, kv.Item1.Find2.Pattern))
                            {
                                deps.Add(r, kv.Item1, false);
                            }
                        }
                    }
                }
                else
                {
                    //// Otherwise the head of the rule is a term involving selectors and relabels.
                    //// Need to use its type to look up possible dependencies.
                    r.HeadType.Visit(
                        x => x.Symbol == x.Owner.TypeUnionSymbol ? x.Args : null,
                        x =>
                        {
                            if (x.Symbol == x.Owner.TypeUnionSymbol)
                            {
                                return;
                            }

                            foreach (var kv in GetReaders(finds, x.Symbol))
                            {
                                if (kv.Item2 == 0 && Unifier.IsUnifiable(r.Head, kv.Item1.Find1.Pattern))
                                {
                                    deps.Add(r, kv.Item1, false);
                                }
                                else if (kv.Item2 == 1 && Unifier.IsUnifiable(r.Head, kv.Item1.Find2.Pattern))
                                {
                                    deps.Add(r, kv.Item1, false);
                                }
                            }
                        });
                }
            }

            //// Third, check for dependency cycles that are not stratified.
            //// Debug_PrintRuleTable();
            //// deps.Debug_PrintCollection(x => x.RuleId.ToString(), x => x ? "STRAT" : "", true);

            int stratum;
            int ndeps;
            int cycleNum = 0;
            bool isStratified = true;
            var sort = deps.GetTopologicalSort(out ndeps, cancel);
            StratificationDepth = 0;
            foreach (var n in sort)
            {
                stratum = 0;
                foreach (var e in n.Requests)
                {
                    if (e.Role)
                    {
                        stratum = Math.Max(stratum, e.Target.Resource.Stratum + 1);
                    }
                    else
                    {
                        stratum = Math.Max(stratum, e.Target.Resource.Stratum);
                    }
                }

                StratificationDepth = Math.Max(StratificationDepth, stratum);
                if (n.Kind == DependencyNodeKind.Normal)
                {
                    n.Resource.Stratum = stratum;
                    continue;
                }

                foreach (var m in n.InternalNodes)
                {
                    m.Resource.Stratum = stratum;
                }

                foreach (var e in n.InternalEnds)
                {
                    if (e.Role)
                    {
                        isStratified = true;
                        foreach (var m in n.InternalNodes)
                        {
                            if (m.Resource.Node != null)
                            {
                                if (isStratified)
                                {
                                    isStratified = false;
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        m.Resource.Node,
                                        Constants.StratificationError.ToString(string.Format("Listing dependency cycle {0}...", cycleNum)),
                                        Constants.StratificationError.Code));
                                }

                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    m.Resource.Node,
                                    Constants.StratificationError.ToString("Dependency cycle " + cycleNum),
                                    Constants.StratificationError.Code));
                            }
                        }

                        ++cycleNum;
                        //// Should have encountered some rule to blame.
                        Contract.Assert(!isStratified);
                        break;
                    }
                }
            }

            ++StratificationDepth;
            return isStratified;
        }

        private void AddReads(
            CoreRule r, 
            int findIndex, 
            Map<Symbol, Set<Tuple<CoreRule, int>>> reads)
        {
            FindData d;
            switch (findIndex)
            {
                case 0:
                    d = r.Find1;
                    break;
                case 1:
                    d = r.Find2;
                    break;
                default:
                    throw new NotImplementedException();            
            }

            if (d.IsNull)
            {
                return;
            }

            Set<Tuple<CoreRule, int>> readers;
            //// In this case, there is a more specific pattern to represent the read
            if (!d.Pattern.Symbol.IsVariable)
            {
                if (!reads.TryFindValue(d.Pattern.Symbol, out readers))
                {
                    readers = new Set<Tuple<CoreRule, int>>(Compare);
                    reads.Add(d.Pattern.Symbol, readers);
                }

                readers.Add(new Tuple<CoreRule, int>(r, findIndex));
                if (index.IsSCValueDefined && d.Pattern.Symbol == index.SCValueSymbol)
                {
                    Contract.Assert(d.Pattern.Args[0].Symbol.Kind == SymbolKind.UserCnstSymb &&
                                    ((UserCnstSymb)d.Pattern.Args[0].Symbol).IsSymbolicConstant);
                    allSymbCnsts.Add(d.Pattern.Args[0]);
                }
            }
            else
            {
                Symbol findSymbol;
                //// Otherwise, need to use the type for the dependency
                d.Type.Visit(
                    x => x.Symbol == x.Owner.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == x.Owner.TypeUnionSymbol)
                        {
                            return;
                        }

                        if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                        {
                            findSymbol = ((UserSortSymb)x.Symbol).DataSymbol;
                        }
                        else
                        {
                            findSymbol = x.Symbol;
                            Contract.Assert(findSymbol.IsDataConstructor || findSymbol.IsNonVarConstant);
                        }

                        if (!reads.TryFindValue(findSymbol, out readers))
                        {
                            readers = new Set<Tuple<CoreRule, int>>(Compare);
                            reads.Add(findSymbol, readers);
                        }

                        readers.Add(new Tuple<CoreRule, int>(r, findIndex));
                    });
            }
        }

        private static int Compare(Tuple<CoreRule, int> find1, Tuple<CoreRule, int> find2)
        {
            var cmp = CoreRule.Compare(find1.Item1, find2.Item1);
            if (cmp != 0)
            {
                return cmp;
            }

            return find1.Item2 - find2.Item2;
        }

        private int GetNextRuleId()
        {
            return nextRuleId++;
        }

        private Set<Term> GetVariables(Term t)
        {
            var vars = new Set<Term>(Term.Compare);
            t.Visit(
                x => 
                {
                    if (x.Groundness != Groundness.Variable)
                    {
                        return null;
                    }
                    else if (x.Symbol == reifySymbs[SymbIndexCompr])
                    {
                        return Extras.EnumerableMethods.GetEnumerable<Term>(x.Args[1]);
                    }
                    else 
                    {
                        return x.Args;
                    }
                },
                x =>
                {
                    if (x.Symbol.IsVariable)
                    {
                        vars.Add(x);
                    }
                });

            return vars;
        }

        private void GetVariables(Term t, Set<Term> vars)
        {
            t.Visit(
                x => x.Groundness == Groundness.Variable ? x.Args : null,
                x =>
                {
                    if (x.Symbol.IsVariable)
                    {
                        vars.Add(x);
                    }
                });
        }

        /// <summary>
        /// Given the body of a rule, generates a new constructor F(x_1,...,x_n) where
        /// x_1 ... x_n are all of the non-internal variables appearing in the body.
        /// If the body has no variables, then a fresh derived constant is returned instead.
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        private Term MkHeadTerm(Set<Term> vars, Term[] argTypes)
        {
            bool wasAdded;
            if (vars.Count == 0)
            {
                return index.MkApply(index.MkFreshConstant(true), TermIndex.EmptyArgs, out wasAdded);
            }
            else
            {
                var headCon = index.MkFreshConstructor(vars.Count);
                symbolTypeMap.Add(headCon, argTypes);
                return index.MkApply(headCon, vars.ToArray(), out wasAdded);
            }
        }

        /// <summary>
        /// Adds sub rules to a direct clone of a transform table.
        /// </summary>
        /// <param name="clone"></param>
        private void CloneSubRules(RuleTable clone)
        {
            //// In this case need to clone subrules.
            bool wasAdded;
            var symbTrans = new Map<UserSymbol, UserSymbol>(Symbol.Compare);
            foreach (var tup in subRules.Values)
            {
                var subRule = tup.Item1;
                var depRules = tup.Item2;
                var clonedMatcherSymb = clone.index.MkFreshConstructor(subRule.Head.Symbol.Arity);

                symbTrans.Add((UserSymbol)subRule.Head.Symbol, clonedMatcherSymb);
                var clonedSubRule = (CoreSubRule)subRule.Clone(GetNextRuleId(), null, clone.Index, null, symbTrans, null);
                var depClones = new LinkedList<CoreRule>();
                clone.subRules.Add(clonedSubRule.Matcher, new Tuple<CoreSubRule, LinkedList<CoreRule>>(clonedSubRule, depClones));
                foreach (var depRule in depRules)
                {
                    //// Next make a rule of the form symb(x1,...,xn) :- matcher(x1,...,xn).
                    var clonedCopyHead = clone.index.MkClone(depRule.Head);
                    for (int i = 0; i < clonedCopyHead.Symbol.Arity; ++i)
                    {
                        if (clonedCopyHead.Args[i] != clonedSubRule.Head.Args[i])
                        {
                            throw new Exception("Bad clone");
                        }
                    }

                    var clonedCopyRule = new CoreRule(
                        GetNextRuleId(),
                        clonedCopyHead,
                        new FindData(
                            //// Non-descriptive reification, because this rule will not be cloned but reification symbol expected.
                            clone.index.MkApply(clone.reifySymbs[SymbIndexRule], new Term[] { clone.index.FalseValue, clone.index.FalseValue }, out wasAdded),
                            clonedSubRule.Head,
                            clone.index.FalseValue),
                        default(FindData),
                        TermIndex.EmptyArgs,
                        x => false,
                        clone.index.MkApply(((ConSymb)(clonedCopyHead.Symbol)).SortSymbol, TermIndex.EmptyArgs, out wasAdded),
                        ((ConSymb)(clonedCopyHead.Symbol)).Definitions.First().Node);
                    depClones.AddLast(clonedCopyRule);
                }
            }
        }

        private void MkSubRule(ConSymb symb, SubtermMatcher matcher)
        {
            Contract.Requires(matcher != null && matcher.IsTriggerable);

            bool wasAdded;
            Tuple<CoreSubRule, LinkedList<CoreRule>> matcherRules;
            if (!subRules.TryFindValue(matcher, out matcherRules))
            {
                var bindVar = Index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "dc", 0), true, out wasAdded);
                var headVars = new Term[matcher.NPatterns];
                for (int i = 0; i < matcher.NPatterns; ++i)
                {
                    headVars[i] = Index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "dc", i + 1), true, out wasAdded);
                }

                var headCon = index.MkFreshConstructor(matcher.NPatterns);
                var head = index.MkApply(headCon, headVars, out wasAdded);
                var subRule = new CoreSubRule(GetNextRuleId(), head, bindVar, matcher);
                matcherRules = new Tuple<CoreSubRule, LinkedList<CoreRule>>(subRule, new LinkedList<CoreRule>());
                subRules.Add(matcher, matcherRules);
            }

            //// Next make a rule of the form symb(x1,...,xn) :- matcher(x1,...,xn).
            var copyHeadArgs = new Term[symb.Arity];
            var subRuleHead = matcherRules.Item1.Head;
            for (int i = 0; i < symb.Arity; ++i)
            {
                copyHeadArgs[i] = subRuleHead.Args[i];
            }

            var copyRule = new CoreRule(
                GetNextRuleId(),
                Index.MkApply(symb, copyHeadArgs, out wasAdded),
                new FindData(
                    //// Non-descriptive reification, because this rule will not be cloned but reification symbol expected.
                    Index.MkApply(reifySymbs[SymbIndexRule], new Term[] { Index.FalseValue, Index.FalseValue }, out wasAdded), 
                    subRuleHead, 
                    Index.FalseValue),
                default(FindData),
                TermIndex.EmptyArgs,
                x => false,
                matcher.MkTypeTerm(symb),
                symb.Definitions.First().Node);

            matcherRules.Item2.AddLast(copyRule);
        }

        private FindData MkProjectionRule(Set<Term> headVars, FindData part, ConstraintSystem environment, out Term body)
        {
            Contract.Requires(part.Binding != null && part.Binding.Symbol == reifySymbs[SymbIndexPRule]);
            var vars = GetVariables(part.Binding);
            if (vars.Count == headVars.Count)
            {
                body = part.Binding;
                return part;
            }

            bool wasAdded;
            vars.IntersectWith(headVars);
            var projTerm = index.TrueValue;
            foreach (var v in vars.Reverse)
            {
                projTerm = index.MkApply(reifySymbs[SymbIndexConj], new Term[] { v, projTerm }, out wasAdded);
            }

            projTerm = index.MkApply(reifySymbs[SymbIndexProj], new Term[] { part.Binding, projTerm }, out wasAdded);
            CoreRule rule;
            if (!partialRules.TryFindValue(projTerm, out rule))
            {
                rule = new CoreRule(
                    GetNextRuleId(), 
                    MkHeadTerm(vars, MkTypeArray(vars, environment)), 
                    part, 
                    default(FindData), 
                    new Set<Term>(Term.Compare),
                    IsComprehensionSymbol);
                symbolMap.Add((UserSymbol)rule.Head.Symbol, rule);
                partialRules.Add(projTerm, rule);
            }

            body = projTerm;
            return new FindData(projTerm, rule.Head, rule.HeadType);
        }

        private static Term[] MkTypeArray(Set<Term> vars, ConstraintSystem environment)
        {
            Contract.Requires(vars != null && environment != null);
            if (vars.Count == 0)
            {
                return null;
            }

            var i = 0;
            Term type;
            bool result;
            var types = new Term[vars.Count];
            foreach (var v in vars)
            {
                result = environment.TryGetType(v, out type);
                Contract.Assert(result);
                types[i++] = type;
            }

            return types;
        }

        private Term MkPartialRuleLabel(Term find1, Term find2, Set<Term> constrs, ConstraintSystem environment)
        {
            int i;
            bool wasAdded;
            bool hasCompr;
            bool isChanged;
            Term rewritten;
            Term comprLabel;
            Term body = index.TrueValue;
            foreach (var t in constrs.Reverse)
            {
                hasCompr = false;
                foreach (var tp in t.Enumerate(x => x.Args))
                {
                    if (IsComprehensionSymbol(tp.Symbol))
                    {
                        hasCompr = true;
                        break;
                    }
                }

                if (hasCompr)
                {
                    rewritten = t.Compute<Term>(
                        (x, s) => x.Args,
                        (x, ch, s) =>
                        {
                            if (IsComprehensionSymbol(x.Symbol, out comprLabel))
                            {
                                return comprLabel;
                            }

                            isChanged = false;
                            i = 0;
                            foreach (var y in ch)
                            {
                                if (y != x.Args[i++])
                                {
                                    isChanged = true;
                                    break;
                                }
                            }

                            if (isChanged)
                            {
                                return index.MkApply(x.Symbol, ToArray(ch, x.Symbol.Arity), out wasAdded);
                            }
                            else
                            {
                                return x;
                            }
                        });

                    body = index.MkApply(reifySymbs[SymbIndexConj], new Term[] { rewritten, body }, out wasAdded);                
                }
                else
                {
                    body = index.MkApply(reifySymbs[SymbIndexConj], new Term[] { t, body }, out wasAdded);                
                }               
            }

            if (Term.Compare(find1, find2) > 0)
            {
                return index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { find2, find1, body }, out wasAdded);
            }
            else
            {
                return index.MkApply(reifySymbs[SymbIndexPRule], new Term[] { find1, find2, body }, out wasAdded);
            }
        }
    }
}
