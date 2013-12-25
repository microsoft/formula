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
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3IntNum = Microsoft.Z3.IntNum;

    internal class IntegerEmbedding : ITypeEmbedding
    {
        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.Integer;
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
            get { return 15; }
        }

        private Z3Context Context
        {
            get { return Owner.Context; }
        }

        private TermIndex Index
        {
            get { return Owner.Index; }
        }

        public IntegerEmbedding(TypeEmbedder owner)
        {
            Contract.Requires(owner != null);
            Owner = owner;
            Representation = Context.MkIntSort();
            bool wasAdded;
            Type = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded);
            DefaultMember = new Tuple<Term, Z3Expr>(Index.MkCnst(Rational.Zero, out wasAdded), Context.MkNumeral(0, Representation));
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            var it = (Z3IntExpr)t;

            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }

            Z3BoolExpr test = null;
            if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural)))
            {
                test = test.Or(Context, it.Ge(Context, Context.MkInt(0)));
            }
            else if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger)))
            {
                test = test.Or(Context, it.Ge(Context, Context.MkInt(1)));
            }

            if (unn.Contains(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger)))
            {
                test = test.Or(Context, it.Lt(Context, Context.MkInt(0)));
            }

            foreach (var i in unn.RangeMembers)
            {
                test = test.Or(Context, it.Ge(Context, Context.MkInt(i.Key.ToString())).
                                          And(Context, it.Le(Context, Context.MkInt(i.Value.ToString()))));
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

            return Context.MkInt(r.Numerator.ToString());
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(t.IsIntNum);
            var i = (Z3IntNum)t;
            bool wasAdded;
            return Index.MkCnst(new Rational(i.BigInteger, System.Numerics.BigInteger.One), out wasAdded);
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (t.IsIntNum)
            {
                return MkGround(t, null);
            }
            else
            {
                return Type;
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine("Integer embedding, sort {0}", Representation.Name);
            Console.WriteLine();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NaturalEmbedding te)
        {
            return te.MkIntCoercion(t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, PosIntegerEmbedding te)
        {
            return te.MkIntCoercion(t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NegIntegerEmbedding te)
        {
            return te.MkIntCoercion(t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntRangeEmbedding te)
        {
            return te.MkIntCoercion(t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, RealEmbedding te)
        {
            return Context.MkReal2Int((Z3.RealExpr)t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, SingletonEmbedding te)
        {
            return Context.MkInt(((Rational)((BaseCnstSymb)te.Value.Symbol).Raw).Numerator.ToString());
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, UnionEmbedding te)
        {
            Z3Expr unbox;
            //// If the union contains real, then it cannot contain other integral numerics.
            var test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), t, out unbox);
            if (test != null)
            {
                return test.Ite(Context, Context.MkReal2Int((Z3.RealExpr)unbox), DefaultMember.Item2);
            }

            //// If the union contains int, then it cannot contain other integral numerics.
            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), t, out unbox);
            if (test != null)
            {
                return test.Ite(Context, unbox, DefaultMember.Item2);
            }

            //// Otherwise unn may contain subsorts of Integer or integer ranges.
            //// First handle the base subsorts
            var coercions = DefaultMember.Item2;

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                    Context, 
                    ((NaturalEmbedding)Owner.GetEmbedding(BaseSortKind.Natural)).MkIntCoercion(unbox), 
                    coercions);
            }

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                    Context,
                    ((PosIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.PosInteger)).MkIntCoercion(unbox),
                    coercions);
            }

            test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), t, out unbox);
            if (test != null)
            {
                coercions = test.Ite(
                    Context,
                    ((NegIntegerEmbedding)Owner.GetEmbedding(BaseSortKind.NegInteger)).MkIntCoercion(unbox),
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
                                ((IntRangeEmbedding)tep).MkIntCoercion(unbox),
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

            return coercions;
        }
    }
}
