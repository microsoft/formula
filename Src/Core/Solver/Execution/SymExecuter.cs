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

        public void ExtendPartialModel()
        {
            //// Introduce Terms for cardinality constraints
            ProgramName programName = new ProgramName("env:///dummy.4ml");
            AST<Program> program = Factory.Instance.MkProgram(programName);

            string domLoc = Solver.Source.Domain.Span.Program.ToString();
            string modLoc = Solver.Source.Span.Program.ToString();

            ModRef modRef = new ModRef(Span.Unknown, Solver.Source.Domain.Name, null, domLoc);
            AST<Model> model = Factory.Instance.MkModel("dummy", true, new ASTConcr<ModRef>(modRef), ComposeKind.Extends); // new model

            //AST<Model> model = Factory.Instance.MkModel("dummy", true, new ASTConcr<ModRef>(Solver.Source.Domain), ComposeKind.Extends);
            AST<ModRef> origModel = Factory.Instance.MkModRef(Solver.Source.Name, null, modLoc, Solver.Source.Span);
            model = Factory.Instance.AddModelCompose(model, origModel);
            model.Print(Console.Out);

            foreach (var entry in Solver.Cardinalities.SolverState)
            {
                foreach (var item in entry)
                {
                    var cardVar = item.Key;
                    var cardLower = item.Value.Item1.Lower;

                    if (cardVar.Symbol.IsDataConstructor &&
                        cardVar.IsLFPCard &&
                        cardLower > 0)
                    {
                        for (BigInteger i = 0; i < (BigInteger)cardLower; i++)
                        {
                            int arity = cardVar.Symbol.Arity;
                            AST<Node>[] args = new AST<Node>[arity];

                            for (int j = 0; j < arity; j++)
                            {
                                args[j] = Factory.Instance.MkId("sc" + symbCnstId++);
                            }

                            AST<FuncTerm> match = Factory.Instance.MkFuncTerm(Factory.Instance.MkId(cardVar.Symbol.Name), Span.Unknown, args);
                            AST<ModelFact> fact = Factory.Instance.MkModelFact(null, match);
                            model = Factory.Instance.AddFact(model, fact);
                        }
                    }
                }
            }

            program.Node.AddModule(model.Node);

            InstallResult result;
            Solver.Env.Install(program, out result);
            if (!result.Succeeded)
            {
                System.Console.WriteLine("Error installing partial model!");
            }
        }

        public SymExecuter(Solver solver)
        {
            Contract.Requires(solver != null);
            Solver = solver;
            Rules = solver.PartialModel.Rules;
            Index = solver.PartialModel.Index;
            Encoder = new TermEncIndex(solver);

            ExtendPartialModel();

            solver.PartialModel.ConvertSymbCnstsToVars(out varFacts, out aliasMap);

            //// Need to pre-register all aliases with the encoder.
            bool wasAdded;
            foreach (var kv in aliasMap)
            {
                Encoder.GetVarEnc(Index.SymbCnstToVar(kv.Key, out wasAdded), Solver.PartialModel.GetSymbCnstType(kv.Key));
            }

            InitializeExecuter();

            foreach (var ti in trigIndices.Values)
            {
                ti.Debug_Print();
            }
        }

        private void InitializeExecuter()
        {
            foreach (var r in Rules.Rules)
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
    }
}
