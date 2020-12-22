namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common.Extras;

    internal partial class TermIndex
    {
        internal static readonly Term[] EmptyArgs = new Term[0];
        private long nTerms = 0;
        private int nextSymbolId = 0;

        private ConSymb scValueSymb; 

        /// <summary>
        /// A map from symbolic constants to their types.
        /// </summary>
        private Func<Term, Term> symbCnstTypeGetter = null;
        private Map<Term, Term> symbCnstTypes = new Map<Term, Term>(Term.Compare);

        /// <summary>
        /// The set of all string constants
        /// </summary>
        private Map<string, BaseCnstSymb> stringCnsts =
            new Map<string, BaseCnstSymb>(string.CompareOrdinal);

        /// <summary>
        /// The set of all variable constants
        /// </summary>
        private Map<string, UserCnstSymb> variables =
            new Map<string, UserCnstSymb>(string.CompareOrdinal);

        /// <summary>
        /// A map from symbolic constants to a constructor that holds their value.
        /// </summary>
        private Map<string, ConSymb> symbConstValues =
            new Map<string, ConSymb>(string.CompareOrdinal);

        /// <summary>
        /// The set of all rational constants
        /// </summary>
        private Map<Rational, BaseCnstSymb> ratCnsts =
            new Map<Rational, BaseCnstSymb>(Rational.Compare);

        /// <summary>
        /// The sets of all terms sorted by symbol.
        /// </summary>
        private Map<Symbol, Set<Term>> bins = new Map<Symbol, Set<Term>>(Symbol.Compare);

        /// <summary>
        /// This is the set of canonical type arguments to user types, or the canonical
        /// type of a user-defined union.
        /// </summary>
        private Map<Tuple<UserSymbol, int>, Term> canTypeTerms = new Map<Tuple<UserSymbol, int>, Term>(Compare);

        /// <summary>
        /// A cache from symbols to type constants.
        /// </summary>
        private Map<UserSymbol, UserCnstSymb> typeConstants = new Map<UserSymbol, UserCnstSymb>(Symbol.Compare);

        /// <summary>
        /// Caches of canonical forms
        /// </summary>
        private Map<Term, Term> canonicalForms = new Map<Term, Term>(Term.Compare);
        private Map<TermPair, Term> canonicalIntrs = new Map<TermPair, Term>(TermPair.Compare);

        /// <summary>
        /// Maps an arity i to the union of all type constants with arity i.
        /// </summary>
        private Map<int, Term> arityToTypeMap = new Map<int, Term>((x, y) => x - y);

        /// <summary>
        /// Maps a type atom t to all pairs (f, i) where f ::= (..., T_i, ...) and t /\ T_i \neq \emptyset.
        /// All numeric types are generalized to Real.
        /// All string types are generalized to String.
        /// </summary>
        private Map<Term, Set<Tuple<UserSymbol, int>>> typeUses = null;

        /// <summary>
        /// A cache of subterm matchers.
        /// </summary>
        private Map<Tuple<bool, Term[]>, SubtermMatcher> matcherCache = new Map<Tuple<bool, Term[]>, SubtermMatcher>(SubtermMatcher.Compare);

        public long Count
        {
            get
            {
                return nTerms;
            }
        }

        public SymbolTable SymbolTable
        {
            get;
            private set;
        }

        public Term CanonicalAnyType
        {
            get;
            private set;
        }

        /// <summary>
        /// Allows a custom function to compute the type of a symbolic constant.
        /// Registration of symbolic constants can be skipped if this is provided.
        /// </summary>
        public Func<Term, Term> SymbCnstTypeGetter
        {
            get
            {
                Contract.Assert(symbCnstTypeGetter != null);
                return symbCnstTypeGetter;
            }

            set
            {
                Contract.Assert(symbCnstTypeGetter == null);
                symbCnstTypeGetter = value;
            }
        }

        /// <summary>
        /// An upper bound on the values over which a find variable can range.
        /// May be null if the domain does not contain any suitable values.
        /// </summary>
        public Term CanonicalFindType
        {
            get;
            private set;
        }

        public Term CanonicalBooleanType
        {
            get;
            private set;
        }

        public Term TypeConstantsType
        {
            get;
            private set;
        }

        /// <summary>
        /// May be null of no list-kind constructors
        /// </summary>
        public Term ListTypeConstantsType
        {
            get;
            private set;
        }

        public Term TrueValue
        {
            get;
            private set;
        }

        public Term FalseValue
        {
            get;
            private set;
        }

        public Term ZeroValue
        {
            get;
            private set;
        }

        public Term OneValue
        {
            get;
            private set;
        }

        public Term EmptyStringValue
        {
            get;
            private set;
        }

        public BaseOpSymb RangeSymbol
        {
            get;
            private set;
        }

        public BaseOpSymb SelectorSymbol
        {
            get;
            private set;
        }

        public BaseOpSymb TypeRelSymbol
        {
            get;
            private set;
        }
        
        public BaseOpSymb TypeUnionSymbol
        {
            get;
            private set;
        }

        public Env Env
        {
            get { return SymbolTable.Env; }
        }

        internal ConSymb SCValueSymbol
        {
            get
            {
                Contract.Assert(scValueSymb != null);
                return scValueSymb;
            }
        }

        internal bool IsSCValueDefined
        {
            get { return scValueSymb != null; }
        }

        public TermIndex(SymbolTable table)
        {
            Contract.Requires(table != null);

            SymbolTable = table;
            nextSymbolId = table.NSymbols;
            foreach (var r in table.RationalCnsts)
            {
                ratCnsts.Add((Rational)r.Raw, r);
            }

            foreach (var s in table.StringCnsts)
            {
                stringCnsts.Add((string)s.Raw, s);
            }

            UserCnstSymb uc;
            foreach (var v in table.Root.Symbols)
            {
                if (v.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)v;
                if (uc.UserCnstKind != UserCnstSymbKind.Variable)
                {
                    continue;
                }
                
                variables.Add(v.Name, uc);
            }

            SelectorSymbol = table.GetOpSymbol(ReservedOpKind.Select);
            TypeUnionSymbol = table.GetOpSymbol(ReservedOpKind.TypeUnn);
            RangeSymbol = table.GetOpSymbol(ReservedOpKind.Range);
            TypeRelSymbol = table.GetOpSymbol(RelKind.Typ);

            UserSymbol other;
            scValueSymb = table.Resolve(Terms.SymbolTable.SCValueName, out other) as ConSymb;
            Contract.Assert(other == null);

            MkAnyType(table);

            bool wasAdded;
            TrueValue = MkApply(
                table.Resolve(API.ASTQueries.ASTSchema.Instance.ConstNameTrue, out other),
                TermIndex.EmptyArgs,
                out wasAdded);
            Contract.Assert(TrueValue != null && other == null);

            FalseValue = MkApply(
                table.Resolve(API.ASTQueries.ASTSchema.Instance.ConstNameFalse, out other),
                TermIndex.EmptyArgs,
                out wasAdded);
            Contract.Assert(TrueValue != null && other == null);

            var boolSymb = table.Resolve(API.ASTQueries.ASTSchema.Instance.TypeNameBoolean, out other);
            Contract.Assert(boolSymb != null && other == null);
            CanonicalBooleanType = GetCanonicalTerm(boolSymb, 0);
            Contract.Assert(CanonicalBooleanType != null);

            CanonicalFindType = null;
            foreach (var t in CanonicalAnyType.Enumerate(x => x.Symbol == TypeUnionSymbol ? x.Args : null))
            {
                if (t.Symbol.Kind == SymbolKind.UserSortSymb || t.Symbol.IsDerivedConstant)
                {
                    if (CanonicalFindType == null)
                    {
                        CanonicalFindType = t;
                    }
                    else
                    {
                        CanonicalFindType = MkApply(TypeUnionSymbol, new Term[] { t, CanonicalFindType }, out wasAdded);
                    }
                }
            }

            EmptyStringValue = MkCnst(string.Empty, out wasAdded);
            ZeroValue = MkCnst(Rational.Zero, out wasAdded);
            OneValue = MkCnst(Rational.One, out wasAdded);
            SetTypeCnstTypes(SymbolTable.Root);
        }

        #region Mks
        public Term MkCnst(string s, out bool wasAdded)
        {
            BaseCnstSymb symb;
            if (!stringCnsts.TryFindValue(s, out symb))
            {
                symb = new BaseCnstSymb(s);
                symb.Id = nextSymbolId++;
                stringCnsts.Add(s, symb);
            }

            return MkApply(symb, EmptyArgs, out wasAdded);
        }

        public Term MkCnst(Rational r, out bool wasAdded)
        {
            BaseCnstSymb symb;
            if (!ratCnsts.TryFindValue(r, out symb))
            {
                symb = new BaseCnstSymb(r);
                symb.Id = nextSymbolId++;
                ratCnsts.Add(r, symb);
            }

            return MkApply(symb, EmptyArgs, out wasAdded);
        }

        public Term MkApply(Symbol s, Term[] args, out bool wasAdded)
        {
            Contract.Requires(s != null && args != null);
            Contract.Requires(Contract.ForAll<Term>(args, x => x.Owner == this));

            var bin = GetBin(s);
            Term t;
            Term tnew = new Term(s, args, this);

            if (bin.Contains(tnew, out t))
            {
                wasAdded = false;
                return t;
            }
            else
            {
                wasAdded = true;
                bin.Add(tnew);
                tnew.UId = nTerms++;
                return tnew;
            }
        }

        /// <summary>
        /// Converts a symbolic constant to a variable.
        /// </summary>
        public Term SymbCnstToVar(UserCnstSymb s, out bool wasAdded)
        {
            Contract.Requires(s != null && s.IsSymbolicConstant);
            return MkVar(
                s.FullName.Replace(".", "@").Replace("%", "~SC2VAR~"),
                true,
                out wasAdded);
        }

        /// <summary>
        /// Converts a variable back to a symbolic constant.
        /// </summary>
        public UserCnstSymb VarToSymbCnst(Term v)
        {
            Contract.Requires(v != null && v.Symbol.IsVariable);
            var symbCnstName = ((UserSymbol)v.Symbol).FullName.Replace("~SC2VAR~", "%").Replace("@", ".");
            UserSymbol symbCnst, other;
            symbCnst = SymbolTable.Resolve(symbCnstName, out other);
            Contract.Assert(symbCnst != null && other == null);
            Contract.Assert(symbCnst.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symbCnst).IsSymbolicConstant);
            return (UserCnstSymb)symbCnst;
        }

        public Term MkVar(string name, bool isAutoGen, out bool wasAdded)
        {
            UserCnstSymb symb;
            if (!variables.TryFindValue(name, out symb))
            {
                symb = new UserCnstSymb(
                                SymbolTable.Root,
                                Factory.Instance.MkId(name),
                                UserCnstSymbKind.Variable,
                                isAutoGen);
                symb.Id = nextSymbolId++;
                variables.Add(name, symb);
            }
            else
            {
                Contract.Assert(symb.IsAutoGen == isAutoGen);
            }

            return MkApply(symb, EmptyArgs, out wasAdded);
        }

        /// <summary>
        /// Makes a fresh symbolic constant. Used for creating cardinality constraints.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="wasAdded"></param>
        /// <returns>A term whose args are symbolic constants.</returns>
        public Term MkSymbolicConstant(string name, out UserCnstSymb s, out AST<Id> id)
        {
            s = this.SymbolTable.ModuleSpace.AddFreshSymbolicConstant(name, out id);
            bool wasAdded;
            return MkApply(s, EmptyArgs, out wasAdded);
        }

        /// <summary>
        /// Makes a fresh constant for internal use only.
        /// </summary>
        public UserCnstSymb MkFreshConstant(bool isDerived)
        {
            string name = string.Format("{0}{1}", SymbolTable.ManglePrefix, nextSymbolId);
            var symb = new UserCnstSymb(SymbolTable.Root, name, isDerived ? UserCnstSymbKind.Derived : UserCnstSymbKind.New);
            symb.Id = nextSymbolId++;
            return symb;
        }

        /// <summary>
        /// Makes a fresh constructor for internal use only.
        /// </summary>
        public ConSymb MkFreshConstructor(int arity)
        {
            Contract.Requires(arity > 0);
            string name = string.Format("{0}{1}", SymbolTable.ManglePrefix, nextSymbolId);
            var symb = new ConSymb(SymbolTable.Root, name, arity);
            symb.Id = nextSymbolId++;
            // TODO: check if this is needed
            //UserSortSymb userSort = new UserSortSymb(symb); 
            //userSort.Id = nextSymbolId++;
            //symb.SortSymbol = userSort;
            return symb;
        }

        /// <summary>
        /// Returns a variable to hold the value of a symbolic constant.
        /// </summary>
        public Term MkScVar(string symbConstName, bool isFindVar)
        {
            bool wasAdded;
            return MkVar(string.Format(
                "{0}{1}_{2}", 
                SymbolTable.ManglePrefix, 
                isFindVar ? "scfind" : "scvar",
                symbConstName.Replace(".", "@")), true, out wasAdded);
        }

        public SubtermMatcher MkSubTermMatcher(bool onlyNewKinds, Term[] patterns)
        {
            Contract.Requires(patterns != null && patterns.Length > 0);
            SubtermMatcher matcher;
            var matcherLabel = new Tuple<bool, Term[]>(onlyNewKinds, patterns);
            if (matcherCache.TryFindValue(matcherLabel, out matcher))
            {
                return matcher;
            }

            matcher = new SubtermMatcher(this, onlyNewKinds, patterns);
            matcherCache.Add(matcherLabel, matcher);
            return matcher;
        }
        
        /// <summary>
        /// Recreates the term t in this index. Requires all symbols, except variables,
        /// to already be defined and of equivalent arity. An optional user defined clone map
        /// can be provided to control the cloning of UserSymbols and UserSortSymbols. If
        /// a partial map is provided, then default behavior occurs for symbols not in the map.
        /// If dropLastRenaming is true and forgnSymbol is called X.Y. ... .f, then resolves Y.Z. ... .f.
        /// </summary>
        public Term MkClone(Term t, string renaming = null, Map<UserSymbol, UserSymbol> symbolMap = null, bool dropLastRenaming = false)
        {
            Contract.Requires(t != null);
            if (t.Owner == this)
            {
                return t;
            }

            Symbol sym;
            BaseCnstSymb bcs;
            BaseOpSymb bos;
            UserSymbol us, usp;
            UserCnstSymb ucs;
            bool wasAdded;
            return t.Compute<Term>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    sym = x.Symbol;
                    switch (sym.Kind)
                    {
                        case SymbolKind.BaseSortSymb:
                            return MkApply(SymbolTable.GetSortSymbol(((BaseSortSymb)sym).SortKind), EmptyArgs, out wasAdded);
                        case SymbolKind.BaseCnstSymb:
                            bcs = (BaseCnstSymb)sym;
                            switch (bcs.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    return MkCnst((Rational)bcs.Raw, out wasAdded);
                                case CnstKind.String:
                                    return MkCnst((string)bcs.Raw, out wasAdded);
                                default:
                                    throw new NotImplementedException();
                            }
                        case SymbolKind.BaseOpSymb:
                            bos = (BaseOpSymb)sym;
                            if (bos.OpKind is OpKind)
                            {
                                return MkApply(SymbolTable.GetOpSymbol((OpKind)bos.OpKind), ToArray(ch, bos.Arity), out wasAdded);
                            }
                            else if (bos.OpKind is RelKind)
                            {
                                return MkApply(SymbolTable.GetOpSymbol((RelKind)bos.OpKind), ToArray(ch, bos.Arity), out wasAdded);
                            }
                            else if (bos.OpKind is ReservedOpKind)
                            {
                                return MkApply(SymbolTable.GetOpSymbol((ReservedOpKind)bos.OpKind), ToArray(ch, bos.Arity), out wasAdded);
                            }

                            throw new NotImplementedException();
                        case SymbolKind.UserSortSymb:
                            us = ((UserSortSymb)sym).DataSymbol;
                            if (symbolMap != null && symbolMap.TryFindValue(us, out usp))
                            {
                                Contract.Assert(us.Arity == usp.Arity);
                            }
                            else
                            {
                                usp = SymbolTable.Resolve(us, renaming, dropLastRenaming);
                                Contract.Assert(usp != null);
                            }

                            switch (usp.Kind)
                            {
                                case SymbolKind.ConSymb:
                                    return MkApply(((ConSymb)usp).SortSymbol, EmptyArgs, out wasAdded);
                                case SymbolKind.MapSymb:
                                    return MkApply(((MapSymb)usp).SortSymbol, EmptyArgs, out wasAdded);
                                default:
                                    throw new NotImplementedException();
                            }
                        case SymbolKind.UserCnstSymb:
                            ucs = (UserCnstSymb)sym;
                            if (ucs.IsVariable)
                            {
                                if (symbolMap != null && symbolMap.TryFindValue(ucs, out usp))
                                {
                                    Contract.Assert(usp.Arity == 0);
                                    return MkApply(usp, EmptyArgs, out wasAdded);
                                }

                                return MkVar(ucs.Name, ucs.IsAutoGen, out wasAdded);
                            }

                            if (symbolMap != null && symbolMap.TryFindValue(ucs, out usp))
                            {
                                Contract.Assert(usp.Arity == 0);
                            }
                            else
                            {
                                usp = SymbolTable.Resolve(
                                            ucs, 
                                            ucs.IsDerivedConstant || ucs.IsTypeConstant || ucs.IsSymbolicConstant ? renaming : null,
                                            dropLastRenaming);
                                Contract.Assert(usp != null);
                            }

                            return MkApply(usp, EmptyArgs, out wasAdded);

                        default:
                            us = (UserSymbol)sym;
                            if (symbolMap != null && symbolMap.TryFindValue(us, out usp))
                            {
                                Contract.Assert(us.Arity == usp.Arity);
                            }
                            else
                            {
                                usp = SymbolTable.Resolve(us, renaming, dropLastRenaming);
                                Contract.Assert(usp != null);
                            }

                            return MkApply(usp, ToArray(ch, usp.Arity), out wasAdded);
                    }
                });
        }

        /// <summary>
        /// Tries to parse a ground term using the supplied configuration.
        /// </summary>
        public bool ParseGroundTerm(AST<Node> ast, Compiler.Configuration conf, List<Flag> flags, out Term grndTerm)
        {
            Contract.Requires(ast != null && conf != null && flags != null);

            var simplified = Compiler.Compiler.EliminateQuotations(conf, ast, flags);
            if (simplified.NodeKind != NodeKind.Id && simplified.NodeKind != NodeKind.Cnst && simplified.NodeKind != NodeKind.FuncTerm)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    simplified,
                    Constants.BadSyntax.ToString("Expected an identifier, constant, or function"),
                    Constants.BadSyntax.Code));
                grndTerm = null;
                return false;
            }

            var success = new SuccessToken();
            var symbStack = new Stack<Tuple<Namespace, Symbol>>();
            symbStack.Push(new Tuple<Namespace, Symbol>(SymbolTable.Root, null));
            var result = ast.Compute<Tuple<Term, Term>>(
                x => ParseGround_Unfold(x, symbStack, success, flags),
                (x, y) => ParseGround_Fold(x, y, symbStack, success, flags));
            grndTerm = result == null ? null : result.Item1;
            Contract.Assert(grndTerm == null || grndTerm.Groundness == Groundness.Ground);
            return success.Result && grndTerm != null;
        }

        /// <summary>
        /// Tries to parse a ground term using the supplied configuration.
        /// </summary>
        public bool ParseGroundTerm(string t, Compiler.Configuration conf, List<Flag> flags, out Term grndTerm)
        {
            ImmutableCollection<Flag> parseFlags;
            var ast = Factory.Instance.ParseDataTerm(t, out parseFlags, Env.Parameters);
            flags.AddRange(parseFlags);
            if (ast == null)
            {
                grndTerm = null;
                return false;
            }

            return ParseGroundTerm(ast, conf, flags, out grndTerm);
        }

        /// <summary>
        /// Returns all pairs (f, i) where f ::= (..., T_i, ...) and type /\ T_i \neq \emptyset.
        /// </summary>
        public IEnumerable<Tuple<UserSymbol, int>> GetTypeUses(Term type)
        {
            Contract.Requires(type != null && type.Groundness != Groundness.Variable);

            //// Need to build a map from type atoms to arg positions.
            if (typeUses == null)
            {
                typeUses = new Map<Term, Set<Tuple<UserSymbol, int>>>(Term.Compare);
                BuildTypeUses(SymbolTable.Root);              
            }

            bool wasAdded;
            Term useKey, intr;
            var realType = MkApply(SymbolTable.GetSortSymbol(BaseSortKind.Real), EmptyArgs, out wasAdded);
            var stringType = MkApply(SymbolTable.GetSortSymbol(BaseSortKind.String), EmptyArgs, out wasAdded);

            BaseCnstSymb bcs;
            BaseSortSymb bss;
            Set<Tuple<UserSymbol, int>> uses;
            foreach (var t in type.Enumerate(x =>
                {
                    if (x.Symbol.Kind == SymbolKind.UnnSymb)
                    {
                        return EnumerableMethods.GetEnumerable(GetCanonicalTerm((UserSymbol)x.Symbol, 0));
                    }
                    else if (x.Symbol == TypeUnionSymbol)
                    {
                        return x.Args;
                    }
                    else
                    {
                        return null;
                    }
                }))
            {
                switch (t.Symbol.Kind)
                {
                    case SymbolKind.ConSymb:
                        {
                            useKey = MkApply(((ConSymb)t.Symbol).SortSymbol, EmptyArgs, out wasAdded);
                            if (typeUses.TryFindValue(useKey, out uses))
                            {
                                foreach (var use in uses)
                                {
                                    yield return use;
                                }
                            }

                            break;
                        }
                    case SymbolKind.MapSymb:
                        {
                            useKey = MkApply(((MapSymb)t.Symbol).SortSymbol, EmptyArgs, out wasAdded);
                            if (typeUses.TryFindValue(useKey, out uses))
                            {
                                foreach (var use in uses)
                                {
                                    yield return use;
                                }
                            }

                            break;
                        }
                    case SymbolKind.UserSortSymb:
                    case SymbolKind.UserCnstSymb:
                        {
                            if (typeUses.TryFindValue(t, out uses))
                            {
                                foreach (var use in uses)
                                {
                                    yield return use;
                                }
                            }

                            break;
                        }
                    case SymbolKind.BaseCnstSymb:
                        {
                            bcs = (BaseCnstSymb)t.Symbol;
                            if (bcs.CnstKind == CnstKind.Numeric)
                            {
                                typeUses.TryFindValue(realType, out uses);
                            }
                            else if (bcs.CnstKind == CnstKind.String)
                            {
                                typeUses.TryFindValue(stringType, out uses);
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }

                            if (uses != null)
                            {
                                foreach (var tup in uses)
                                {
                                    if (tup.Item1.CanonicalForm[tup.Item2].AcceptsConstant(bcs))
                                    {
                                        yield return tup;
                                    }
                                }
                            }

                            break;
                        }
                    case SymbolKind.BaseSortSymb:
                        {
                            bss = (BaseSortSymb)t.Symbol;
                            switch (bss.SortKind)
                            {
                                case BaseSortKind.Integer:
                                case BaseSortKind.Natural:
                                case BaseSortKind.NegInteger:
                                case BaseSortKind.PosInteger:
                                case BaseSortKind.Real:
                                    typeUses.TryFindValue(realType, out uses);
                                    break;
                                case BaseSortKind.String:
                                    typeUses.TryFindValue(stringType, out uses);
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            if (uses != null)
                            {
                                foreach (var tup in uses)
                                {
                                    if (MkIntersection(GetCanonicalTerm(tup.Item1, tup.Item2), t, out intr))
                                    {
                                        yield return tup;
                                    }
                                }
                            }

                            break;
                        }
                    case SymbolKind.BaseOpSymb:
                        {
                            if (t.Symbol == RangeSymbol && typeUses.TryFindValue(realType, out uses))
                            {
                                if (uses != null)
                                {
                                    foreach (var tup in uses)
                                    {
                                        if (MkIntersection(GetCanonicalTerm(tup.Item1, tup.Item2), t, out intr))
                                        {
                                            yield return tup;
                                        }
                                    }
                                }
                            }

                            break;
                        }
                }
            }
        }

        private void SetTypeCnstTypes(Namespace space)
        {
            bool wasAdded;
            Term t;
            UserSymbol us;
            UserCnstSymb uc;
            foreach (var s in space.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (!uc.IsTypeConstant)
                {
                    continue;
                }

                TypeConstantsType = TypeConstantsType == null
                    ? MkApply(uc, EmptyArgs, out wasAdded)
                    : MkApply(TypeUnionSymbol, new Term[] { MkApply(uc, EmptyArgs, out wasAdded), TypeConstantsType }, out wasAdded);

                if (uc.Name.Contains("[") || !space.TryGetSymbol(uc.Name.Substring(1), out us))
                {
                    if (!arityToTypeMap.TryFindValue(0, out t))
                    {
                        arityToTypeMap.Add(0, MkApply(uc, EmptyArgs, out wasAdded));
                    }
                    else
                    {
                        arityToTypeMap[0] = MkApply(TypeUnionSymbol, new Term[] { MkApply(uc, EmptyArgs, out wasAdded), t }, out wasAdded);
                    }

                    continue;
                }
                else
                {
                    if (!arityToTypeMap.TryFindValue(us.Arity, out t))
                    {
                        arityToTypeMap.Add(us.Arity, MkApply(uc, EmptyArgs, out wasAdded));
                    }
                    else
                    {
                        arityToTypeMap[us.Arity] = MkApply(TypeUnionSymbol, new Term[] { MkApply(uc, EmptyArgs, out wasAdded), t }, out wasAdded);
                    }
                }

                if (us.Arity != 2)
                {
                    continue;
                }

                if ((us.Kind == SymbolKind.ConSymb && us.CanonicalForm[1].Contains(((ConSymb)us).SortSymbol)) ||
                    (us.Kind == SymbolKind.MapSymb && us.CanonicalForm[1].Contains(((MapSymb)us).SortSymbol)))
                {
                    ListTypeConstantsType = ListTypeConstantsType == null
                        ? MkApply(uc, EmptyArgs, out wasAdded)
                        : MkApply(TypeUnionSymbol, new Term[] { MkApply(uc, EmptyArgs, out wasAdded), ListTypeConstantsType }, out wasAdded);
                }
            }

            foreach (var c in space.Children)
            {
                SetTypeCnstTypes(c);
            }
        }
        
        private Term[] ToArray(IEnumerable<Term> args, int arity)
        {
            if (arity == 0)
            {
                return EmptyArgs;
            }

            int i = 0;
            var argsArr = new Term[arity];
            foreach (var a in args)
            {
                argsArr[i++] = a;
            }

            return argsArr;
        }

        private Set<Term> GetBin(Symbol s)
        {
            Set<Term> bin;
            if (!bins.TryFindValue(s, out bin))
            {
                bin = new Set<Term>(BinCompare);
                bins.Add(s, bin);
            }

            return bin;
        }

        private void BuildTypeUses(Namespace n)
        {
            bool wasAdded;
            BaseCnstSymb bcs;
            BaseSortSymb bss;
            AppFreeCanUnn[] canForm;
            Tuple<UserSymbol, int> use;

            var realType = MkApply(SymbolTable.GetSortSymbol(BaseSortKind.Real), EmptyArgs, out wasAdded);
            var stringType = MkApply(SymbolTable.GetSortSymbol(BaseSortKind.String), EmptyArgs, out wasAdded);
            foreach (var s in n.Symbols)
            {
                if (s.Kind == SymbolKind.ConSymb)
                {
                    canForm = ((ConSymb)s).CanonicalForm;
                }
                else if (s.Kind == SymbolKind.MapSymb)
                {
                    canForm = ((MapSymb)s).CanonicalForm;
                }
                else
                {
                    continue;
                }

                for (int i = 0; i < s.Arity; ++i)
                {
                    use = new Tuple<UserSymbol, int>(s, i);
                    using (var it = canForm[i].RangeMembers.GetEnumerator())
                    {
                        if (it.MoveNext())
                        {
                            GetUseSet(realType).Add(use);
                        }
                    }

                    foreach (var sp in canForm[i].NonRangeMembers)
                    {
                        switch (sp.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                                {
                                    bcs = (BaseCnstSymb)sp;
                                    if (bcs.CnstKind == CnstKind.Numeric)
                                    {
                                        GetUseSet(realType).Add(use);
                                    }
                                    else if (bcs.CnstKind == CnstKind.String)
                                    {
                                        GetUseSet(stringType).Add(use);
                                    }
                                    else
                                    {
                                        throw new NotImplementedException();
                                    }

                                    break;
                                }
                            case SymbolKind.UserCnstSymb:
                            case SymbolKind.UserSortSymb:
                                {
                                    GetUseSet(MkApply(sp, EmptyArgs, out wasAdded)).Add(use);
                                    break;
                                }
                            case SymbolKind.BaseSortSymb:
                                {
                                    bss = (BaseSortSymb)sp;
                                    switch (bss.SortKind)
                                    {
                                        case BaseSortKind.Integer:
                                        case BaseSortKind.Natural:
                                        case BaseSortKind.NegInteger:
                                        case BaseSortKind.PosInteger:
                                        case BaseSortKind.Real:
                                            GetUseSet(realType).Add(use);
                                            break;
                                        case BaseSortKind.String:
                                            GetUseSet(stringType).Add(use);
                                            break;
                                        default:
                                            throw new NotImplementedException();
                                    }

                                    break;
                                }
                            default:
                                throw new NotImplementedException();
                        }
                    }
                }                                               
            }

            foreach (var c in n.Children)
            {
                BuildTypeUses(c);
            }
        }

        private Set<Tuple<UserSymbol, int>> GetUseSet(Term typeAtom)
        {
            Set<Tuple<UserSymbol, int>> uses;
            if (!typeUses.TryFindValue(typeAtom, out uses))
            {
                uses = new Set<Tuple<UserSymbol, int>>(UseCompare);
                typeUses.Add(typeAtom, uses);
            }

            return uses;
        }

        private IEnumerable<Node> ParseGround_Unfold(Node n,
                                                     Stack<Tuple<Namespace, Symbol>> symbStack,
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
                                symb = (BaseCnstSymb)MkCnst((Rational)cnst.Raw, out wasAdded).Symbol;
                                break;
                            case CnstKind.String:
                                symb = (BaseCnstSymb)MkCnst((string)cnst.Raw, out wasAdded).Symbol;
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
                        if (SymbolTable.HasRenamingPrefix(id))
                        {
                            if (!Resolve(id.Name, "constant", id, space, x => x.IsNonVarConstant, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
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

        private Tuple<Term, Term> ParseGround_Fold(
                   Node n,
                   IEnumerable<Tuple<Term, Term>> args,
                   Stack<Tuple<Namespace, Symbol>> symbStack,
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
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadSyntax.ToString("Symbolic constants are not allowed here."),
                        Constants.BadSyntax.Code));
                    return null;
                }
                else
                {
                    var valTerm = MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                    return new Tuple<Term, Term>(valTerm, valTerm);
                }
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
                    else
                    {
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

                return new Tuple<Term, Term>(MkApply(con, vargs, out wasAdded), MkApply(sort, TermIndex.EmptyArgs, out wasAdded));
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

            symbol = SymbolTable.Resolve(id, out other, space);
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
        #endregion

        #region Type Operations

        public void RegisterSymbCnstType(Term symbCnst, Term type)
        {
            Contract.Requires(symbCnst != null && type != null);
            Contract.Requires(symbCnst.Symbol.IsNewConstant && ((UserCnstSymb)symbCnst.Symbol).IsSymbolicConstant);
            Contract.Requires(type.Groundness != Groundness.Variable);

            symbCnstTypes.Add(symbCnst, type);
        }

        public Term GetSymbCnstType(Term symbCnst)
        {
            Contract.Requires(symbCnst != null);
            Contract.Requires(symbCnst.Symbol.IsNewConstant && ((UserCnstSymb)symbCnst.Symbol).IsSymbolicConstant);

            if (symbCnstTypeGetter == null)
            {
                return symbCnstTypes[symbCnst];
            }

            Term type;
            if (!symbCnstTypes.TryFindValue(symbCnst, out type))
            {
                type = symbCnstTypeGetter(symbCnst);
                symbCnstTypes.Add(symbCnst, type);
            }

            return type;
        }

        /// <summary>
        /// Returns a union of all type constants with a given arity. 
        /// Or null if there is no such constant.
        /// </summary>
        public Term GetTypeConstType(int arity)
        {
            Term t;
            return arityToTypeMap.TryFindValue(arity, out t) ? t : null;
        }

        /// <summary>
        /// Returns the set of all 
        /// </summary>
        /// <returns></returns>
        public IntIntervals GetConstructorArities()
        {
            var iints = new IntIntervals();
            foreach (var a in arityToTypeMap.Keys)
            {
                if (a > 0)
                {
                    iints.Add(new BigInteger(a), new BigInteger(a));
                }
            }

            return iints;
        }

        /// <summary>
        /// Converts a Con / Map / Unn user symbol to its type constant.
        /// </summary>
        public UserCnstSymb ToTypeConstant(UserSymbol s)
        {
            Contract.Requires(s != null);
            Contract.Requires(s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb || s.Kind == SymbolKind.UnnSymb);

            UserCnstSymb typeCnst;
            if (typeConstants.TryFindValue(s, out typeCnst))
            {
                return typeCnst;
            }

            UserSymbol sp;
            var result = s.Namespace.TryGetSymbol("#" + s.Name, out sp);
            Contract.Assert(sp != null && sp.Kind == SymbolKind.UserCnstSymb);
            typeCnst = (UserCnstSymb)sp;
            Contract.Assert(typeCnst.IsTypeConstant);
            typeConstants.Add(s, typeCnst);
            return typeCnst;
        }

        /// <summary>
        /// Returns the cached representation of the canonical type arguments
        /// to a constructor/map/union.
        /// </summary>
        internal Term GetCanonicalTerm(UserSymbol s, int i)
        {
            Term t;
            var tup = new Tuple<UserSymbol, int>(s, i);
            if (!canTypeTerms.TryFindValue(tup, out t))
            {
                t = s.CanonicalForm[i].MkTypeTerm(this);
                canTypeTerms.Add(tup, t);
            }

            return t;
        }

        /// <summary>
        /// Converts all constructor applications f(...) to the sort f. 
        /// </summary>
        internal Term MkDataWidenedType(Term ttype)
        {
            Contract.Requires(ttype != null && ttype.Groundness != Groundness.Variable);
            Contract.Requires(ttype.Owner == this);
            Term t;
            Term wtype = null;
            bool wasAdded;
            ttype.Visit(
                x => x.Symbol == TypeUnionSymbol ? x.Args : null,
                x =>
                {
                    if (x.Symbol == TypeUnionSymbol)
                    {
                        return;
                    }

                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.ConSymb:
                            t = MkApply(((ConSymb)x.Symbol).SortSymbol, EmptyArgs, out wasAdded);
                            break;
                        case SymbolKind.MapSymb:
                            t = MkApply(((MapSymb)x.Symbol).SortSymbol, EmptyArgs, out wasAdded);
                            break;
                        default:
                            t = x;
                            break;
                    }

                    wtype = wtype == null ? t : MkApply(TypeUnionSymbol, new Term[] { t, wtype }, out wasAdded);
                });

            return wtype;
        }

        /// <summary>
        /// Compares the smallest element in type tmin with the largest element in type tmax.
        /// Returns unknown if the relationship can't be determined.
        /// </summary>
        /// <param name="t1"></param>
        /// <param name="t2"></param>
        /// <returns></returns>
        internal LiftedInt LexicographicMinMaxCompare(Term tmin, Term tmax)
        {
            Contract.Requires(tmin != null && tmin.Owner == this && tmin.Groundness != Groundness.Variable);
            Contract.Requires(tmax != null && tmax.Owner == this && tmax.Groundness != Groundness.Variable);
            var min = MkSmallest(tmin);
            var max = MkLargest(tmax);
            var cmp = 0;
            var isCmp = true;
            var success = new SuccessToken();
            min.Compute<Unit>(
                max,
                (x, y, s) =>
                {
                    if (x == y && 
                        x.Groundness == Groundness.Ground && 
                        y.Groundness == Groundness.Ground)
                    {
                        return null;
                    }

                    cmp = x.Family - y.Family;
                    if (cmp != 0)
                    {
                        s.Failed();
                        return null;
                    }

                    switch (x.Family)
                    {
                        case Term.FamilyNumeric:
                            if (x.Symbol.Kind == SymbolKind.BaseSortSymb || y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = -1;
                            }
                            else
                            {
                                cmp = Rational.Compare((Rational)((BaseCnstSymb)x.Symbol).Raw, (Rational)((BaseCnstSymb)y.Symbol).Raw);
                            }

                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyString:
                            if (y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = -1;
                            }
                            else
                            {
                                cmp = String.CompareOrdinal((string)((BaseCnstSymb)x.Symbol).Raw, (string)((BaseCnstSymb)y.Symbol).Raw);
                            }

                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyUsrCnst:
                            cmp = String.CompareOrdinal(((UserCnstSymb)x.Symbol).FullName, ((UserCnstSymb)y.Symbol).FullName);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyApp:
                            string xName, yName;
                            if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                xName = ((UserSortSymb)x.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                xName = ((UserSymbol)x.Symbol).FullName;
                            }

                            if (y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                yName = ((UserSortSymb)y.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                yName = ((UserSymbol)y.Symbol).FullName;
                            }

                            cmp = String.CompareOrdinal(xName, yName);
                            if (cmp != 0)
                            {
                                s.Failed();
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.UserSortSymb ||
                                     y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                isCmp = false;
                                s.Failed();
                                return null;
                            }
                            else
                            {
                                return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(x.Args, y.Args);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                },
                (x, y, ch, s) =>
                {
                    return default(Unit);
                },
                success);

            return isCmp ? new LiftedInt(cmp) : LiftedInt.Unknown;
        }

        /// <summary>
        /// Compares two ground terms in a standard manner without using their UIDs. 
        /// Comparison is consistent across compatible type declarations.
        /// </summary>
        internal int LexicographicCompare(Term t1, Term t2)
        {
            Contract.Requires(t1 != null && t1.Owner == this && t1.Groundness == Groundness.Ground);
            Contract.Requires(t2 != null && t2.Owner == this && t2.Groundness == Groundness.Ground);
            if (t1 == t2)
            {
                return 0;
            }

            int cmp = 0;
            var successToken = new SuccessToken();
            t1.Compute<Unit>(
                t2,
                (x, y, s) =>
                {
                    if (x == y)
                    {
                        return null;
                    }

                    cmp = x.Family - y.Family;
                    if (cmp != 0)
                    {
                        s.Failed();
                        return null;
                    }

                    switch (x.Family)
                    {
                        case Term.FamilyNumeric:
                            cmp = Rational.Compare((Rational)((BaseCnstSymb)x.Symbol).Raw, (Rational)((BaseCnstSymb)y.Symbol).Raw);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyString:
                            cmp = String.CompareOrdinal((string)((BaseCnstSymb)x.Symbol).Raw, (string)((BaseCnstSymb)y.Symbol).Raw);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyUsrCnst:
                            cmp = String.CompareOrdinal(((UserCnstSymb)x.Symbol).FullName, ((UserCnstSymb)y.Symbol).FullName);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyApp:
                            Contract.Assert(x.Args.Length > 0 && y.Args.Length > 0);
                            cmp = String.CompareOrdinal(((UserSymbol)x.Symbol).FullName, ((UserSymbol)y.Symbol).FullName);
                            if (cmp != 0)
                            {
                                s.Failed();
                                return null;
                            }
                            else
                            {
                                return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(x.Args, y.Args);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                },
                (x, y, ch, s) =>
                {
                    return default(Unit);
                },
                successToken);
                             
            return cmp;
        }

        /// <summary>
        /// Tests if tgrnd is a member of us[index], assuming tgrnd is already well-typed.
        /// If us does not have a type definition, then returns true.
        /// </summary>
        internal bool IsGroundMember(UserSymbol us, int index, Term tgrnd)
        {
            Contract.Requires(tgrnd != null && tgrnd.Groundness == Groundness.Ground);
            Contract.Requires(tgrnd.Owner == this);
            if (us.CanonicalForm == null || us == scValueSymb)
            {
                return true;
            }

            return IsGroundMember(GetCanonicalTerm(us, index), tgrnd);
        }

        /// <summary>
        /// Tests if tgrnd is a member of ttype, assuming tgrnd is already well-typed
        /// </summary>
        [Pure]
        internal bool IsGroundMember(Term ttype, Term tgrnd)
        {
            Contract.Requires(ttype != null && ttype.Groundness != Groundness.Variable);
            Contract.Requires(tgrnd != null && tgrnd.Groundness == Groundness.Ground);
            Contract.Requires(ttype.Owner == tgrnd.Owner);

            return ttype.Compute<bool>(
                tgrnd,
                (tt, tg, sc) => new Tuple<IEnumerable<Term>, IEnumerable<Term>>(IsGrndMem_Unfold(tt, tg, true), IsGrndMem_Unfold(tt, tg, false)),
                (tt, tg, vls, sc) => IsGrndMem_Fold(tt, tg, vls));
        }

        /// <summary>
        /// Data widens and compares types:
        /// </summary>
        internal bool IsSubtypeWidened(Term typeLft, Term typeRt)
        {
            Contract.Requires(typeLft != null && typeRt != null);

            if (typeLft == typeRt)
            {
                return true;
            }

            var appRight = new AppFreeCanUnn(typeRt);
            var success = new SuccessToken();
            typeLft.Visit(
                x => x.Symbol == TypeUnionSymbol ? x.Args : null,
                x =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                        case SymbolKind.UserCnstSymb:
                        case SymbolKind.BaseSortSymb:
                            if (!appRight.AcceptsConstants(x))
                            {
                                success.Failed();
                            }

                            break;
                        case SymbolKind.UserSortSymb:
                            if (!appRight.Contains(x.Symbol))
                            {
                                success.Failed();
                            }

                            break;
                        case SymbolKind.ConSymb:
                            if (!appRight.Contains(((ConSymb)x.Symbol).SortSymbol))
                            {
                                success.Failed();
                            }

                            break;
                        case SymbolKind.MapSymb:
                            if (!appRight.Contains(((MapSymb)x.Symbol).SortSymbol))
                            {
                                success.Failed();
                            }

                            break;
                        case SymbolKind.BaseOpSymb:
                            if (x.Symbol == RangeSymbol && !appRight.AcceptsConstants(x))
                            {
                                success.Failed();
                            }

                            break;
                        default:
                            throw new NotImplementedException();
                    }
                },
                success);

            return success.Result;
        }

        /// <summary>
        /// Converts a ground term a natural
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        internal Term MkNatural(Term t)
        {
            Contract.Requires(t != null && t.Owner == this && t.Groundness == Groundness.Ground);
            var str = (string)((BaseCnstSymb)MkString(t).Symbol).Raw;
            var value = BigInteger.Zero;
            for (int i = 0; i < str.Length; ++i)
            {
                value += ((int)str[i]) + (256 * value);
            }

            bool wasAdded;
            return MkCnst(new Rational(value, BigInteger.One), out wasAdded);
        }

        /// <summary>
        /// Converts a ground term to a unique string term.
        /// </summary>
        internal Term MkString(Term t)
        {
            Contract.Requires(t != null && t.Owner == this && t.Groundness == Groundness.Ground);
            var bldr = new System.Text.StringBuilder();
            var argCountStack = new Stack<MutableTuple<int>>();
            MutableTuple<int> argCount;
            BaseCnstSymb bc;
            UserCnstSymb uc;

            t.Compute<Unit>(
                (x, s) =>
                {
                    if (x.Args.Length > 0)
                    {
                        argCountStack.Push(new MutableTuple<int>(x.Args.Length));
                        bldr.Append(((UserSymbol)x.Symbol).FullName + "(");
                    }

                    return x.Args;
                },
                (x, ch, s) =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            bc = (BaseCnstSymb)x.Symbol;
                            if (bc.CnstKind == CnstKind.Numeric)
                            {
                                bldr.Append(((Rational)bc.Raw).ToString());
                            }
                            else if (bc.CnstKind == CnstKind.String)
                            {
                                MkString((string)bc.Raw, bldr);
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }

                            break;
                        case SymbolKind.UserCnstSymb:
                            uc = (UserCnstSymb)x.Symbol;
                            bldr.Append(uc.FullName);
                            break;
                        default:
                            break;
                    }

                    if (argCountStack.Count > 0)
                    {
                        argCount = argCountStack.Peek();
                        --argCount.Item1;
                        if (argCount.Item1 == 0)
                        {
                            bldr.Append(")");
                            argCountStack.Pop();
                        }
                        else
                        {
                            bldr.Append(", ");
                        }
                    }

                    return default(Unit);
                });

            bool wasAdded;
            return MkCnst(bldr.ToString(), out wasAdded);
        }

        /// <summary>
        /// Computes the canonical form of type t. The type t should already be well-typed.
        /// </summary>
        internal Term MkCanonicalForm(Term t)
        {
            Contract.Requires(t != null);
            Contract.Requires(t.Owner == this);
            Contract.Requires(t.Groundness != Groundness.Variable);

            bool wasAdded;
            Term canForm;
            return t.Compute<Term>(
                (x, s) =>
                {
                    if (canonicalForms.ContainsKey(x))
                    {
                        return null;
                    }
                    else if (x.Symbol == TypeUnionSymbol || x.Symbol.Kind == SymbolKind.UnnSymb)
                    {
                        return GetComponents(x);
                    }
                    else if (x.Symbol.IsDataConstructor)
                    {
                        return x.Args;
                    }
                    else
                    {
                        return null;
                    }
                },
                (x, ch, s) =>
                {
                    if (canonicalForms.TryFindValue(x, out canForm))
                    {
                        return canForm;
                    }
                    else if (x.Symbol.IsDataConstructor)
                    {
                        //// Check if f(...) should be folded into the sort f

                        int i = 0;
                        bool fold = true;
                        var us = (UserSymbol)x.Symbol;
                        foreach (var a in ch)
                        {
                            if (a != GetCanonicalTerm(us, i++))
                            {
                                fold = false;
                                break;
                            }
                        }

                        if (fold)
                        {
                            canForm = MkApply(
                                us.Kind == SymbolKind.ConSymb ? ((ConSymb)us).SortSymbol : ((MapSymb)us).SortSymbol,
                                EmptyArgs,
                                out wasAdded);
                            canonicalForms.Add(x, canForm);
                            return canForm;
                        }
                        else
                        {
                            //// Check if x already agrees with canonical form
                            //// to avoid rebuilding the same term.
                            i = 0;
                            foreach (var a in ch)
                            {
                                if (a != x.Args[i])
                                {
                                    break;
                                }
                                else
                                {
                                    ++i;
                                }
                            }

                            if (i == us.Arity)
                            {
                                return x;
                            }
                           
                            var args = new Term[us.Arity];
                            i = 0;
                            foreach (var a in ch)
                            {
                                args[i++] = a;
                            }

                            canForm = MkApply(us, args, out wasAdded);
                            canonicalForms.Add(x, canForm);
                            return canForm;
                        }
                    }
                    else if (x.Symbol == TypeUnionSymbol || x.Symbol.Kind == SymbolKind.UnnSymb)
                    {
                        canForm = MkCanUnion(ch);
                        if (!canonicalForms.ContainsKey(x))
                        {
                            canonicalForms.Add(x, canForm);
                        }

                        return canForm;
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                    {
                        var bc = (BaseCnstSymb)x.Symbol;
                        if (bc.CnstKind != CnstKind.Numeric)
                        {
                            return x;
                        }

                        var rat = (Rational)bc.Raw;
                        if (!rat.IsInteger)
                        {
                            return x;
                        }

                        return MkApply(RangeSymbol, new Term[] { x, x }, out wasAdded);
                    }
                    else
                    {
                        return x;
                    }
                });
        }

        internal bool MkIntersection(Term t1, Term t2, out Term tintr)
        {
            Contract.Requires(t1 != null && t2 != null);
            Contract.Requires(t1.Owner == this && t2.Owner == this);
            if (t1 == CanonicalAnyType)
            {
                tintr = t2;
                return true;
            }
            else if (t2 == CanonicalAnyType)
            {
                tintr = t1;
                return true;
            }
            else if (t1.Groundness == Groundness.Ground)
            {
                if (IsGroundMember(t2, t1))
                {
                    tintr = t1;
                    return true;
                }
                else
                {
                    tintr = null;
                    return false;
                }
            }
            else if (t2.Groundness == Groundness.Ground)
            {
                if (IsGroundMember(t1, t2))
                {
                    tintr = t2;
                    return true;
                }
                else
                {
                    tintr = null;
                    return false;
                }
            }

            var unn = new BinnedUnion(t1);
            return unn.MkIntersection(t2, out tintr);
        }

        /// <summary>
        /// Surrounds a string by double quotes and converts every internal " -> \" and \ -> \\
        /// </summary>
        private void MkString(string s, System.Text.StringBuilder bldr)
        {
            bldr.Append("\"");

            char c;
            for (int i = 0; i < s.Length; ++i)
            {
                c = s[i];

                switch (c)
                {
                    case '\"':
                        bldr.Append("\\\"");
                        break;
                    case '\\':
                        bldr.Append("\\\\");
                        break;
                    default:
                        bldr.Append(c);
                        break;
                }
            }

            bldr.Append("\"");
        }

        /// <summary>
        /// Returns a term representing the smallest value in the type as follows:
        /// Smallest(t) =
        ///  (1) Real if t = Real
        ///  (2) Integer if t = Integer
        ///  (3) NegInteger if t = NegInteger
        ///  (4) 0 if t = Natural
        ///  (5) 1 if t = PosInteger
        ///  (6) c_l if t = c_l..c_u
        ///  (7) t_g if t is ground
        ///  (8) F if t = F
        ///  (9) f(Smallest(t_1),...,Smallest(t_n)) if t = f(...)
        ///  (10) min{ Smallest(t_1), ..., Smallest(t_n) } if t = t_1 + ... + t_n.
        /// 
        /// If Smallest returns a type, then this should be interpreted as the smallest element in the type.
        /// </summary>
        private Term MkSmallest(Term t)
        {
            Contract.Requires(t != null && t.Owner == this && t.Groundness != Groundness.Variable);
            bool wasAdded;
            return t.Compute<Term>(
                (x, s) =>
                {
                    if (x.Groundness == Groundness.Ground)
                    {
                        return null;
                    }
                    else
                    {
                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    if (x.Groundness == Groundness.Ground ||
                        x.Symbol.Kind == SymbolKind.UserSortSymb)
                    {
                        return x;
                    }
                    else if (x.Symbol == TypeUnionSymbol)
                    {
                        using (var it = ch.GetEnumerator())
                        {
                            it.MoveNext();
                            var t1 = it.Current;
                            it.MoveNext();
                            var t2 = it.Current;
                            return GetMinType(t1, t2);
                        }
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                    {
                        var bs = (BaseSortSymb)x.Symbol;
                        switch (bs.SortKind)
                        {
                            case BaseSortKind.Natural:
                                return MkCnst(Rational.Zero, out wasAdded);
                            case BaseSortKind.PosInteger:
                                return MkCnst(Rational.One, out wasAdded);
                            case BaseSortKind.String:
                                return MkCnst(string.Empty, out wasAdded);
                            default:
                                return x;
                        }
                    }
                    else if (x.Symbol == RangeSymbol)
                    {
                        return LexicographicCompare(x.Args[0], x.Args[1]) <= 0 ? x.Args[0] : x.Args[1];
                    }
                    else if (x.Symbol.Kind == SymbolKind.ConSymb ||
                             x.Symbol.Kind == SymbolKind.MapSymb)
                    {
                        var args = new Term[x.Symbol.Arity];
                        var i = 0;
                        foreach (var c in ch)
                        {
                            args[i++] = c;
                        }

                        return MkApply(x.Symbol, args, out wasAdded);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                });
        }

        /// <summary>
        /// Returns a term representing the biggest value in the type as follows:
        /// Largest(t) =
        ///  (1) Real if t = Real
        ///  (2) Integer if t = Integer
        ///  (4) Natural if t = Natural
        ///  (5) PosInteger if t = PosInteger
        ///  (3) -1 if t = NegInteger
        ///  (6) c_u if t = c_l..c_u
        ///  (7) t_g if t is ground
        ///  (8) F if t = F
        ///  (9) f(Largest(t_1),...,Largest(t_n)) if t = f(...)
        ///  (10) max{ Largest(t_1), ..., Largest(t_n) } if t = t_1 + ... + t_n.
        /// 
        /// If Smallest returns a type, then this should be interpreted as the smallest element in the type.
        /// </summary>
        private Term MkLargest(Term t)
        {
            Contract.Requires(t != null && t.Owner == this && t.Groundness != Groundness.Variable);
            bool wasAdded;
            return t.Compute<Term>(
                (x, s) =>
                {
                    if (x.Groundness == Groundness.Ground)
                    {
                        return null;
                    }
                    else
                    {
                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    if (x.Groundness == Groundness.Ground ||
                        x.Symbol.Kind == SymbolKind.UserSortSymb)
                    {
                        return x;
                    }
                    else if (x.Symbol == TypeUnionSymbol)
                    {
                        using (var it = ch.GetEnumerator())
                        {
                            it.MoveNext();
                            var t1 = it.Current;
                            it.MoveNext();
                            var t2 = it.Current;
                            return GetMaxType(t1, t2);
                        }
                    }
                    else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                    {
                        var bs = (BaseSortSymb)x.Symbol;
                        switch (bs.SortKind)
                        {
                            case BaseSortKind.NegInteger:
                                return MkCnst(new Rational(-1), out wasAdded);
                            default:
                                return x;
                        }
                    }
                    else if (x.Symbol == RangeSymbol)
                    {
                        return LexicographicCompare(x.Args[0], x.Args[1]) >= 0 ? x.Args[0] : x.Args[1];
                    }
                    else if (x.Symbol.Kind == SymbolKind.ConSymb ||
                             x.Symbol.Kind == SymbolKind.MapSymb)
                    {
                        var args = new Term[x.Symbol.Arity];
                        var i = 0;
                        foreach (var c in ch)
                        {
                            args[i++] = c;
                        }

                        return MkApply(x.Symbol, args, out wasAdded);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                });
        }

        /// <summary>
        /// If t1 and t2 represent two biggest elements, then returns the larger of the two.
        /// </summary>
        /// <param name="t1"></param>
        /// <param name="t2"></param>
        /// <returns></returns>
        private Term GetMaxType(Term t1, Term t2)
        {
            var cmp = 0;
            var success = new SuccessToken();
            t1.Compute<Unit>(
                t2,
                (x, y, s) =>
                {
                    if (x == y)
                    {
                        return null;
                    }

                    cmp = x.Family - y.Family;
                    if (cmp != 0)
                    {
                        s.Failed();
                        return null;
                    }

                    switch (x.Family)
                    {
                        case Term.FamilyNumeric:
                            if (x.Symbol.Kind == SymbolKind.BaseSortSymb && y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = 1;
                            }
                            else if (y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = -1;
                            }
                            else
                            {
                                cmp = Rational.Compare((Rational)((BaseCnstSymb)x.Symbol).Raw, (Rational)((BaseCnstSymb)y.Symbol).Raw);
                            }

                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyString:
                            if (x.Symbol.Kind == SymbolKind.BaseSortSymb && y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = 1;
                            }
                            else if (y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = -1;
                            }
                            else
                            {
                                cmp = String.CompareOrdinal((string)((BaseCnstSymb)x.Symbol).Raw, (string)((BaseCnstSymb)y.Symbol).Raw);
                            }

                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyUsrCnst:
                            cmp = String.CompareOrdinal(((UserCnstSymb)x.Symbol).FullName, ((UserCnstSymb)y.Symbol).FullName);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyApp:
                            string xName, yName;
                            if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                xName = ((UserSortSymb)x.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                xName = ((UserSymbol)x.Symbol).FullName;
                            }

                            if (y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                yName = ((UserSortSymb)y.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                yName = ((UserSymbol)y.Symbol).FullName;
                            }

                            cmp = String.CompareOrdinal(xName, yName);
                            if (cmp != 0)
                            {
                                s.Failed();
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                cmp = 1;
                                return null;
                            }
                            else if (y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                cmp = -1;
                                return null;
                            }
                            else
                            {
                                return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(x.Args, y.Args);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                },
                (x, y, ch, s) =>
                {
                    return default(Unit);
                },
                success);

            return cmp >= 0 ? t1 : t2;
        }

        /// <summary>
        /// If t1 and t2 represent two smallest elements, then returns the smaller of the two.
        /// </summary>
        /// <param name="t1"></param>
        /// <param name="t2"></param>
        /// <returns></returns>
        private Term GetMinType(Term t1, Term t2)
        {
            var cmp = 0;
            var success = new SuccessToken();
            t1.Compute<Unit>(
                t2,
                (x, y, s) =>
                {
                    if (x == y)
                    {
                        return null;
                    }

                    cmp = x.Family - y.Family;
                    if (cmp != 0)
                    {
                        s.Failed();
                        return null;
                    }

                    switch (x.Family)
                    {
                        case Term.FamilyNumeric:
                            if (x.Symbol.Kind == SymbolKind.BaseSortSymb && y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = -1;
                            }
                            else if (y.Symbol.Kind == SymbolKind.BaseSortSymb)
                            {
                                cmp = 1;
                            }
                            else
                            {
                                cmp = Rational.Compare((Rational)((BaseCnstSymb)x.Symbol).Raw, (Rational)((BaseCnstSymb)y.Symbol).Raw);
                            }

                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyString:
                            cmp = String.CompareOrdinal((string)((BaseCnstSymb)x.Symbol).Raw, (string)((BaseCnstSymb)y.Symbol).Raw);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyUsrCnst:
                            cmp = String.CompareOrdinal(((UserCnstSymb)x.Symbol).FullName, ((UserCnstSymb)y.Symbol).FullName);
                            if (cmp != 0)
                            {
                                s.Failed();
                            }

                            return null;
                        case Term.FamilyApp:
                            Contract.Assert(x.Args.Length > 0 && y.Args.Length > 0);
                            string xName, yName;
                            if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                xName = ((UserSortSymb)x.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                xName = ((UserSymbol)x.Symbol).FullName;
                            }

                            if (y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                yName = ((UserSortSymb)y.Symbol).DataSymbol.FullName;
                            }
                            else
                            {
                                yName = ((UserSymbol)y.Symbol).FullName;
                            }

                            cmp = String.CompareOrdinal(xName, yName);
                            if (cmp != 0)
                            {
                                s.Failed();
                                return null;
                            }
                            else if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                cmp = -1;
                                return null;
                            }
                            else if (y.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                cmp = 1;
                                return null;
                            }
                            else
                            {
                                return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(x.Args, y.Args);
                            }
                        default:
                            throw new NotImplementedException();
                    }                
                },
                (x, y, ch, s) =>
                {
                    return default(Unit);
                },
                success);

            return cmp <= 0 ? t1 : t2;
        }

        private IEnumerable<Term> IsGrndMem_Unfold(Term ttype, Term tgrnd, bool unfoldType)
        {
            if (ttype.Groundness == Groundness.Ground)
            {
                return null;
            }

            switch (ttype.Symbol.Kind)
            {
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    if (ttype.Symbol == tgrnd.Symbol)
                    {
                        return unfoldType ? ttype.Args : tgrnd.Args;
                    }
                    else
                    {
                        return null;
                    }
                case SymbolKind.BaseSortSymb:
                case SymbolKind.UserSortSymb:
                    return null;
                case SymbolKind.UnnSymb:
                    {
                        var ct = GetCanonicalTerm((UserSymbol)ttype.Symbol, 0);
                        return unfoldType ? EnumUnnComponents(ct) : DupTerm(tgrnd, ct);
                    }
                case SymbolKind.BaseOpSymb:
                    if (ttype.Symbol == TypeUnionSymbol)
                    {
                        return unfoldType ? EnumUnnComponents(ttype) : DupTerm(tgrnd, ttype);
                    }
                    else if (ttype.Symbol == RangeSymbol)
                    {
                        return null;
                    }

                    throw new NotImplementedException();
                default:
                    throw new Impossible();
            }
        }

        private bool IsGrndMem_Fold(Term ttype, Term tgrnd, IEnumerable<bool> memberships)
        {
            if (ttype.Groundness == Groundness.Ground)
            {
                return ttype == tgrnd;
            }

            switch (ttype.Symbol.Kind)
            {
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    if (ttype.Symbol == tgrnd.Symbol)
                    {
                        return memberships.And();
                    }
                    else
                    {
                        return false;
                    }
                case SymbolKind.BaseSortSymb:
                    {
                        if (tgrnd.Symbol.Kind != SymbolKind.BaseCnstSymb)
                        {
                            return false;
                        }

                        var sort = (BaseSortSymb)ttype.Symbol;
                        var cnst = (BaseCnstSymb)tgrnd.Symbol;
                        if (sort.SortKind == BaseSortKind.Real)
                        {
                            return cnst.CnstKind == CnstKind.Numeric;
                        }
                        else if (sort.SortKind == BaseSortKind.String)
                        {
                            return cnst.CnstKind == CnstKind.String;
                        }
                        else if (cnst.CnstKind == CnstKind.String)
                        {
                            return sort.SortKind == BaseSortKind.String;
                        }

                        var rat = (Rational)cnst.Raw;
                        switch (sort.SortKind)
                        {
                            case BaseSortKind.Integer:
                                return rat.IsInteger;
                            case BaseSortKind.Natural:
                                return rat >= Rational.Zero;
                            case BaseSortKind.PosInteger:
                                return rat >= Rational.One;
                            case BaseSortKind.NegInteger:
                                return rat < Rational.Zero;
                            default:
                                throw new Impossible();
                        }
                    }
                case SymbolKind.UserSortSymb:
                    {
                        UserSortSymb usrSort = null;
                        if (tgrnd.Symbol.Kind == SymbolKind.ConSymb)
                        {
                            usrSort = ((ConSymb)tgrnd.Symbol).SortSymbol;
                        }
                        else if (tgrnd.Symbol.Kind == SymbolKind.MapSymb)
                        {
                            usrSort = ((MapSymb)tgrnd.Symbol).SortSymbol;
                        }

                        return usrSort == ttype.Symbol;
                    }
                case SymbolKind.UnnSymb:
                    return memberships.Or();
                case SymbolKind.BaseOpSymb:
                    if (ttype.Symbol == TypeUnionSymbol)
                    {
                        return memberships.Or();
                    }
                    else if (ttype.Symbol == RangeSymbol)
                    {
                        if (tgrnd.Symbol.Kind != SymbolKind.BaseCnstSymb)
                        {
                            return false;
                        }

                        var cnst = (BaseCnstSymb)tgrnd.Symbol;
                        if (cnst.CnstKind != CnstKind.Numeric)
                        {
                            return false;
                        }

                        var val = (Rational)cnst.Raw;
                        if (!val.IsInteger)
                        {
                            return false;
                        }

                        var lb = (Rational)((BaseCnstSymb)(ttype.Args[0].Symbol)).Raw;
                        var ub = (Rational)((BaseCnstSymb)(ttype.Args[1].Symbol)).Raw;
                        return lb <= val && val <= ub;
                    }

                    throw new NotImplementedException();
                default:
                    throw new Impossible();
            }
        }

        /// <summary>
        /// Enumerates the non-union components of a term.
        /// </summary>
        private IEnumerable<Term> EnumUnnComponents(Term unn)
        {
            var enm = unn.Enumerate(x => x.Symbol == TypeUnionSymbol ? x.Args : null);
            foreach (var t in enm)
            {
                if (t.Symbol != TypeUnionSymbol)
                {
                    yield return t;
                }
            }
        }

        /// <summary>
        /// Duplicates t equal to the number of non-union components in unn.
        /// </summary>
        private IEnumerable<Term> DupTerm(Term t, Term unn)
        {
            var enm = unn.Enumerate(x => x.Symbol == TypeUnionSymbol ? x.Args : null);
            foreach (var tp in enm)
            {
                if (tp.Symbol != TypeUnionSymbol)
                {
                    yield return t;
                }
            }
        }

        /// <summary>
        /// Returns the non-union components of t.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private IEnumerable<Term> GetComponents(Term t)
        {
            Contract.Requires(t != null);
            Contract.Requires(t.Symbol == TypeUnionSymbol || t.Symbol.Kind == SymbolKind.UnnSymb);
            foreach (var tp in t.Enumerate(GetComponentsHelper))
            {
                if (tp.Symbol != TypeUnionSymbol && tp.Symbol.Kind != SymbolKind.UnnSymb)
                {
                    yield return tp;
                }
            }
        }

        private IEnumerable<Term> GetComponentsHelper(Term t)
        {
            if (t.Symbol == TypeUnionSymbol)
            {
                return t.Args;
            }
            else if (t.Symbol.Kind == SymbolKind.UnnSymb)
            {
                return EnumerableMethods.GetEnumerable<Term>(GetCanonicalTerm((UserSymbol)t.Symbol, 0));
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Makes a canonical union of a set of constructor applications, all with the same outer term.
        /// </summary>
        /// <param name="components"></param>
        /// <returns></returns>
        private void MkCanUnion(UserSymbol dataConSymb, IEnumerable<Term> components, Set<Term> maxTerms, Set<Term> fullyFolded)
        {
            //// The set of all terms with apps. Mapped to a
            //// tuple indicating if the term is maximal w.r.t. the set.
            var appTerms = new Map<Term, MutableTuple<Term, bool>>(Term.Compare);

            //// A stack of app terms that need to be saturated.
            var pending = new Stack<Term>();
            var pended = new Set<Term>(Term.Compare);

            //// Let t be a term t = f(t_1,...,t_n). Then argMap(f, [...,t_{i-1},t_{i+1},...]) is the union
            //// of s s.t. t' = f(...,t_{i-1}, s, t_{i+1}, ...).
            var argMap = new Map<UserSymbol, Map<Term[], Term>>(Symbol.Compare);

            //// Step 0. Pend all the app terms.
            foreach (var c in components)
            {
                Contract.Assert(c.Symbol == dataConSymb);
                pending.Push(c);
                pended.Add(c);
                IndexForUnionRule(c, argMap);
            }

            //// Step 1. Saturate the appTerms according to the
            //// intersection and union rules
            int i, j, k, l;
            Term pTerm, t, t1, t2, t3;
            MutableTuple<Term, bool> pData, tData;
            bool wasAdded;
            while (pending.Count > 0)
            {
                pTerm = pending.Pop();
                if (appTerms.ContainsKey(pTerm))
                {
                    continue;
                }

                //// Assume the term is maximal until proven otherwise.
                pData = new MutableTuple<Term, bool>(pTerm, true);
                appTerms.Add(pTerm, pData);
                foreach (var kv in appTerms)
                {
                    t = kv.Key;
                    if (t == pTerm)
                    {
                        continue;
                    }

                    tData = kv.Value;

                    //// Apply intersection rule
                    k = -1;
                    l = -1;
                    for (i = 0; i < dataConSymb.Arity; ++i)
                    {
                        var args1 = new Term[dataConSymb.Arity];
                        var args2 = new Term[dataConSymb.Arity];
                        for (j = 0; j < dataConSymb.Arity; ++j)
                        {
                            args1[j] = i == j ? MkCanIntr(t.Args[i], pTerm.Args[i]) : t.Args[j];
                            args2[j] = i == j ? MkCanIntr(t.Args[i], pTerm.Args[i]) : pTerm.Args[j];
                            if (i == j)
                            {
                                if (args1[i] != t.Args[i])
                                {
                                    k = i;
                                }

                                if (args1[i] != pTerm.Args[i])
                                {
                                    l = i;
                                }

                                if (args1[i] == null)
                                {
                                    args1 = null;
                                    args2 = null;
                                    break;
                                }
                            }
                        }

                        if (args1 == null)
                        {
                            continue;
                        }

                        t1 = MkApply(dataConSymb, args1, out wasAdded);
                        t2 = MkApply(dataConSymb, args2, out wasAdded);
                        if (!appTerms.ContainsKey(t1) && !pended.Contains(t1))
                        {
                            pended.Add(t1);
                            pending.Push(t1);
                            IndexForUnionRule(t1, argMap);
                        }

                        if (!appTerms.ContainsKey(t2) && !pended.Contains(t2))
                        {
                            pended.Add(t2);
                            pending.Push(t2);
                            IndexForUnionRule(t2, argMap);
                        }
                    }

                    if (k < 0)
                    {
                        Contract.Assert(l >= 0);
                        tData.Item2 = false;
                    }

                    if (l < 0)
                    {
                        Contract.Assert(k >= 0);
                        pData.Item2 = false;
                    }
                }

                var unionTerms = LookupForUnionRule(pTerm, argMap);
                for (i = 0; i < unionTerms.Length; ++i)
                {
                    t3 = unionTerms[i];
                    if (!appTerms.ContainsKey(t3) && !pended.Contains(t3))
                    {
                        pended.Add(t3);
                        pending.Push(t3);
                        IndexForUnionRule(t3, argMap);
                    }
                }
            }

            //// Step 2. Check if any maximal terms need to be folded.
            //// If so, mark them as non-maximal and add their sorts to the fullyFolded set.
            var symbSort = MkApply(dataConSymb.Kind == SymbolKind.ConSymb ? ((ConSymb)dataConSymb).SortSymbol : ((MapSymb)dataConSymb).SortSymbol,
                                   EmptyArgs,
                                   out wasAdded);
            foreach (var v in appTerms.Values)
            {
                if (!v.Item2)
                {
                    continue;
                }

                j = -1;
                for (i = 0; i < dataConSymb.Arity; ++i)
                {
                    if (v.Item1.Args[i] != GetCanonicalTerm(dataConSymb, i))
                    {
                        j = i;
                        break;
                    }
                }

                if (j < 0)
                {
                    v.Item2 = false;
                    fullyFolded.Add(symbSort);
                    return;
                }
                else
                {
                    maxTerms.Add(v.Item1);
                }
            }
        }

        private Term MkCanUnion(IEnumerable<Term> components)
        {
            var appBins = new Map<UserSymbol, LinkedList<Term>>(Symbol.Compare);
            var fullyFolded = new Set<Term>(Term.Compare);
            var maxTerms = new Set<Term>((x, y) => x.LexicographicCompare(y));

            //// Step 1. Organize the terms.
            UserSymbol symb;
            LinkedList<Term> appList;
            foreach (var c in components)
            {
                if (!c.Symbol.IsDataConstructor)
                {
                    fullyFolded.Add(c);
                }
                else
                {
                    symb = (UserSymbol)c.Symbol;
                    if (!appBins.TryFindValue(symb, out appList))
                    {
                        appList = new LinkedList<Term>();
                        appBins.Add(symb, appList);
                    }

                    appList.AddLast(c);
                }
            }

            //// Step 2. Canonize each app bin.
            bool wasAdded;
            foreach (var kv in appBins)
            {
                var symbSort = MkApply(kv.Key.Kind == SymbolKind.ConSymb ? ((ConSymb)kv.Key).SortSymbol : ((MapSymb)kv.Key).SortSymbol,
                                       EmptyArgs,
                                       out wasAdded);
                if (!fullyFolded.Contains(symbSort))
                {
                    MkCanUnion(kv.Key, kv.Value, maxTerms, fullyFolded);
                }
            }

            //// Step 3. Sort the final result.
            Term canForm = null;
            if (fullyFolded.Count > 0)
            {
                canForm = new AppFreeCanUnn(fullyFolded).MkTypeTerm(this);
            }

            foreach (var mt in maxTerms)
            {
                canForm = canForm == null ? mt : MkApply(TypeUnionSymbol, new Term[] { mt, canForm }, out wasAdded);
            }

            return canForm;
        }

        private Term[] LookupForUnionRule(Term t, Map<UserSymbol, Map<Term[], Term>> argMap)
        {
            Contract.Requires(t != null && t.Symbol.IsDataConstructor);
            bool wasAdded;
            var symb = (UserSymbol)t.Symbol;
            var fArgMap = argMap[symb];
            var results = new Term[symb.Arity];
            for (int i = 0; i < symb.Arity; ++i)
            {
                var args = new Term[symb.Arity];
                var vector = new Term[symb.Arity];
                vector[0] = MkCnst(new Rational(i), out wasAdded);
                for (int j = 0; j < symb.Arity; ++j)
                {
                    if (j < i)
                    {
                        vector[j + 1] = t.Args[j];
                    }
                    else if (j > i)
                    {
                        vector[j] = t.Args[j];
                    }

                    if (j != i)
                    {
                        args[j] = t.Args[j];
                    }
                }

                args[i] = fArgMap[vector];
                results[i] = MkApply(symb, args, out wasAdded);
            }

            return results;
        }

        private void IndexForUnionRule(Term t, Map<UserSymbol, Map<Term[], Term>> argMap)
        {
            Contract.Requires(t != null && t.Symbol.IsDataConstructor);
            Map<Term[], Term> fArgMap;
            var symb = (UserSymbol)t.Symbol;
            if (!argMap.TryFindValue(symb, out fArgMap))
            {
                fArgMap = new Map<Term[], Term>((x, y) => EnumerableMethods.LexCompare(x, y, Term.Compare));
                argMap.Add(symb, fArgMap);
            }

            bool wasAdded;
            Term type, newType;
            for (int i = 0; i < symb.Arity; ++i)
            {
                var vector = new Term[symb.Arity];
                vector[0] = MkCnst(new Rational(i), out wasAdded);
                for (int j = 0; j < symb.Arity; ++j)
                {
                    if (j < i)
                    {
                        vector[j + 1] = t.Args[j];
                    }
                    else if (j > i)
                    {
                        vector[j] = t.Args[j];
                    }
                }

                if (!fArgMap.TryFindValue(vector, out type))
                {
                    fArgMap.Add(vector, t.Args[i]);
                }
                else if (type == t.Args[i])
                {
                    continue;
                }
                else 
                {
                    newType = MkCanUnn(t.Args[i], type);
                    if (newType != type)
                    {
                        fArgMap[vector] = newType;
                    }
                }
            }
        }

        /// <summary>
        /// Forms the canonical intersection of two terms, but looks in a cache first
        /// </summary>
        private Term MkCanIntr(Term t1, Term t2)
        {
            Contract.Requires(t1 != null && t2 != null);
            if (t1 == t2)
            {
                return t1;
            }

            var tp = new TermPair(t1, t2);
            Term intr;
            if (canonicalIntrs.TryFindValue(tp, out intr))
            {
                return intr;
            }
            else if (!MkIntersection(t1, t2, out intr))
            {
                canonicalIntrs.Add(tp, null);
                canonicalIntrs.Add(new TermPair(t2, t1), null);
                return null;
            }
            else
            {
                intr = MkCanonicalForm(intr);
                if (!canonicalIntrs.ContainsKey(tp))
                {
                    canonicalIntrs.Add(tp, intr);
                    canonicalIntrs.Add(new TermPair(t2, t1), intr);
                }

                return intr;
            }
        }

        /// <summary>
        /// Forms the canonical union of two terms, but looks in a cache first
        /// </summary>
        private Term MkCanUnn(Term t1, Term t2)
        {
            Contract.Requires(t1 != null && t2 != null);
            if (t1 == t2)
            {
                return t1;
            }

            bool wasAdded;
            Term unn;
            var t12 = MkApply(TypeUnionSymbol, new Term[] { t1, t2 }, out wasAdded);
            var t21 = MkApply(TypeUnionSymbol, new Term[] { t2, t1 }, out wasAdded);

            if (canonicalForms.TryFindValue(t12, out unn))
            {
                return unn;
            }
            else if (canonicalForms.TryFindValue(t21, out unn))
            {
                return unn;
            }

            unn = MkCanonicalForm(t12);
            if (!canonicalForms.ContainsKey(t21))
            {
                canonicalForms.Add(t21, unn);
            }

            return unn;
        }                     
        #endregion
        
        private void MkAnyType(SymbolTable table)
        {
            var spaceName = table.ModuleData.Source.AST.Node.NodeKind == NodeKind.Model ?
                ((Model)table.ModuleData.Source.AST.Node).Domain.Name :
                table.ModuleSpace.FullName;

            UserSymbol other;
            var anySymb = table.Resolve(spaceName + "." + API.ASTQueries.ASTSchema.Instance.TypeNameAny, out other);
            Contract.Assert(anySymb != null && other == null);
            CanonicalAnyType = GetCanonicalTerm(anySymb, 0);
            Contract.Assert(CanonicalAnyType != null);

            if (table.ModuleData.Source.AST.Node.NodeKind == NodeKind.Model)
            {
                bool wasAdded;
                var mangledRequires = SymbolTable.ManglePrefix + SymbolTable.RequiresName;
                var mangledEnsures = SymbolTable.ManglePrefix + SymbolTable.EnsuresName;
                foreach (var us in table.Root.DescendantSymbols)
                {
                    if (!us.IsDerivedConstant)
                    {
                        continue;
                    }

                    if (us.Name == SymbolTable.RequiresName ||
                        us.Name == SymbolTable.EnsuresName ||
                        us.Name.StartsWith(mangledRequires) ||
                        us.Name.StartsWith(mangledEnsures))
                    {
                        CanonicalAnyType = MkApply(
                            TypeUnionSymbol,
                            new Term[] { MkApply(us, EmptyArgs, out wasAdded), CanonicalAnyType },
                            out wasAdded);
                    }
                }
            }
        }

        private static int Compare(Tuple<UserSymbol, int> p1, Tuple<UserSymbol, int> p2)
        {
            if (p1.Item1 != p2.Item1)
            {
                return UserSymbol.Compare(p1.Item1, p2.Item1);
            }

            return p1.Item2 - p2.Item2;
        }

        private static int BinCompare(Term t1, Term t2)
        {
            long cmp;
            for (int i = 0; i < t1.Args.Length; ++i)
            {
                cmp = t1.Args[i].UId - t2.Args[i].UId;
                if (cmp != 0)
                {
                    return cmp < 0 ? -1 : 1;
                }
            }

            return 0;
        }

        private static int UseCompare(Tuple<UserSymbol, int> u1, Tuple<UserSymbol, int> u2)
        {
            if (u1.Item1.Id != u2.Item1.Id)
            {
                return u1.Item1.Id < u2.Item1.Id ? -1 : 1;
            }

            if (u1.Item2 != u2.Item2)
            {
                return u1.Item2 < u2.Item2 ? -1 : 1;
            }

            return 0;
        }

        internal static void Debug_PrintSmallTerm(Term t, System.IO.TextWriter wr)
        {
            Contract.Requires(t != null);
            wr.Write(t.Symbol.PrintableName);
            if (t.Symbol.Arity == 0)
            {
                return;
            }

            wr.Write("(");
            for (int i = 0; i < t.Args.Length; ++i)
            {
                Debug_PrintSmallTerm(t.Args[i], wr);
                if (i < t.Args.Length - 1)
                {
                    wr.Write(", ");
                }
            }

            wr.Write(")");
        }

        private class TermPair
        {
            public Term T1
            {
                get;
                private set;
            }

            public Term T2
            {
                get;
                private set;
            }

            public TermPair(Term t1, Term t2)
            {
                T1 = t1;
                T2 = t2;
            }

            public static int Compare(TermPair k1, TermPair k2)
            {
                Contract.Requires(k1 != null && k2 != null);

                if (k1.T1 != k2.T1)
                {
                    return Term.Compare(k1.T1, k2.T1);
                }

                return Term.Compare(k1.T2, k2.T2);
            }
        }
    }
}
