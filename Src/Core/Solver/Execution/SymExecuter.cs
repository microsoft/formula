namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Rules;
    using Common.Terms;
    using Compiler;

    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using System.Numerics;
    using System.Text.RegularExpressions;

    internal class SymExecuter
    {
        private static char PatternVarBoundPrefix = '^';
        private static char PatternVarUnboundPrefix = '*';

        /// <summary>
        /// Facts where symbolic constants have been replaced by variables
        /// </summary>
        private Set<Term> varFacts;

        /// <summary>
        /// Map from an alias to variablized fact.
        /// </summary>
        private Map<UserCnstSymb, Term> aliasMap;

        /// <summary>
        /// Maps a type term to a set of patterns that cover the triggering of that type.
        /// </summary>
        private Map<Term, Set<Term>> typesToTriggersMap = new Map<Term, Set<Term>>(Term.Compare);

        /// <summary>
        /// Maps a find pattern to a subindex.
        /// </summary>
        private Map<Term, SymSubIndex> trigIndices = new Map<Term, SymSubIndex>(Term.Compare);

        /// <summary>
        /// Maps a comprehension symbol to a subindex.
        /// </summary>
        private Map<Symbol, SymSubIndex> comprIndices = new Map<Symbol, SymSubIndex>(Symbol.Compare);

        /// <summary>
        /// Maps a symbol to a set of indices with patterns beginning with this symbol. 
        /// </summary>
        private Map<Symbol, LinkedList<SymSubIndex>> symbToIndexMap = new Map<Symbol, LinkedList<SymSubIndex>>(Symbol.Compare);

        /// <summary>
        /// A map from strata to rules that are not triggered.
        /// </summary>
        private Map<int, LinkedList<CoreRule>> untrigRules =
            new Map<int, LinkedList<CoreRule>>((x, y) => x - y);

        /// <summary>
        /// The current least fixed point.
        /// </summary>
        private Map<Term, SymElement> lfp = 
            new Map<Term, SymElement>(Term.Compare);

        private Map<Term, Set<Derivation>> facts = new Map<Term, Set<Derivation>>(Term.Compare);

        private List<Z3BoolExpr> pendingConstraints =
            new List<Z3BoolExpr>();

        private Dictionary<int, Z3BoolExpr> recursionConstraints =
            new Dictionary<int, Z3BoolExpr>();

        private Dictionary<int, int> ruleCycles =
            new Dictionary<int, int>();

        public RuleTable Rules
        {
            get;
            private set;
        }

        public Solver Solver
        {
            get;
            private set;
        }

        public bool KeepDerivations
        {
            get;
            private set;
        }

        public TermIndex Index
        {
            get;
            private set;
        }

        public TermEncIndex Encoder
        {
            get;
            private set;
        }

        public Map<Term, Term> varToTypeMap =
            new Map<Term, Term>(Term.Compare);

        private Set<Term> PositiveConstraintTerms = new Set<Term>(Term.Compare);
        private Set<Term> NegativeConstraintTerms = new Set<Term>(Term.Compare);

        private Map<Term, List<Term>> symCountMap =
            new Map<Term, List<Term>>(Term.Compare);

        public int GetSymbolicCountIndex(Term t)
        {
            List<Term> terms;
            if (!symCountMap.TryFindValue(t, out terms))
            {
                terms = new List<Term>();
                symCountMap.Add(t, terms);
            }

            return terms.Count;
        }

        public Term GetSymbolicCountTerm(Term t, int index)
        {
            return symCountMap[t].ElementAt(index);
        }

        public void AddSymbolicCountTerm(Term x, Term y)
        {
            List<Term> terms;
            if (!symCountMap.TryFindValue(x, out terms))
            {
                terms = new List<Term>();
                symCountMap.Add(x, terms);
            }

            terms.Add(y);
        }

        public void AddPositiveConstraint(Term t)
        {
            SymElement e;
            if (lfp.TryFindValue(t, out e))
            {
                if (e.HasConstraints())
                {
                    PositiveConstraintTerms.Add(t);
                }
            }
        }

        public bool Exists(Term t)
        {
            return lfp.ContainsKey(t);
        }

        public bool IfExistsThenDerive(Term t, Derivation d)
        {
            Contract.Requires(t != null);

            if (!KeepDerivations)
            {
                return lfp.ContainsKey(t);
            }

            Set<Derivation> dervs;
            if (!facts.TryFindValue(t, out dervs))
            {
                return false;
            }

            dervs.Add(d);
            return true;
        }

        public void PendConstraint(Z3BoolExpr expr)
        {
            pendingConstraints.Add(expr);
        }

        public bool HasSideConstraint(Term term)
        {
            SymElement e;
            if (lfp.TryFindValue(term, out e))
            {
                if (e.HasConstraints())
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasSideConstraints(IEnumerable<Term> terms)
        {
            foreach (var term in terms)
            {
                SymElement e;
                if (lfp.TryFindValue(term, out e))
                {
                    if (e.HasConstraints())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public Z3BoolExpr GetSideConstraints(Term t)
        {
            SymElement e;
            if (lfp.TryFindValue(t, out e))
            {
                if (e.HasConstraints())
                {
                    return e.GetSideConstraints(this);
                }
            }

            return Solver.Context.MkTrue(); // if no constraints
        }

        public bool AddNegativeConstraint(Term t)
        {
            SymElement e;
            if (lfp.TryFindValue(t, out e))
            {
                if (e.HasConstraints())
                {
                    NegativeConstraintTerms.Add(t);
                    return true;
                }
            }

            return false;
        }

        public bool GetSymbolicTerm(Term t, out SymElement e)
        {
            return lfp.TryFindValue(t, out e);
        }

        public void PendEqualityConstraint(Term t1, Term t2)
        {
            Term normalized;
            var expr1 = this.Encoder.GetTerm(t1, out normalized);
            var expr2 = this.Encoder.GetTerm(t2, out normalized);
            this.PendEqualityConstraint(expr1, expr2);
        }

        public void PendEqualityConstraint(Z3Expr expr1, Z3Expr expr2)
        {
            pendingConstraints.Add(Solver.Context.MkEq(expr1, expr2));
        }

        private void AddRecursionConstraint(int ruleId)
        {
            if (pendingConstraints.IsEmpty())
            {
                return;
            }

            Z3BoolExpr expr = null;
            Z3BoolExpr previousExpr;

            foreach (var constraint in pendingConstraints)
            {
                if (expr == null)
                {
                    expr = constraint;
                }
                else
                {
                    expr = Solver.Context.MkAnd(expr, constraint);
                }
            }
            expr = Solver.Context.MkNot(expr);
            
            if (recursionConstraints.TryGetValue(ruleId, out previousExpr))
            {
                recursionConstraints[ruleId] = Solver.Context.MkAnd(previousExpr, expr);
            }
            else
            {
                recursionConstraints.Add(ruleId, expr);
            }
        }

        public void PrintRecursionConflict(Z3BoolExpr expr)
        {
            Console.WriteLine("Conflict detected in recursion constraint: " + expr.ToString() + "\n\n");
        }

        public SymExecuter(Solver solver)
        {
            Contract.Requires(solver != null);
            Solver = solver;
            Rules = solver.PartialModel.Rules;
            Index = solver.PartialModel.Index;
            Encoder = new TermEncIndex(solver);
            KeepDerivations = true;

            solver.PartialModel.ConvertSymbCnstsToVars(out varFacts, out aliasMap);

            Map<ConSymb, List<Term>> cardTerms = new Map<ConSymb, List<Term>>(ConSymb.Compare);
            foreach (var fact in varFacts)
            {
                if (solver.PartialModel.CheckIfCardTerm(fact))
                {
                    List<Term> termList;
                    if (!cardTerms.TryFindValue((ConSymb)fact.Symbol, out termList))
                    {
                        termList = new List<Term>();
                        cardTerms.Add((ConSymb)fact.Symbol, termList);
                    }
                    termList.Add(fact);
                }
            }

            //// Need to pre-register all aliases with the encoder.
            bool wasAdded;
            foreach (var kv in aliasMap)
            {
                Term vTerm = Index.SymbCnstToVar(kv.Key, out wasAdded);
                Term tTerm = Solver.PartialModel.GetSymbCnstType(kv.Key);
                if (!varToTypeMap.ContainsKey(vTerm))
                {
                    varToTypeMap.Add(vTerm, tTerm);
                }
                var expr = Encoder.GetVarEnc(vTerm, tTerm);
            }

            InitializeExecuter();

            // TODO: handle cardinality terms properly
            foreach (var kvp in cardTerms)
            {
                var inequalities = kvp.Value.ToArray();
                for (int i = 0; i < inequalities.Length - 1; i++)
                {
                    Term first = inequalities[i];

                    SymElement firstEnc, secondEnc;
                    if (!lfp.TryFindValue(first, out firstEnc))
                    {
                        // flag error
                        continue;
                    }

                    for (int j = i + 1; j < inequalities.Length; j++)
                    {
                        Term second = inequalities[j];
                        if (!lfp.TryFindValue(second, out secondEnc))
                        {
                            // flag error
                            continue;
                        }

                        Z3BoolExpr boolExpr = Solver.Context.MkNot(Solver.Context.MkEq(firstEnc.Encoding, secondEnc.Encoding));
                    }
                }
            }

            Execute();
            
            bool hasConforms = false;
            foreach (var elem in lfp)
            {
                if (elem.Key.Symbol.PrintableName.EndsWith("conforms"))
                {
                    hasConforms = true;
                }
            }

            if (hasConforms)
            {
                var assumptions = new List<Z3Expr>();
                Z3BoolExpr topLevelConstraint = null;
                string pattern = @"conforms\d+$";
                foreach (var elem in lfp)
                {
                    if (Regex.IsMatch(elem.Key.Symbol.PrintableName, pattern))
                    {
                        SymElement symbolicConforms = elem.Value;
                        var constraint = symbolicConforms.GetSideConstraints(this);
                        if (constraint != null)
                        {
                            assumptions.Add(constraint);
                        }
                    }
                }

                foreach (var kvp in recursionConstraints)
                {
                    assumptions.Add(kvp.Value);
                }

                var status = Solver.Z3Solver.Check(assumptions.ToArray());
                if (status == Z3.Status.SATISFIABLE)
                {
                    System.Console.WriteLine("Model solvable.\n");

                    var model = Solver.Z3Solver.Model;
                    PrintSymbolicConstants(model);
                    PrintNewKindConstructors(model);

                    Console.WriteLine("Debug output terms:");

                    foreach (var kvp in lfp)
                    {
                        if (!kvp.Key.Symbol.PrintableName.StartsWith("~") &&
                            Encoder.CanGetEncoding(kvp.Key))
                        {
                            if (kvp.Value.HasConstraints())
                            {
                                var eval = model.Evaluate(kvp.Value.GetSideConstraints(this));
                                if (eval.BoolValue == Z3.Z3_lbool.Z3_L_TRUE)
                                {
                                    Console.WriteLine("  " + GetModelInterpretation(kvp.Key, model));
                                }
                            }
                            else
                            {
                                Console.WriteLine("  " + GetModelInterpretation(kvp.Key, model));
                            }
                        }
                    }
                }
                else if (status == Z3.Status.UNSATISFIABLE)
                {
                    Console.WriteLine("Model not solvable.\nUnsat core and related terms below.");
                    var core = Solver.Z3Solver.UnsatCore;
                    foreach (var expr in core)
                    {
                        if (recursionConstraints.ContainsValue(expr))
                        {
                            PrintRecursionConflict(expr);
                        }
                        else
                        {

                            Console.WriteLine("Expr: " + expr);
                            Console.WriteLine("Terms: ");
                            foreach (var item in lfp)
                            {
                                Term t = item.Key;
                                SymElement e = item.Value;

                                if (e.ContainsConstraint(expr))
                                {
                                    if (!(t.Symbol is UserCnstSymb &&
                                          ((UserCnstSymb)t.Symbol).IsAutoGen))
                                    {
                                        Console.WriteLine("  " + item.Key.ToString());
                                    }
                                }
                            }
                        }
                    }

                }
            }
            else
            {
                Console.WriteLine("Model not solvable.\nThe conforms term could not be derived.");
            }
        }

        private void PrintSymbolicConstants(Z3.Model model)
        {
            Console.WriteLine("Symbolic constants:");

            foreach (var kvp in aliasMap)
            {
                Console.WriteLine("  " + kvp.Key.Name.Substring(1) + " = " + GetModelInterpretation(kvp.Value, model));
            }

            Console.WriteLine("");
        }

        private void PrintNewKindConstructors(Z3.Model model)
        {
            Console.WriteLine("New-kind constructors:");

            foreach (var kvp in lfp)
            {
                Term t = kvp.Key;
                if (t.Symbol.IsDataConstructor &&
                    !((ConSymb)t.Symbol).IsAutoGen &&
                    Encoder.CanGetEncoding(kvp.Key))
                {
                    if (kvp.Value.HasConstraints())
                    {
                        var eval = model.Evaluate(kvp.Value.GetSideConstraints(this));
                        if (eval.BoolValue == Z3.Z3_lbool.Z3_L_TRUE)
                        {
                            Console.WriteLine("  " + GetModelInterpretation(kvp.Key, model));
                        }
                    }
                    else
                    {
                        Console.WriteLine("  " + GetModelInterpretation(kvp.Key, model));
                    }
                }
            }

            Console.WriteLine("");
        }

        public void Execute()
        {
            Activation act;
            var pendingAct = new Set<Activation>(Activation.Compare);
            var pendingFacts = new Map<Term, Set<Derivation>>(Term.Compare);
            LinkedList<CoreRule> untrigList;
            uint maxDepth = Solver.RecursionBound;

            for (int i = 0; i < Rules.StratificationDepth; ++i)
            {
                if (untrigRules.TryFindValue(i, out untrigList))
                {
                    foreach (var r in untrigList)
                    {
                        Term normalized;
                        Z3Expr enc = Encoder.GetTerm(Index.FalseValue, out normalized);
                        SymElement symFalse = new SymElement(normalized, enc, Solver.Context);
                        pendingAct.Add(new Activation(r, -1, symFalse));
                    }
                }

                foreach (var kv in trigIndices)
                {
                    kv.Value.PendAll(pendingAct, i);
                }

                while (pendingAct.Count > 0)
                {
                    bool copyConstraints = true;
                    act = pendingAct.GetSomeElement();
                    pendingAct.Remove(act);
                    int ruleId = act.Rule.RuleId;
                    PositiveConstraintTerms.Clear();
                    NegativeConstraintTerms.Clear();

                    act.Rule.Execute(act.Binding1.Term, act.FindNumber, this, KeepDerivations, pendingFacts);

                    // Check for cycles and cut if execution count exceeds max depth
                    if (ruleCycles.ContainsKey(ruleId))
                    {
                        ruleCycles[ruleId]++;
                        if (ruleCycles[ruleId] > maxDepth)
                        {
                            copyConstraints = false;
                        }
                    }

                    foreach (var kv in pendingFacts)
                    {
                        if (copyConstraints)
                        {
                            if (IsConstraintSatisfiable(kv.Key))
                            {
                                IndexFact(ExtendLFP(kv.Key), pendingAct, i);
                            }
                        }
                        else
                        {
                            AddRecursionConstraint(ruleId);
                        }
                    }

                    pendingConstraints.Clear();
                    pendingFacts.Clear();
                }
            }
        }

        private Z3BoolExpr CreateConstraint(Z3BoolExpr currConstraint, Z3BoolExpr nextConstraint)
        {
            if (currConstraint == null)
            {
                return nextConstraint;
            }
            else
            {
                return Solver.Context.MkAnd(currConstraint, nextConstraint);
            }
        }

        private bool ShouldCheckConstraints(Term t)
        {
            bool shouldCheckConstraints = true;
            string pattern = @"conforms\d+$";

            if (t.Symbol.PrintableName.EndsWith("conforms") ||
                Regex.IsMatch(t.Symbol.PrintableName, pattern))
            {
                shouldCheckConstraints = false;
            }

            if (pendingConstraints.IsEmpty() &&
                PositiveConstraintTerms.IsEmpty() &&
                NegativeConstraintTerms.IsEmpty())
            {
                shouldCheckConstraints = false;
            }

            return shouldCheckConstraints;
        }

        public bool IsConstraintSatisfiable(Term term)
        {
            if (!ShouldCheckConstraints(term))
            {
                return true;
            }

            Z3BoolExpr currConstraint = null;

            foreach (Term t in PositiveConstraintTerms)
            {
                var e = lfp[t];
                var nextConstraint = e.GetSideConstraints(this);
                currConstraint = CreateConstraint(currConstraint, nextConstraint);
            }

            foreach (Term t in NegativeConstraintTerms)
            {
                var e = lfp[t];
                var nextConstraint = e.GetSideConstraints(this);
                nextConstraint = Solver.Context.MkNot(nextConstraint);
                currConstraint = CreateConstraint(currConstraint, nextConstraint);
            }

            foreach (var nextConstraint in pendingConstraints)
            {
                currConstraint = CreateConstraint(currConstraint, nextConstraint);
            }

            var status = Solver.Z3Solver.Check(currConstraint);
            if (status == Z3.Status.UNSATISFIABLE)
            {
                System.Console.WriteLine("Unsat constraint: \n" + currConstraint + "\n\n");
                return false;
            }

            return true;
        }

        public string GetModelInterpretation(Term t, Z3.Model model)
        {
            if (t.Groundness == Groundness.Ground)
            {
                return t.ToString();
            }
            return t.Compute<string>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    if (x.Symbol.Arity == 0)
                    {
                        string str = "";
                        if (x.Symbol.Kind == SymbolKind.UserCnstSymb && x.Symbol.IsVariable)
                        {
                            var expr = Encoder.GetVarEnc(x, varToTypeMap[x]);
                            var interp = model.ConstInterp(expr);
                            if (Solver.TypeEmbedder.GetEmbedding(expr.Sort) is EnumEmbedding)
                            {
                                var embedding = Solver.TypeEmbedder.GetEmbedding(expr.Sort) as EnumEmbedding;
                                int index = (interp == null) ? 0 : ((Z3.BitVecNum)interp.Args[0]).Int;
                                str = embedding.GetSymbolAtIndex(index);
                            }
                            else if (interp == null)
                            {
                                // If there were no constraints on the term, use the default
                                str = Solver.TypeEmbedder.GetEmbedding(expr.Sort).DefaultMember.Item2.ToString();
                            }
                            else
                            {
                                str = interp.ToString();
                            }

                            return str;
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            str = x.Symbol.PrintableName;
                        }

                        return str;
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                    {
                        int arg1, arg2, res;
                        string str;
                        switch (((BaseOpSymb)x.Symbol).OpKind)
                        {
                            case OpKind.Add:
                                if (!Int32.TryParse(ch.ElementAt(0), out arg1) ||
                                    !Int32.TryParse(ch.ElementAt(1), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                res = arg1 + arg2;
                                str = "" + res;
                                return str;
                            case OpKind.Sub:
                                if (!Int32.TryParse(ch.ElementAt(0), out arg1) ||
                                    !Int32.TryParse(ch.ElementAt(1), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                res = arg1 - arg2;
                                str = "" + res;
                                return str;
                            case OpKind.Mul:
                                if (!Int32.TryParse(ch.ElementAt(0), out arg1) ||
                                    !Int32.TryParse(ch.ElementAt(1), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                res = arg1 * arg2;
                                str = "" + res;
                                return str;
                            case OpKind.Div:
                                if (!Int32.TryParse(ch.ElementAt(0), out arg1) ||
                                    !Int32.TryParse(ch.ElementAt(1), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                res = arg1 / arg2;
                                str = "" + res;
                                return str;
                            case OpKind.SymAnd:
                                if (ch.ElementAt(0) == "TRUE" && ch.ElementAt(1) == "TRUE")
                                {
                                    str = "TRUE";
                                }
                                else
                                {
                                    str = "FALSE";
                                }
                                return str;
                            case OpKind.SymAndAll:
                                bool hasFalse = ch.Any(s => s.Equals("FALSE"));
                                str = hasFalse ? "FALSE" : "TRUE";
                                return str;
                            case OpKind.SymMax:
                                if (!Int32.TryParse(ch.ElementAt(0), out arg1) ||
                                    !Int32.TryParse(ch.ElementAt(1), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                res = arg1 > arg2 ? arg1 : arg2;
                                str = "" + res;
                                return str;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    else if (x.Symbol.IsDataConstructor)
                    {
                        string str = x.Symbol.PrintableName;
                        str += "(";
                        for (int i = 0; i < ch.Count(); i++)
                        {
                            str += ch.ElementAt(i);
                            str += i == ch.Count() - 1 ? "" : ", ";
                        }
                        str += ")";
                        return str;
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                });
        }

        private void InitializeExecuter()
        {
            var optRules = Rules.Optimize();
            ruleCycles = Rules.GetCycles(optRules);

            foreach (var r in optRules)
            {
                foreach (var s in r.ComprehensionSymbols)
                {
                    Register(s);
                }

                if (r.Trigger1 == null && r.Trigger2 == null)
                {
                    Register(r);
                    continue;
                }

                if (r.Trigger1 != null)
                {
                    Register(r, 0);
                }

                if (r.Trigger2 != null)
                {
                    Register(r, 1);
                }
            }

            foreach (var f in varFacts)
            {
                IndexFact(ExtendLFP(f), null, -1);
            }

            Term scTerm;
            bool wasAdded;
            foreach (var sc in Rules.SymbolicConstants)
            {
                scTerm = Index.MkApply(
                    Index.SCValueSymbol,
                    new Term[] { sc, aliasMap[(UserCnstSymb)sc.Symbol]  },
                    out wasAdded);

                IndexFact(ExtendLFP(scTerm), null, -1);
            }
        }

        /// <summary>
        /// Extends the lfp with a symbolic element equivalent to t. 
        /// </summary>
        private SymElement ExtendLFP(Term t)
        {
            SymElement e;
            if (!lfp.TryFindValue(t, out e))
            {
                Term normalized = t;
                Z3Expr enc = null;
                if (Encoder.CanGetEncoding(t))
                {
                    enc = Encoder.GetTerm(t, out normalized, this);
                }

                e = new SymElement(normalized, enc, Solver.Context);
                lfp.Add(normalized, e);
            }

            if (!pendingConstraints.IsEmpty() ||
                !PositiveConstraintTerms.IsEmpty() ||
                !NegativeConstraintTerms.IsEmpty())
            {
                HashSet<Z3BoolExpr> a = new HashSet<Z3BoolExpr>(pendingConstraints);
                Set<Term> b = new Set<Term>(Term.Compare, PositiveConstraintTerms);
                Set<Term> c = new Set<Term>(Term.Compare, NegativeConstraintTerms);

                e.AddConstraintData(a, b, c);
            }
            else
            {
                e.SetDirectlyProvable();
            }

            return e;
        }
        
        private void IndexFact(SymElement t, Set<Activation> pending, int stratum)
        {
            LinkedList<SymSubIndex> subindices;
            if (!symbToIndexMap.TryFindValue(t.Term.Symbol, out subindices))
            {
                return;
            }

            foreach (var index in subindices)
            {
                index.TryAdd(t, pending, stratum);
            }
        }
        
        /// <summary>
        /// Register a rule without any finds.
        /// </summary>
        private void Register(CoreRule rule)
        {
            Contract.Requires(rule != null && rule.Trigger1 == null && rule.Trigger2 == null);
            LinkedList<CoreRule> untriggered;
            if (!untrigRules.TryFindValue(rule.Stratum, out untriggered))
            {
                untriggered = new LinkedList<CoreRule>();
                untrigRules.Add(rule.Stratum, untriggered);
            }

            untriggered.AddLast(rule);
        }

        /// <summary>
        /// Register a rule with a findnumber. The trigger may be constrained only by type constraints.
        /// </summary>
        private void Register(CoreRule rule, int findNumber)
        {
            Term trigger;
            Term type;
            switch (findNumber)
            {
                case 0:
                    trigger = rule.Trigger1;
                    type = rule.Find1.Type;
                    break;
                case 1:
                    trigger = rule.Trigger2;
                    type = rule.Find2.Type;
                    break;
                default:
                    throw new Impossible();
            }

            if (!trigger.Symbol.IsVariable)
            {
                Register(rule, trigger, findNumber);
                return;
            }

            Set<Term> patternSet;
            if (typesToTriggersMap.TryFindValue(type, out patternSet))
            {
                foreach (var p in patternSet)
                {
                    trigIndices[p].AddTrigger(rule, findNumber);
                }

                return;
            }

            Set<Symbol> triggerSymbols = new Set<Symbol>(Symbol.Compare);
            type.Visit(
                x => x.Symbol == Index.TypeUnionSymbol ? x.Args : null,
                x =>
                {
                    if (x.Symbol != Index.TypeUnionSymbol)
                    {
                        triggerSymbols.Add(x.Symbol);
                    }
                });

            Term pattern;
            patternSet = new Set<Term>(Term.Compare);
            foreach (var s in triggerSymbols)
            {
                if (s.Kind == SymbolKind.UserSortSymb)
                {
                    pattern = MkPattern(((UserSortSymb)s).DataSymbol, false);
                    patternSet.Add(pattern);
                    Register(rule, pattern, findNumber);
                }
                else
                {
                    Contract.Assert(s.IsDataConstructor || s.IsNonVarConstant);
                    pattern = MkPattern(s, false);
                    patternSet.Add(pattern);
                    Register(rule, pattern, findNumber);
                }
            }

            typesToTriggersMap.Add(type, patternSet);
        }

        /// <summary>
        /// Register a rule triggered by a find in position findnumber
        /// </summary>
        private void Register(CoreRule rule, Term trigger, int findNumber)
        {
            Contract.Requires(rule != null && trigger != null);

            SymSubIndex index;
            if (!trigIndices.TryFindValue(trigger, out index))
            {
                index = new SymSubIndex(this, trigger);
                trigIndices.Add(trigger, index);

                LinkedList<SymSubIndex> subindices;
                if (!symbToIndexMap.TryFindValue(index.Pattern.Symbol, out subindices))
                {
                    subindices = new LinkedList<SymSubIndex>();
                    symbToIndexMap.Add(index.Pattern.Symbol, subindices);
                }

                subindices.AddLast(index);
            }

            index.AddTrigger(rule, findNumber);
        }

        /// <summary>
        /// Register a comprehension symbol
        /// </summary>
        private void Register(Symbol comprSymbol)
        {
            Contract.Requires(comprSymbol != null);
            SymSubIndex index;
            if (!comprIndices.TryFindValue(comprSymbol, out index))
            {
                index = new SymSubIndex(this, MkPattern(comprSymbol, true));
                comprIndices.Add(comprSymbol, index);

                LinkedList<SymSubIndex> subindices;
                if (!symbToIndexMap.TryFindValue(comprSymbol, out subindices))
                {
                    subindices = new LinkedList<SymSubIndex>();
                    symbToIndexMap.Add(comprSymbol, subindices);
                }

                subindices.AddLast(index);
            }
        }

        /// <summary>
        /// Makes a subindex pattern for a symbol s. 
        /// If the symb is a comprehension symbol then the pattern is s(^0,...,^n-1, *0).
        /// Otherwise it is s(*0,...,*n).
        /// </summary>
        private Term MkPattern(Symbol s, bool isCompr)
        {
            Contract.Requires(s != null && (!isCompr || s.Arity > 0));
            bool wasAdded;
            var args = new Term[s.Arity];

            if (isCompr)
            {
                for (int i = 0; i < s.Arity - 1; ++i)
                {
                    args[i] = Index.MkVar(PatternVarBoundPrefix + i.ToString(), true, out wasAdded);
                }

                args[s.Arity - 1] = Index.MkVar(PatternVarUnboundPrefix + "0", true, out wasAdded);
            }
            else
            {
                for (int i = 0; i < s.Arity; ++i)
                {
                    args[i] = Index.MkVar(PatternVarUnboundPrefix + i.ToString(), true, out wasAdded);
                }
            }

            return Index.MkApply(s, args, out wasAdded);
        }

        private uint symbCnstId = 0;

        private Term MkSymbolicTerm(Symbol s)
        {
            Contract.Requires(s != null);
            bool wasAdded;
            var args = new Term[s.Arity];
            
            for (int i = 0; i < s.Arity; ++i)
            {
                UserCnstSymb symbCnst;
                AST<Id> id;
                args[i] = Index.MkSymbolicConstant("sc" + symbCnstId++, out symbCnst, out id);
                Solver.PartialModel.AddAlias(symbCnst, id.Node);
            }

            return Index.MkApply(s, args, out wasAdded);
        }

        public IEnumerable<Term> Query(Term comprTerm, out int nResults)
        {
            Contract.Requires(comprTerm != null);
            var subIndex = comprIndices[comprTerm.Symbol];
            var projection = new Term[comprTerm.Symbol.Arity - 1];

            //// Console.Write("Query {0}: [", subIndex.Pattern.Debug_GetSmallTermString());
            for (int i = 0; i < comprTerm.Symbol.Arity - 1; ++i)
            {
                //// Console.Write(" " + comprTerm.Args[i].Debug_GetSmallTermString());
                projection[i] = comprTerm.Args[i];
            }

            //// Console.WriteLine(" ]");

            return subIndex.Query(projection, out nResults);
        }

        public IEnumerable<Term> Query(Term pattern, Term[] projection)
        {
            /*
            Console.Write("Query {0}: [", pattern.Debug_GetSmallTermString());
            foreach (var t in projection)
            {
                Console.Write(" " + t.Debug_GetSmallTermString());
            }

            Console.WriteLine(" ]");
            */

            return trigIndices[pattern].Query(projection);
        }

        public IEnumerable<Term> Query(Term type, Term binding)
        {
            /*
            if (binding == null)
            {
                Console.WriteLine("Query type {0}", type.Debug_GetSmallTermString());
            }
            else
            {
                Console.WriteLine("Query type {0} = {1}", type.Debug_GetSmallTermString(), binding.Debug_GetSmallTermString());
            }
            */

            var patterns = typesToTriggersMap[type];
            if (binding != null)
            {
                foreach (var p in patterns)
                {
                    if (p.Symbol == binding.Symbol && Exists(binding))
                    {
                        yield return binding;
                        yield break;
                    }
                }

                yield break;
            }

            foreach (var p in patterns)
            {
                var results = trigIndices[p].Query(new Term[0]);
                foreach (var t in results)
                {
                    yield return t;
                }
            }

            yield break;
        }

        public Map<Term, Term> GetBindings(Term tA, Term tB, Map<Term, Set<Term>> partitions)
        {
            Map<Term, Term> bindings = new Map<Term, Term>(Term.Compare);
            Set<Term> lhsVars = new Set<Term>(Term.Compare);
            Set<Term> rhsVars = new Set<Term>(Term.Compare);

            // Collect all variables in the LHS term
            tA.Compute<Term>(
                (x, s) => x.Groundness == Groundness.Variable ? x.Args : null,
                (x, ch, s) =>
                {
                    if (x.Groundness != Groundness.Variable)
                    {
                        return null;
                    }
                    else if (x.Symbol.IsVariable)
                    {
                        lhsVars.Add(x);
                    }
                    else
                    {
                        foreach (var t in x.Args)
                        {
                            if (t.Symbol.IsVariable)
                            {
                                lhsVars.Add(t);
                            }
                        }
                    }

                    return null;
                });

            // Collect all variables in the RHS term
            tB.Compute<Term>(
                (x, s) =>
                {
                    if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                    {
                        return null; // don't descend into base op symbols
                    }
                    else
                    {
                        return x.Groundness == Groundness.Variable ? x.Args : null;
                    }
                },
                (x, ch, s) =>
                {
                    if (x.Groundness != Groundness.Variable)
                    {
                        return null;
                    }
                    else if (x.Symbol.IsVariable || x.Symbol.Kind == SymbolKind.BaseOpSymb || x.Symbol.Kind == SymbolKind.ConSymb)
                    {
                        rhsVars.Add(x);
                    }
                    else
                    {
                        foreach (var t in x.Args)
                        {
                            if (t.Symbol.IsVariable)
                            {
                                rhsVars.Add(t);
                            }
                        }
                    }

                    return null;
                });

            foreach (var part in partitions)
            {
                Set<Term> constants = new Set<Term>(Term.Compare);
                Set<Term> lhsPartVars = new Set<Term>(Term.Compare);
                Set<Term> rhsPartVars = new Set<Term>(Term.Compare);
                Set<Term> rhsPartGround = new Set<Term>(Term.Compare);

                foreach (var term in part.Value)
                {
                    if (term.Symbol.IsNonVarConstant)
                    {
                        constants.Add(term);
                    }
                    else if (lhsVars.Contains(term))
                    {
                        lhsPartVars.Add(term);
                    }
                    else if (rhsVars.Contains(term))
                    {
                        rhsPartVars.Add(term);
                    }
                    else if (term.Symbol.IsDataConstructor && term.Groundness == Groundness.Ground)
                    {
                        rhsPartGround.Add(term);
                    }
                }

                if (!constants.IsEmpty())
                {
                    Term constant = constants.First();
                    foreach (var rhs in rhsPartVars)
                    {
                        this.PendEqualityConstraint(rhs, constant);
                    }
                }

                if (!rhsPartVars.IsEmpty())
                {
                    foreach (var lhsVar in lhsPartVars)
                    {
                        bindings.Add(lhsVar, rhsPartVars.First());
                    }
                }
                else if (!constants.IsEmpty())
                {
                    foreach (var lhsVar in lhsPartVars)
                    {
                        bindings.Add(lhsVar, constants.First());
                    }
                }
                else if (!rhsPartGround.IsEmpty())
                {
                    foreach (var lhsVar in lhsPartVars)
                    {
                        bindings.Add(lhsVar, rhsPartGround.First());
                    }
                }
            }

            return bindings;
        }

        internal class SymSubIndex
        {
            public static readonly Term[] EmptyProjection = new Term[0];

            private SymExecuter Executer;

            private int nBoundVars;
            private Matcher patternMatcher;

            /// <summary>
            /// Map from strata to rules triggered by this pattern.
            /// </summary>
            private Map<int, LinkedList<Tuple<CoreRule, int>>> triggers =
                new Map<int, LinkedList<Tuple<CoreRule, int>>>((x, y) => x - y);

            /// <summary>
            /// Simple collection of facts. No pre-unification optimization.
            /// </summary>
            //private Set<SymElement> facts = new Set<SymElement>(SymElement.Compare);
            private Map<Term[], Set<SymElement>> facts = new Map<Term[], Set<SymElement>>(Compare);

            /// <summary>
            /// The pattern of this subindex.
            /// </summary>
            public Term Pattern
            {
                get;
                private set;
            }

            public SymSubIndex(SymExecuter executer, Term pattern)
            {
                Executer = executer;
                Pattern = pattern;

                patternMatcher = new Matcher(pattern);
                nBoundVars = 0;
                foreach (var kv in patternMatcher.CurrentBindings)
                {
                    if (((UserSymbol)kv.Key.Symbol).Name[0] == SymExecuter.PatternVarBoundPrefix)
                    {
                        ++nBoundVars;
                    }
                }
            }

            public IEnumerable<Term> Query(Term[] projection)
            {
                if (projection.IsEmpty())
                {
                    Set<SymElement> subindex;
                    if (!facts.TryFindValue(projection, out subindex))
                    {
                        yield break;
                    }

                    foreach (var t in subindex)
                    {
                        yield return t.Term;
                    }
                }
                else
                {
                    Set<SymElement> allFacts = new Set<SymElement>(SymElement.Compare);
                    foreach (var kvp in facts)
                    {
                        bool isUnifiable = true;
                        for (int i = 0; i < kvp.Key.Length; i++)
                        {
                            if (!Unifier.IsUnifiable(projection[i], kvp.Key[i]))
                            {
                                isUnifiable = false;
                                break;
                            }
                        }

                        if (isUnifiable)
                        {
                            foreach (SymElement e in kvp.Value)
                            {
                                allFacts.Add(e);
                            }
                        }
                    }

                    foreach (var t in allFacts)
                    {
                        yield return t.Term;
                    }
                }
            }

            public IEnumerable<Term> Query(Term[] projection, out int nResults)
            {
                Set<SymElement> subindex;
                if (!facts.TryFindValue(projection, out subindex))
                {
                    nResults = 0;
                }
                else
                {
                    nResults = subindex.Count;
                }

                return Query(projection);
            }

            public void AddTrigger(CoreRule rule, int findNumber)
            {
                LinkedList<Tuple<CoreRule, int>> rules;
                if (!triggers.TryFindValue(rule.Stratum, out rules))
                {
                    rules = new LinkedList<Tuple<CoreRule, int>>();
                    triggers.Add(rule.Stratum, rules);
                }

                rules.AddLast(new Tuple<CoreRule, int>(rule, findNumber));
            }

            /// <summary>
            /// Tries to add this term to the subindex. Returns true if t unifies with pattern.
            /// If pending is non-null, then pends rules that are triggered by this term.
            /// </summary>
            public bool TryAdd(SymElement t, Set<Activation> pending, int stratum)
            {
                Contract.Requires(t != null && t.Term.Groundness != Groundness.Type);
                Contract.Requires(t.Term.Owner == Pattern.Owner);

                Map<Term, Set<Term>> partitions = new Map<Term, Set<Term>>(Term.Compare);

                if (!Unifier.IsUnifiable(Pattern, t.Term, true, partitions))
                {
                    //// Terms t must unify with the pattern for insertion to succeed.
                    //// Pattern is already standardized apart from t.
                    return false;
                }

                Term[] projection;

                if (nBoundVars == 0)
                {
                    projection = EmptyProjection;
                }
                else
                {
                    var bindings = Executer.GetBindings(Pattern, t.Term, partitions);
                    int i = 0;
                    projection = new Term[nBoundVars];
                    foreach (var kv in patternMatcher.CurrentBindings)
                    {
                        if (((UserSymbol)kv.Key.Symbol).Name[0] == SymExecuter.PatternVarBoundPrefix)
                        {
                            projection[i++] = bindings[kv.Key];
                        }
                    }
                }

                Set<SymElement> subset;
                if (!facts.TryFindValue(projection, out subset))
                {
                    subset = new Set<SymElement>(SymElement.Compare);
                    facts.Add(projection, subset);
                }

                subset.Add(t);

                if (pending != null)
                {
                    LinkedList<Tuple<CoreRule, int>> triggered;
                    if (triggers.TryFindValue(stratum, out triggered))
                    {
                        foreach (var trig in triggered)
                        {
                            pending.Add(new Activation(trig.Item1, trig.Item2, t));
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// Generate pending activations for all rules triggered in this stratum. 
            /// </summary>
            public void PendAll(Set<Activation> pending, int stratum)
            {
                LinkedList<Tuple<CoreRule, int>> triggered;
                if (!triggers.TryFindValue(stratum, out triggered))
                {
                    return;
                }

                foreach (var kv in facts)
                {
                    foreach (var t in kv.Value)
                    {
                        foreach (var trig in triggered)
                        {
                            pending.Add(new Activation(trig.Item1, trig.Item2, t));
                        }
                    }
                }
            }

            public static int Compare(Term[] v1, Term[] v2)
            {
                return EnumerableMethods.LexCompare<Term>(v1, v2, Term.Compare);
            }
        }
    }
}
