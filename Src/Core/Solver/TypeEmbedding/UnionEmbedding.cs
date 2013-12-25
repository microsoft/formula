namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3BVSort = Microsoft.Z3.BitVecSort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3BVExpr = Microsoft.Z3.BitVecExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3BVNum = Microsoft.Z3.BitVecNum;
    using Z3Con = Microsoft.Z3.Constructor;
    using Z3Fun = Microsoft.Z3.FuncDecl;

    /// <summary>
    /// Represents a set of non-integral constants be mapping bit vectors to constants.
    /// </summary>
    internal class UnionEmbedding : ITypeEmbedding
    {
        private const uint BaseEncodingCost = 5;

        private uint encodingCost = 0;
        private Set<Z3Fun> allBoxers = new Set<Z3Fun>((x, y) => ((int)x.Id - (int)y.Id));
        private Map<Term, Z3Con> rngBoxings = new Map<Term, Z3Con>(Term.Compare);
        private Map<Symbol, Z3Con> otherBoxings = new Map<Symbol, Z3Con>(Symbol.Compare);
        private Map<Z3Sort, Z3Con> sortToBoxing = new Map<Z3Sort, Z3Con>((x, y) => ((int)x.Id) - ((int)y.Id));

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.Union;
            }
        }

        public TypeEmbedder Owner
        {
            get;
            private set;
        }

        public Z3Sort Representation
        {
            get;
            private set;
        }

        public Term Type
        {
            get;
            private set;
        }

        public AppFreeCanUnn CanonicalUnion
        {
            get;
            private set;
        }

        public Tuple<Term, Z3Expr> DefaultMember
        {
            get;
            private set;
        }

        public Z3Con[] Boxers
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public uint EncodingCost
        {
            get
            {
                if (encodingCost != 0)
                {
                    return encodingCost;
                }

                uint maxCompCost = 0;
                foreach (var box in allBoxers)
                {
                    maxCompCost = Math.Max(maxCompCost, Owner.GetEmbedding(box.Domain[0]).EncodingCost);
                }

                //// Finally, account for the number of bits required to label the components of this union.
                encodingCost = maxCompCost + BaseEncodingCost + (uint)(Math.Ceiling(Math.Log(allBoxers.Count, 2)));
                return encodingCost;
            }
        }

        private Z3Context Context
        {
            get { return Owner.Context; }
        }

        private TermIndex Index
        {
            get { return Owner.Index; }
        }

        public UnionEmbedding(TypeEmbedder owner, Term unnType, Map<Term, Tuple<uint, UserSymbol>> sortIndices)
        {
            Contract.Requires(owner != null);
            Owner = owner;
            Name = string.Format("Unn_{0}", sortIndices[unnType].Item1);
            Type = unnType;
            CanonicalUnion = new AppFreeCanUnn(unnType);
            var unn = CanonicalUnion;

            Z3Con con;
            LinkedList<ITypeEmbedding> intFacts, cnstFacts;
            LinkedList<Z3Con> allConstructors = new LinkedList<Z3Con>();
            owner.GetFactorizations(unnType, out intFacts, out cnstFacts);
            foreach (var te in intFacts)
            {
                rngBoxings.Add(
                    te.Type,
                    con = Context.MkConstructor(
                        string.Format("Box_{0}_{1}", Name, te.Representation.Name),
                        string.Format("IsBox_{0}_{1}", Name, te.Representation.Name),
                        new string[] { string.Format("Unbox_{0}_{1}", Name, te.Representation.Name) },
                        new Z3Sort[] { te.Representation }));
                allConstructors.AddLast(con);
            }

            foreach (var te in cnstFacts)
            {
                con = Context.MkConstructor(
                        string.Format("Box_{0}_{1}", Name, te.Representation.Name),
                        string.Format("IsBox_{0}_{1}", Name, te.Representation.Name),
                        new string[] { string.Format("Unbox_{0}_{1}", Name, te.Representation.Name) },
                        new Z3Sort[] { te.Representation });
                allConstructors.AddLast(con);

                foreach (var t in te.Type.Enumerate(x => x.Args))
                {
                    if (t.Symbol.Arity == 0)
                    {
                        otherBoxings.Add(t.Symbol, con);
                    }
                }
            }

            bool wasAdded;
            foreach (var m in unn.NonRangeMembers)
            {
                if (m.Kind == SymbolKind.BaseSortSymb)
                {
                    var te = Owner.GetEmbedding(Index.MkApply(m, TermIndex.EmptyArgs, out wasAdded));
                    otherBoxings.Add(
                        m,
                        con = Context.MkConstructor(
                            string.Format("Box_{0}_{1}", Name, te.Representation.Name),
                            string.Format("IsBox_{0}_{1}", Name, te.Representation.Name),
                            new string[] { string.Format("Unbox_{0}_{1}", Name, te.Representation.Name) },
                            new Z3Sort[] { te.Representation }));
                    allConstructors.AddLast(con);
                }
                else if (m.Kind == SymbolKind.UserSortSymb)
                {
                    otherBoxings.Add(
                        m,
                        con = Context.MkConstructor(
                            string.Format("Box_{0}_{1}", Name, ((UserSortSymb)m).DataSymbol.FullName),
                            string.Format("IsBox_{0}_{1}", Name, ((UserSortSymb)m).DataSymbol.FullName),
                            new string[] { string.Format("Unbox_{0}_{1}", Name, ((UserSortSymb)m).DataSymbol.FullName) },
                            new Z3Sort[] { null },
                            new uint[] { sortIndices[Index.MkApply(m, TermIndex.EmptyArgs, out wasAdded)].Item1 }));
                    allConstructors.AddLast(con);
                }
            }

            Boxers = allConstructors.ToArray(allConstructors.Count);
        }

        public void SetRepresentation(Z3Sort sort)
        {
            Contract.Requires(Representation == null);
            Representation = sort;

            foreach (var kv in rngBoxings)
            {
                sortToBoxing.Add(kv.Value.AccessorDecls[0].Range, kv.Value);
                allBoxers.Add(kv.Value.ConstructorDecl);
            }

            foreach (var kv in otherBoxings)
            {
                if (!sortToBoxing.ContainsKey(kv.Value.AccessorDecls[0].Range))
                {
                    sortToBoxing.Add(kv.Value.AccessorDecls[0].Range, kv.Value);
                    allBoxers.Add(kv.Value.ConstructorDecl);
                }
            }
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            ///// Produces a test of the form.
            ////  ite(isBox_T(t), Test_T(t), ...).

            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }

            bool wasAdded;
            Z3Con boxCon;
            Z3Fun unbox, tester;
            Z3BoolExpr boxTest;
            ITypeEmbedding te;
            Term subtypeTrm;
            MutableTuple<Z3Con, Term> subtype;
            Z3BoolExpr iteTest = Context.MkFalse();

            //// For non-user sorts, want to project the type along the boxed type, so the boxed type
            //// can decide how to encode the test of this projection. 
            Map<Z3Sort, MutableTuple<Z3Con, Term>> subtypes = 
                new Map<Z3Sort, MutableTuple<Z3Con, Term>>((x, y) => ((int)x.Id) - ((int)y.Id)); 
            
            foreach (var s in unn.NonRangeMembers)
            {
                boxCon = GetBoxer(s);
                Contract.Assert(boxCon != null);
                unbox = boxCon.AccessorDecls[0];

                if (s.Kind == SymbolKind.UserSortSymb)
                {
                    tester = boxCon.TesterDecl;
                    te = Owner.GetEmbedding(unbox.Range);
                    boxTest = (Z3BoolExpr)tester.Apply(t);
                    subtypeTrm = Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded);
                    iteTest = (Z3BoolExpr)boxTest.Ite(Context, te.MkTest(unbox.Apply(t), subtypeTrm), iteTest);
                }
                else
                {
                    if (!subtypes.TryFindValue(unbox.Range, out subtype))
                    {
                        subtype = new MutableTuple<Z3Con,Term>(
                                    boxCon,
                                    Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded));
                        subtypes.Add(unbox.Range, subtype);
                    }
                    else
                    {
                        subtype.Item2 = Index.MkApply(
                                    Index.TypeUnionSymbol, 
                                    new Term[] { Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded), subtype.Item2 }, 
                                    out wasAdded);
                    }
                }
            }

            foreach (var kv in unn.RangeMembers)
            {
                subtypeTrm = Index.MkApply(
                               Index.RangeSymbol,
                               new Term[] 
                               { 
                                   Index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                   Index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded)
                               },
                               out wasAdded);

                foreach (var bc in GetBoxers(kv.Key, kv.Value))
                {
                    unbox = bc.AccessorDecls[0];
                    if (!subtypes.TryFindValue(unbox.Range, out subtype))
                    {
                        subtype = new MutableTuple<Z3Con, Term>(bc, subtypeTrm);
                        subtypes.Add(unbox.Range, subtype);
                    }
                    else
                    {
                        subtype.Item2 = Index.MkApply(
                                    Index.TypeUnionSymbol,
                                    new Term[] { subtypeTrm, subtype.Item2 },
                                    out wasAdded);
                    }
                }
            }

            foreach (var kv in subtypes)
            {
                boxCon = kv.Value.Item1;
                Contract.Assert(boxCon != null);
                unbox = boxCon.AccessorDecls[0];

                tester = boxCon.TesterDecl;
                te = Owner.GetEmbedding(unbox.Range);
                boxTest = (Z3BoolExpr)tester.Apply(t);
                iteTest = (Z3BoolExpr)boxTest.Ite(Context, te.MkTest(unbox.Apply(t), kv.Value.Item2), iteTest);
            }

            return iteTest;
        }

        /// <summary>
        /// Gets the type embedding that is boxed by this union and contains
        /// values constructed by symbol s.
        /// </summary>
        public ITypeEmbedding GetUnboxedEmbedding(Symbol s)
        {
            Z3Con boxer;
            if (s.Kind == SymbolKind.ConSymb)
            {
                boxer = GetBoxer(((ConSymb)s).SortSymbol);
            }
            else if (s.Kind == SymbolKind.MapSymb)
            {
                boxer = GetBoxer(((MapSymb)s).SortSymbol);
            }
            else
            {
                boxer = GetBoxer(s);
            }

            Contract.Assert(boxer != null);
            return Owner.GetEmbedding(boxer.AccessorDecls[0].Range);
        }

        public Z3Expr MkCoercion(Z3Expr t)
        {
            var srcTE = Owner.GetEmbedding(t.Sort);
            if (srcTE == this)
            {
                return t;
            }

            Term intr;
            var unn = Owner.GetIntersection(srcTE.GetSubtype(t), Type, out intr);
            if (unn == null)
            {
                return DefaultMember.Item2;
            }

            switch (srcTE.Kind)
            {
                case TypeEmbeddingKind.Enum:
                case TypeEmbeddingKind.Natural:
                case TypeEmbeddingKind.Integer:
                case TypeEmbeddingKind.PosInteger:
                case TypeEmbeddingKind.NegInteger:
                case TypeEmbeddingKind.IntRange:
                case TypeEmbeddingKind.Real:
                case TypeEmbeddingKind.String:
                case TypeEmbeddingKind.Singleton:
                case TypeEmbeddingKind.Constructor:
                case TypeEmbeddingKind.Union:
                    return MkCoercion(t, unn, srcTE);
                default:
                    throw new NotImplementedException();
            }
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb == null && args != null && args.Length == 1);
            var te = Owner.GetEmbedding(args[0].FuncDecl.Range);
            var box = sortToBoxing[te.Representation];
            return box.ConstructorDecl.Apply(args[0]);
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null && t.FuncDecl.Range == Representation);
            Contract.Assert(args != null && args.Length == 1);
            return args[0];
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (!allBoxers.Contains(t.FuncDecl))
            {
                return Type;
            }

            return Owner.GetEmbedding(t.FuncDecl.Domain[0]).GetSubtype(t.Args[0]);
        }

        public void Debug_Print()
        {
            Console.WriteLine("Union embedding {0}", Name);
            Console.WriteLine("Sort: {0}", Representation.Name);

            foreach (var kv in sortToBoxing)
            {
                Console.WriteLine(
                    "\t{0}({1} : {2})",
                    kv.Value.ConstructorDecl.Name,
                    kv.Value.AccessorDecls[0].Name,
                    kv.Value.AccessorDecls[0].Range.Name);
            }
        }

        /// <summary>
        /// If the symbol s identifies a boxed type:
        /// Returns a type test that is true if t is boxed inside of s.
        /// Returns a unbox expression that removes the boxing of t.
        /// 
        /// Otherwise returns null.
        /// </summary>
        internal Z3BoolExpr MkTestAndUnbox(Symbol s, Z3Expr t, out Z3Expr unboxed)
        {
            Contract.Requires(s != null && t != null);
            Contract.Requires(t.Sort == Representation);
            var boxer = GetBoxer(s);
            if (boxer == null)
            {
                unboxed = null;
                return null;
            }

            unboxed = boxer.AccessorDecls[0].Apply(t);
            return (Z3BoolExpr)boxer.TesterDecl.Apply(t);
        }

        /// <summary>
        /// Returns any array of testers and unboxers for all IntRanges that intersect with [lower, upper]
        /// </summary>
        internal Z3BoolExpr[] MkTestAndUnbox(BigInteger lower, BigInteger upper, Z3Expr t, out Z3Expr[] unboxed)
        {
            Contract.Requires(t != null && t.Sort == Representation);
            var testList = new LinkedList<Z3BoolExpr>();
            var unboxedList = new LinkedList<Z3Expr>();
            foreach (var boxer in GetBoxers(lower, upper))
            {
                testList.AddLast((Z3BoolExpr)boxer.TesterDecl.Apply(t));
                unboxedList.AddLast(boxer.AccessorDecls[0].Apply(t));                
            }

            unboxed = unboxedList.ToArray(unboxedList.Count);
            return testList.ToArray(testList.Count);
        }

        /// <summary>
        /// The default value is F applied to args. If args[i] is null, then a constant
        /// can be choosen for arg[i]
        /// </summary>
        internal void SetDefaultMember(Term[] args)
        {
            Contract.Requires(args != null && args.Length == 1);
            Contract.Requires(DefaultMember == null);

            if (args[0] == null)
            {
                args[0] = Owner.GetSomeConstant(CanonicalUnion);
            }

            Contract.Assert(Index.IsGroundMember(Type, args[0]));
            DefaultMember = new Tuple<Term, Z3Expr>(args[0], Owner.MkGround(args[0], this));
        }

        /// <summary>
        /// Gets the boxer for the type represented by the symbol s.
        /// </summary>
        private Z3Con GetBoxer(Symbol s)
        {
            switch (s.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                case SymbolKind.UserCnstSymb:
                    return GetCnstBoxer(s);
                case SymbolKind.UserSortSymb:
                    return GetUsrSortBoxer((UserSortSymb)s);
                case SymbolKind.BaseSortSymb:
                    return GetBaseSortBoxer((BaseSortSymb)s);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns all the boxing constructors that may contain a part of this interval.
        /// </summary>
        private IEnumerable<Z3Con> GetBoxers(BigInteger lower, BigInteger upper)
        {
            Z3Con numBox;
            if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), out numBox))
            {
                yield return numBox;
                yield break;
            }
            else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), out numBox))
            {
                yield return numBox;
                yield break;
            }

            if (lower.Sign < 0 && otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), out numBox))
            {
                yield return numBox;
            }

            if (upper.Sign >= 0 && otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), out numBox))
            {
                yield return numBox;
            }
            else if (upper.Sign > 0 && otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), out numBox))
            {
                yield return numBox;
            }

            bool wasAdded;
            var rng = Index.MkApply(
                           Index.RangeSymbol,
                           new Term[] 
                               { 
                                   Index.MkCnst(new Rational(lower, BigInteger.One), out wasAdded),
                                   Index.MkCnst(new Rational(upper, BigInteger.One), out wasAdded)
                               },
                           out wasAdded);
            Z3Con rngBoxer;
            if (rngBoxings.TryFindValue(rng, out rngBoxer))
            {
                yield return rngBoxer;
                yield break;
            }

            BigInteger rlower, rupper;
            foreach (var kv in rngBoxings)
            {
                rlower = ((Rational)((BaseCnstSymb)kv.Key.Args[0].Symbol).Raw).Numerator;
                rupper = ((Rational)((BaseCnstSymb)kv.Key.Args[1].Symbol).Raw).Numerator;
                if (BigInteger.Max(rlower, lower) <= BigInteger.Min(upper, rupper))
                {
                    yield return kv.Value;
                }
            }
        }

        private Z3Con GetBaseSortBoxer(BaseSortSymb s)
        {
            Z3Con boxer;
            if (otherBoxings.TryFindValue(s, out boxer))
            {
                return boxer;
            }

            switch (s.SortKind)
            {
                case BaseSortKind.String:
                    return null;
                case BaseSortKind.Integer:
                    if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), out boxer))
                    {
                        return boxer;
                    }

                    return null;
                case BaseSortKind.Natural:
                case BaseSortKind.NegInteger:
                    if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), out boxer))
                    {
                        return boxer;
                    }
                    else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), out boxer))
                    {
                        return boxer;
                    }

                    return null;
                case BaseSortKind.PosInteger:
                    if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), out boxer))
                    {
                        return boxer;
                    }
                    else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), out boxer))
                    {
                        return boxer;
                    }
                    else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), out boxer))
                    {
                        return boxer;
                    }

                    return null;
                case BaseSortKind.Real:
                    //// Cannot be boxed by anything else.
                    return null;
                default:
                    throw new NotImplementedException();
            }
        }

        private Z3Con GetUsrSortBoxer(UserSortSymb s)
        {
            Z3Con boxer;
            if (!otherBoxings.TryFindValue(s, out boxer))
            {
                return null;
            }
            else
            {
                return boxer;
            }
        }

        /// <summary>
        /// Get the boxer for the sort containing the constant s.
        /// </summary>
        private Z3Con GetCnstBoxer(Symbol s)
        {
            Contract.Requires(s != null && s.IsNonVarConstant);
            Z3Con boxing;
            switch (s.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                    {
                        var bc = (BaseCnstSymb)s;
                        if (bc.CnstKind == CnstKind.Numeric)
                        {
                            var r = (Rational)bc.Raw;
                            //// Integers should only come from ranges
                            if (r.IsInteger)
                            {
                                using (var it = GetBoxers(r.Numerator, r.Numerator).GetEnumerator())
                                {
                                    if (it.MoveNext())
                                    {
                                        return it.Current;
                                    }
                                    else
                                    {
                                        return null;
                                    }
                                }
                            }

                            //// r must be in other symbol or in Real.
                            if (otherBoxings.TryFindValue(s, out boxing))
                            {
                                return boxing;
                            }
                            else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), out boxing))
                            {
                                return boxing;
                            }
                            else
                            {
                                return null;
                            }
                        }
                        else if (bc.CnstKind == CnstKind.String)
                        {
                            if (otherBoxings.TryFindValue(s, out boxing))
                            {
                                return boxing;
                            }
                            else if (otherBoxings.TryFindValue(Index.SymbolTable.GetSortSymbol(BaseSortKind.String), out boxing))
                            {
                                return boxing;
                            }
                            else
                            {
                                return null;
                            }
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    }

                case SymbolKind.UserCnstSymb:
                    {
                        if (otherBoxings.TryFindValue(s, out boxing))
                        {
                            return boxing;
                        }
                        else
                        {
                            return null;
                        }
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, ITypeEmbedding te)
        {
            var coercions = DefaultMember.Item2;
            bool wasAdded;
            ITypeEmbedding boxedTE;
            foreach (var s in unn.NonRangeMembers)
            {
                var boxer = GetBoxer(s);
                Contract.Assert(s != null);
                boxedTE = Owner.GetEmbedding(boxer.AccessorDecls[0].Range);
                coercions = te.MkTest(t, Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded)).Ite(
                               Context,
                               boxer.ConstructorDecl.Apply(boxedTE.MkCoercion(t)),
                               coercions);
            }

            Term rngType;
            foreach (var kv in unn.RangeMembers)
            {
                foreach (var boxer in GetBoxers(kv.Key, kv.Value))
                {
                    boxedTE = Owner.GetEmbedding(boxer.AccessorDecls[0].Range);
                    rngType = Index.MkApply(
                                    Index.RangeSymbol,
                                    new Term[] 
                                    { 
                                        Index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                        Index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded)
                                    },
                                    out wasAdded);

                    coercions = te.MkTest(t, rngType).Ite(
                                   Context,
                                   boxer.ConstructorDecl.Apply(boxedTE.MkCoercion(t)),
                                   coercions);
                }
            }

            Contract.Assert(coercions != null);
            return coercions;
        }
    }
}