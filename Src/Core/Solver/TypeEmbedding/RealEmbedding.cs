namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using API;
    using Common;
    using Common.Extras;
    using Common.Terms;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3RealExpr = Microsoft.Z3.RealExpr;
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3RatNum = Microsoft.Z3.RatNum;
     
    internal class RealEmbedding : ITypeEmbedding
    {
        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.Real;
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

        public Tuple<Term, Z3Expr> DefaultMember
        {
            get;
            private set;
        }

        public uint EncodingCost
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

        public RealEmbedding(TypeEmbedder owner, uint cost)
        {
            Contract.Requires(owner != null);
            Owner = owner;
            Representation = Context.MkRealSort();
            EncodingCost = cost;
            bool wasAdded;
            Type = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), TermIndex.EmptyArgs, out wasAdded);
            DefaultMember = new Tuple<Term, Z3Expr>(Index.MkCnst(Rational.Zero, out wasAdded), Context.MkNumeral(0, Representation));
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            var rt = (Z3RealExpr)t;

            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }

            Z3BoolExpr test = null;
            if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer)))
            {
                test = test.Or(Context, Context.MkIsInteger(rt));
            }
            else
            {
                if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural)))
                {
                    test = test.Or(Context, Context.MkIsInteger(rt).And(Context, rt.Ge(Context, Context.MkReal(0))));
                }
                else if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger)))
                {
                    test = test.Or(Context, Context.MkIsInteger(rt).And(Context, rt.Ge(Context, Context.MkReal(1))));
                }

                if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger)))
                {
                    test = test.Or(Context, Context.MkIsInteger(rt).And(Context, rt.Lt(Context, Context.MkReal(0))));
                }
            }

            Rational r;
            foreach (var e in unn.NonRangeMembers)
            {
                if (e.Kind != SymbolKind.BaseCnstSymb)
                {
                    continue;
                }

                r = (Rational)((BaseCnstSymb)e).Raw;
                test = test.Or(Context, rt.Eq(Context, Context.MkReal(string.Format("{0}/{1}", r.Numerator, r.Denominator))));
            }

            foreach (var i in unn.RangeMembers)
            {
                test = test.Or(Context, rt.Ge(Context, Context.MkReal(i.Key.ToString())).
                                          And(Context, rt.Le(Context, Context.MkReal(i.Value.ToString()))).
                                          And(Context, Context.MkIsInteger(rt)));
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
                case TypeEmbeddingKind.Enum:
                    return MkCoercion(t, unn, (EnumEmbedding)srcTE);
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
            return Context.MkReal(string.Format("{0}/{1}", r.Numerator, r.Denominator)); 
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(t.IsRatNum);
            var r = (Z3RatNum)t;
            bool wasAdded;
            return Index.MkCnst(new Rational(r.BigIntNumerator, r.BigIntDenominator), out wasAdded);
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (t.IsRatNum)
            {
                bool wasAdded;
                var ratExpr = (Z3RatNum)t;
                var rat = new Rational(ratExpr.Numerator.BigInteger, ratExpr.Denominator.BigInteger);
                if (rat.IsInteger)
                {
                    return Index.MkApply(
                        Index.RangeSymbol,
                        new Term[] { Index.MkCnst(rat, out wasAdded), Index.MkCnst(rat, out wasAdded) },
                        out wasAdded);
                }
                else
                {
                    return Index.MkCnst(rat, out wasAdded);
                }
            }
            else
            {
                return Type;
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine("Real embedding, sort {0}", Representation.Name);
            Console.WriteLine();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NaturalEmbedding te)
        {
            return Context.MkInt2Real(te.MkIntCoercion(t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, PosIntegerEmbedding te)
        {
            return Context.MkInt2Real(te.MkIntCoercion(t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NegIntegerEmbedding te)
        {
            return Context.MkInt2Real(te.MkIntCoercion(t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntRangeEmbedding te)
        {
            return Context.MkInt2Real(te.MkIntCoercion(t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntegerEmbedding te)
        {
            return Context.MkInt2Real((Z3.IntExpr)t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, SingletonEmbedding te)
        {
            return MkGround(te.Value.Symbol, null);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, EnumEmbedding te)
        {
            var coercions = DefaultMember.Item2;
            foreach (var s in unn.NonRangeMembers)
            {
                Contract.Assert(s.IsNonVarConstant);
                coercions = t.Eq(Context, te.MkGround(s, null)).Ite(
                            Context,
                            MkGround(s, null),
                            coercions);
            }

            return coercions;
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, UnionEmbedding te)
        {
            Z3Expr unbox;
            //// If the union contains real, then it cannot contain other numerics.
            var test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), t, out unbox);
            if (test != null)
            {
                return test.Ite(Context, unbox, DefaultMember.Item2);
            }

            //// Otherwise unn may contain subsorts of Integer or integer ranges.
            //// First handle the base subsorts
            var coercions = DefaultMember.Item2;
            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), t, out unbox);

            if (test != null)
            {
                coercions = test.Ite(Context, Context.MkInt2Real((Z3IntExpr)unbox), coercions);
            }
            else
            {
                test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), t, out unbox);
                if (test != null)
                {
                    coercions = test.Ite(
                        Context,
                        Context.MkInt2Real(((NaturalEmbedding)Owner.GetEmbedding(BaseSortKind.Natural)).MkIntCoercion(unbox)),
                        coercions);
                }

                test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), t, out unbox);
                if (test != null)
                {
                    coercions = test.Ite(
                        Context,
                        Context.MkInt2Real(((PosIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.PosInteger)).MkIntCoercion(unbox)),
                        coercions);
                }

                test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), t, out unbox);
                if (test != null)
                {
                    coercions = test.Ite(
                        Context,
                        Context.MkInt2Real(((NegIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.NegInteger)).MkIntCoercion(unbox)),
                        coercions);
                }
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
                                Context.MkInt2Real(((IntRangeEmbedding)tep).MkIntCoercion(unbox)),
                                coercions);
                            break;
                        case TypeEmbeddingKind.Singleton:
                            coercions = test.Ite(
                                Context,
                                MkGround(((SingletonEmbedding)tep).Value.Symbol, null),
                                coercions);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            foreach (var s in unn.NonRangeMembers)
            {
                if (s.Kind != SymbolKind.BaseCnstSymb)
                {
                    continue;
                }

                test = te.MkTestAndUnbox(s, t, out unbox);
                Contract.Assert(test != null);
                tep = Owner.GetEmbedding(unbox.Sort);
                switch (tep.Kind)
                {
                    case TypeEmbeddingKind.Enum:
                        coercions = test.And(Context, unbox.Eq(Context, tep.MkGround(s, null))).Ite(
                            Context,
                            MkGround(s, null),
                            coercions);
                        break;
                    case TypeEmbeddingKind.Singleton:
                        coercions = test.Ite(
                            Context,
                            MkGround(s, null),
                            coercions);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return coercions;
        }
    }
}
