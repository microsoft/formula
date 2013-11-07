namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    using API;
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
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3RealExpr = Microsoft.Z3.RealExpr;
    using Z3IntNum = Microsoft.Z3.IntNum;

    /// <summary>
    /// Represents a range of integers [l, u] as a bitvector bv where bv + l is the true value.
    /// The range must be of the form: Exists p > 0.  |u - l + 1| = 2^p.
    /// </summary>
    internal class IntRangeEmbedding : ITypeEmbedding
    {
        private const string BoxingName = "BoxBV2Rng{0}_{1}";
        private const string UnboxingName = "UnboxRng2BV{0}_{1}";
        private const string TesterName = "IsRng{0}_{1}";
        private const string SortName = "Rng{0}_{1}";

        private Z3Con boxingCon;
        private Z3BVSort bvSort;
        private Z3IntExpr z3Lower;

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.IntRange;
            }
        }

        public BigInteger Lower
        {
            get;
            private set;
        }

        public BigInteger Upper
        {
            get;
            private set;
        }

        public Z3Fun BoxingFun
        {
            get;
            private set;
        }

        public Z3Fun UnboxingFun
        {
            get;
            private set;
        }

        public Z3Fun TesterFun
        {
            get;
            private set;
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

        public Tuple<Term, Z3Expr> DefaultMember
        {
            get;
            private set;
        }

        private Z3Context Context
        {
            get { return Owner.Context; }
        }

        private TermIndex Index
        {
            get { return Owner.Index; }
        }

        public IntRangeEmbedding(TypeEmbedder owner, BigInteger lower, BigInteger upper)
        {
            Contract.Requires(owner != null);
            Contract.Requires(lower <= upper);
            Owner = owner;
            Lower = lower;
            Upper = upper;
            var width = upper - lower + 1;
            Contract.Assert(width > 1 && width.IsPowerOfTwo);
            bvSort = Context.MkBitVecSort(width.MostSignificantOne());

            bool wasAdded;
            Type = Index.MkApply(Index.RangeSymbol,
                                 new Term[]
                                 {
                                     Index.MkCnst(new Rational(lower, BigInteger.One), out wasAdded),
                                     Index.MkCnst(new Rational(upper, BigInteger.One), out wasAdded)
                                 },
                                 out wasAdded);

            boxingCon = Context.MkConstructor(
                string.Format(BoxingName, lower, upper),
                string.Format(TesterName, lower, upper), 
                new string[] { string.Format(UnboxingName, lower, upper) }, 
                new Z3Sort[] { bvSort });

            Representation = Context.MkDatatypeSort(string.Format(SortName, lower, upper), new Z3Con[] { boxingCon });
            BoxingFun = boxingCon.ConstructorDecl;
            UnboxingFun = boxingCon.AccessorDecls[0];
            TesterFun = boxingCon.TesterDecl;
            DefaultMember = new Tuple<Term, Z3Expr>(
                Index.MkCnst(new Rational(lower, BigInteger.One), out wasAdded),
                BoxingFun.Apply(Context.MkBV(0, bvSort.Size)));

            z3Lower = Context.MkInt(Lower.ToString());
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            var bt = (Z3BVExpr)UnboxingFun.Apply(t);

            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }

            Z3BoolExpr test = null;
            foreach (var i in unn.RangeMembers)
            {
                var istart = Context.MkBV((i.Key - Lower).ToString(), bvSort.Size);
                var iend = Context.MkBV((i.Value - Lower).ToString(), bvSort.Size);
                test = test.Or(Context, bt.UGe(Context, istart).And(Context, bt.ULe(Context, iend)));
            }

            return test;
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
                case TypeEmbeddingKind.Real:
                    return MkCoercion(t, unn, (RealEmbedding)srcTE);
                case TypeEmbeddingKind.Natural:
                    return MkCoercion(t, unn, (NaturalEmbedding)srcTE);
                case TypeEmbeddingKind.Integer:
                    return MkCoercion(t, unn, (IntegerEmbedding)srcTE);
                case TypeEmbeddingKind.PosInteger:
                    return MkCoercion(t, unn, (PosIntegerEmbedding)srcTE);
                case TypeEmbeddingKind.NegInteger:
                    return MkCoercion(t, unn, (NegIntegerEmbedding)srcTE);
                case TypeEmbeddingKind.IntRange:
                    return MkCoercion(t, unn, (IntRangeEmbedding)srcTE);
                case TypeEmbeddingKind.Singleton:
                    return MkCoercion(t, unn, (SingletonEmbedding)srcTE);
                case TypeEmbeddingKind.Union:
                    return MkCoercion(t, unn, (UnionEmbedding)srcTE);
                default:
                    throw new NotImplementedException();
            }
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb != null && symb.Kind == SymbolKind.BaseCnstSymb);
            Contract.Assert(args == null || args.Length == 0);
            var bc = (BaseCnstSymb)symb;
            Contract.Assert(bc.CnstKind == CnstKind.Numeric);
            var r = (Rational)bc.Raw;
            Contract.Assert(r.IsInteger);
            Contract.Assert(Lower <= r.Numerator && r.Numerator <= Upper);
            return BoxingFun.Apply(Context.MkBV((r.Numerator - Lower).ToString(), bvSort.Size));
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(t.FuncDecl.Equals(BoxingFun));
            var i = (Z3BVNum)t.Args[0];
            bool wasAdded;
            return Index.MkCnst(new Rational(i.BigInteger + Lower, System.Numerics.BigInteger.One), out wasAdded);
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (!t.FuncDecl.Equals(BoxingFun) || !t.Args[0].IsBVNumeral)
            {
                return Type;
            }
            else
            {
                bool wasAdded;
                var val = MkGround(t, null);
                return Index.MkApply(Index.RangeSymbol, new Term[] { val, val }, out wasAdded);
            }
        }

        public Z3IntExpr MkIntCoercion(Z3Expr rng)
        {
            Contract.Requires(rng.Sort.Equals(Representation));
            var i = (Z3BVExpr)UnboxingFun.Apply(rng);
            var c = (Z3IntExpr)z3Lower.Add(Context, i.BV2Int(Context));
            return c;
        }

        public void Debug_Print()
        {
            Console.WriteLine(
                "Range {0}..{1} with bv width {2}", 
                Lower,
                Upper,
                bvSort.Size);

            Console.WriteLine(
                "Boxing fun: {0}, Unboxing fun: {1}, sort: {2}",
                BoxingFun.Name,
                UnboxingFun.Name,
                Representation.Name);

            Console.WriteLine();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntegerEmbedding te)
        {
            return BoxingFun.Apply(  
                ((Z3IntExpr)Context.MkSub((Z3IntExpr)t, Context.MkInt(Lower.ToString()))).Int2BV(Context, bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NaturalEmbedding te)
        {
            return BoxingFun.Apply(
                ((Z3IntExpr)Context.MkSub(te.MkIntCoercion(t), Context.MkInt(Lower.ToString()))).Int2BV(Context, bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, PosIntegerEmbedding te)
        {
            return BoxingFun.Apply(
                ((Z3IntExpr)Context.MkSub(te.MkIntCoercion(t), Context.MkInt(Lower.ToString()))).Int2BV(Context, bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NegIntegerEmbedding te)
        {
            return BoxingFun.Apply(
                ((Z3IntExpr)Context.MkSub(te.MkIntCoercion(t), Context.MkInt(Lower.ToString()))).Int2BV(Context, bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntRangeEmbedding te)
        {
            Z3BVExpr translate;
            var unbox = (Z3BVExpr)te.UnboxingFun.Apply(t);
            if (te.Lower <= Lower)
            {
                translate = unbox.BVSub(Context, Context.MkBV((Lower - te.Lower).ToString(), unbox.SortSize)).FitBV(Context, bvSort.Size);
            }
            else
            {
                translate = unbox.FitBV(Context, bvSort.Size).BVAdd(Context, Context.MkBV((te.Lower - Lower).ToString(), bvSort.Size)); 
            }

            return BoxingFun.Apply(translate);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, RealEmbedding te)
        {
            return BoxingFun.Apply(
                ((Z3IntExpr)Context.MkSub(Context.MkReal2Int((Z3RealExpr)t), Context.MkInt(Lower.ToString()))).Int2BV(Context, bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, SingletonEmbedding te)
        {
            var val = ((Rational)((BaseCnstSymb)te.Value.Symbol).Raw).Numerator - Lower;
            return BoxingFun.Apply(Context.MkBV(val.ToString(), bvSort.Size));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, UnionEmbedding te)
        {
            Z3Expr unbox;
            //// If the union contains real, then it cannot contain other integral numerics.
            var test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), t, out unbox);
            if (test != null)
            {
                return test.Ite(
                    Context, 
                    MkCoercion(unbox, unn, (RealEmbedding)Owner.GetEmbedding(BaseSortKind.Real)), 
                    DefaultMember.Item2);
            }

            //// If the union contains int, then it cannot contain other integral numerics.
            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), t, out unbox);
            if (test != null)
            {
                return test.Ite(
                    Context,
                    MkCoercion(unbox, unn, (IntegerEmbedding)Owner.GetEmbedding(BaseSortKind.Integer)),
                    DefaultMember.Item2);
            }

            //// Otherwise unn may contain subsorts of Integer or integer ranges.
            //// First handle the base subsorts
            var coercions = DefaultMember.Item2;

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                    Context,
                    MkCoercion(unbox, unn, (NaturalEmbedding)Owner.GetEmbedding(BaseSortKind.Natural)),
                    coercions);
            }

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                     Context,
                     MkCoercion(unbox, unn, (PosIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.PosInteger)),
                     coercions);
            }

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                     Context,
                     MkCoercion(unbox, unn, (NegIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.NegInteger)),
                     coercions);
            }

            //// Additionally, there may be integer ranges.
            Z3BoolExpr[] tests;
            Z3Expr[] unboxeds;
            ITypeEmbedding tep;
            foreach (var kv in unn.RangeMembers)
            {
                tests = te.MkTestAndUnbox(kv.Key, kv.Value, t, out unboxeds);
                for (int i = 0; i < tests.Length; ++i)
                {
                    test = tests[i];
                    unbox = unboxeds[i];
                    tep = Owner.GetEmbedding(unbox.Sort);
                    switch (tep.Kind)
                    {
                        case TypeEmbeddingKind.IntRange:
                            coercions = test.Ite(
                                 Context,
                                 MkCoercion(unbox, unn, (IntRangeEmbedding)tep),
                                 coercions);
                            break;
                        case TypeEmbeddingKind.Singleton:
                            coercions = test.Ite(
                                 Context,
                                 MkCoercion(unbox, unn, (SingletonEmbedding)tep),
                                 coercions);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return coercions;
        }
    }
}
