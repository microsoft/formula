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
        private Map<Term, ShapeTrie> trigIndices = new Map<Term, ShapeTrie>(Term.Compare);

        /// <summary>
        /// Maps a comprehension symbol to a subindex.
        /// </summary>
        private Map<Symbol, ShapeTrie> comprIndices = new Map<Symbol, ShapeTrie>(Symbol.Compare);

        /// <summary>
        /// Maps a symbol to a set of indices with patterns beginning with this symbol. 
        /// </summary>
        private Map<Symbol, LinkedList<ShapeTrie>> symbToIndexMap = new Map<Symbol, LinkedList<ShapeTrie>>(Symbol.Compare);

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

        private List<Z3BoolExpr> pendingConstraints =
            new List<Z3BoolExpr>();

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

        public bool Exists(Term t)
        {
            return lfp.ContainsKey(t);
        }

        public void PendConstraint(Z3BoolExpr expr)
        {
            this.pendingConstraints.Add(expr);
        }

        public void PendEqualityConstraint(Z3Expr expr1, Z3Expr expr2)
        {
            this.pendingConstraints.Add(Solver.Context.MkEq(expr1, expr2));
        }

        public SymExecuter(Solver solver)
        {
            Contract.Requires(solver != null);
            Solver = solver;
            Rules = solver.PartialModel.Rules;
            Index = solver.PartialModel.Index;
            Encoder = new TermEncIndex(solver);

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
                        firstEnc.ExtendSideConstraint(0, boolExpr, Solver.Context);
                        Solver.Z3Solver.Add(boolExpr);
                    }
                }

            }

            Execute();

            foreach (var elem in lfp)
            {
                foreach (var currConstr in elem.Value.SideConstraints)
                {
                    Solver.Z3Solver.Assert(currConstr.Value);
                }
            }

            var status = Solver.Z3Solver.Check();
            if (status == Z3.Status.SATISFIABLE)
            {
                var model = Solver.Z3Solver.Model;
                foreach (var kvp in lfp)
                {
                    var s = GetModelInterpretation(kvp.Key, model);
                    Console.WriteLine(s);
                }
            }
            else if (status == Z3.Status.UNSATISFIABLE)
            {
                Console.WriteLine("Model not solvable");
            }
        }

        public void Execute()
        {
            Activation act;
            var pendingAct = new Set<Activation>(Activation.Compare);
            Set<Term> pendingFacts = new Set<Term>(Term.Compare);
            LinkedList<CoreRule> untrigList;
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
                    act = pendingAct.GetSomeElement();
                    pendingAct.Remove(act);
                    act.Rule.Execute(act.Binding1.Term, act.FindNumber, this, pendingFacts);
                    foreach (var pending in pendingFacts)
                    {
                        IndexFact(ExtendLFP(pending), pendingAct, i);
                    }

                    pendingConstraints.Clear();
                    pendingFacts.Clear();
                }
            }
        }

        public string GetModelInterpretation(Term t, Z3.Model model)
        {
            Queue<string> pieces = new Queue<string>();
            return t.Compute<string>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    if (x.Symbol.Arity == 0)
                    {
                        if (x.Symbol.Kind == SymbolKind.UserCnstSymb && x.Symbol.IsVariable)
                        {
                            string str;
                            var expr = Encoder.GetVarEnc(x, varToTypeMap[x]);
                            var interp = model.ConstInterp(expr);
                            if (interp == null)
                            {
                                // If there were no constraints on the term, use the default
                                str = Solver.TypeEmbedder.GetEmbedding(expr.Sort).DefaultMember.Item2.ToString();
                            }
                            else
                            {
                                str = interp.ToString();
                            }

                            pieces.Enqueue(str);
                            return str;
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            string str = x.Symbol.PrintableName;
                            pieces.Enqueue(str);
                            return str;
                        }

                        return "";
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseOpSymb)
                    {
                        switch (((BaseOpSymb)x.Symbol).OpKind)
                        {
                            case OpKind.Add:
                                int arg1, arg2;
                                if (!Int32.TryParse(pieces.Dequeue(), out arg1) ||
                                    !Int32.TryParse(pieces.Dequeue(), out arg2))
                                {
                                    throw new NotImplementedException();
                                }
                                int res = arg1 + arg2;
                                string str = "" + res;
                                pieces.Enqueue(str);
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
                            str += pieces.Dequeue();
                            str += i == ch.Count() - 1 ? "" : ",";
                        }
                        str += ")";
                        pieces.Enqueue(str);
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

            /*foreach (var rule in optRules.OrderBy(x => x.Stratum))
            {
                System.Console.WriteLine("Stratum = {0}, Rule id = {1}, head = {2}, find = {3}",
                    rule.Stratum,
                    rule.RuleId,
                    rule.Head.Debug_GetSmallTermString(),
                    rule.Find1.Pattern == null ? "" : rule.Find1.Pattern.Debug_GetSmallTermString()
                    );
            }*/

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
            if (lfp.TryFindValue(t, out e))
            {
                return e;
            }

            Term normalized;
            var enc = Encoder.GetTerm(t, out normalized);

            if (lfp.TryFindValue(normalized, out e))
            {
                return e;
            }

            //// Neither t nor a normalized version of t has been seen.
            e = new SymElement(normalized, enc, Solver.Context);
            lfp.Add(normalized, e);
            if (normalized != t)
            {
                lfp.Add(t, e);
            }

            foreach (var constr in pendingConstraints)
            {
                e.ExtendSideConstraint(0, constr, Solver.Context);
            }

            return e;
        }
        
        private void IndexFact(SymElement t, Set<Activation> pending, int stratum)
        {
            LinkedList<ShapeTrie> subindices;
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

            ShapeTrie index;
            if (!trigIndices.TryFindValue(trigger, out index))
            {
                index = new ShapeTrie(trigger);
                trigIndices.Add(trigger, index);

                LinkedList<ShapeTrie> subindices;
                if (!symbToIndexMap.TryFindValue(index.Pattern.Symbol, out subindices))
                {
                    subindices = new LinkedList<ShapeTrie>();
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
            ShapeTrie index;
            if (!comprIndices.TryFindValue(comprSymbol, out index))
            {
                index = new ShapeTrie(MkPattern(comprSymbol, true));
                comprIndices.Add(comprSymbol, index);

                LinkedList<ShapeTrie> subindices;
                if (!symbToIndexMap.TryFindValue(comprSymbol, out subindices))
                {
                    subindices = new LinkedList<ShapeTrie>();
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

        // TODO: implement the ShapeTrie query operation
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

            nResults = 0;
            return null;
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
    }
}
