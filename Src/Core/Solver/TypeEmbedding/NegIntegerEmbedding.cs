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
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3IntNum = Microsoft.Z3.IntNum;
    using Z3Con = Microsoft.Z3.Constructor;
    using Z3Fun = Microsoft.Z3.FuncDecl;

    internal class NegIntegerEmbedding : ITypeEmbedding
    {
        private const string BoxingName = "BoxInt2Neg";
        private const string UnboxingName = "UnboxNeg2Int";
        private const string TesterName = "IsNeg";
        private const string SortName = "NegInteger";

        private Z3Con boxingCon;

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.NegInteger;
            }
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

        public NegIntegerEmbedding(TypeEmbedder owner, uint cost)
        {
            Contract.Requires(owner != null);
            Owner = owner;
            EncodingCost = cost;
            bool wasAdded;
            Type = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded);
            boxingCon = Context.MkConstructor(BoxingName, TesterName, new string[] { UnboxingName }, new Z3Sort[] { Context.MkIntSort() });
            Representation = Context.MkDatatypeSort(SortName, new Z3Con[] { boxingCon });
            BoxingFun = boxingCon.ConstructorDecl;
            UnboxingFun = boxingCon.AccessorDecls[0];
            TesterFun = boxingCon.TesterDecl;
            DefaultMember = new Tuple<Term, Z3Expr>(
                Index.MkCnst(new Rational(BigInteger.MinusOne, BigInteger.One), out wasAdded), 
                BoxingFun.Apply(Context.MkInt(-1)));
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            var it = (Z3IntExpr)UnboxingFun.Apply(t);

            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }

            Z3BoolExpr test = null;
            var iNeg = MkIntCoercion(t);
            foreach (var r in unn.RangeMembers)
            {
                test = test.Or(
                        Context,
                        iNeg.Ge(
                            Context,
                            Context.MkInt(r.Key.ToString())).And(
                                Context,
                                iNeg.Le(Context, Context.MkInt(r.Value.ToString()))));
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
                case TypeEmbeddingKind.Integer:
                    return MkCoercion(t, unn, (IntegerEmbedding)srcTE);
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

        /// <summary>
        /// Coerces an integer into a natural.
        /// </summary>
        public Z3Expr MkNegCoercion(Z3IntExpr i)
        {
            var zero = Context.MkInt(0);
            var one = Context.MkInt(1);
            var two = Context.MkInt(2);

            var oddCase = BoxingFun.Apply(i.Add(Context, one).Div(Context, two));

            var coercion = i.IsEven(Context).Ite(
                                Context,
                                BoxingFun.Apply(i.Neg(Context).Div(Context, two)),
                                oddCase);

            return coercion;
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb != null && symb.Kind == SymbolKind.BaseCnstSymb);
            Contract.Assert(args == null || args.Length == 0);
            var bc = (BaseCnstSymb)symb;
            Contract.Assert(bc.CnstKind == CnstKind.Numeric);
            var r = (Rational)bc.Raw;
            Contract.Assert(r.IsInteger);
            Contract.Assert(r.Sign < 0);
            var n = r.Numerator;
            if (n == -1)
            {
                return BoxingFun.Apply(Context.MkInt(0));
            }
            else if (n.IsEven)
            {
                n = (-n) / 2;
                return BoxingFun.Apply(Context.MkInt(n.ToString()));
            }
            else
            {
                n = (n + 1) / 2;
                return BoxingFun.Apply(Context.MkInt(n.ToString()));
            }
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(t.FuncDecl.Equals(BoxingFun));
            var n = ((Z3IntNum)t.Args[0]).BigInteger;
            bool wasAdded;

            if (n.IsZero)
            {
                return Index.MkCnst(-Rational.One, out wasAdded);
            }
            else if (n.Sign > 0)
            {
                n = -2 * n;
                return Index.MkCnst(new Rational(n, System.Numerics.BigInteger.One), out wasAdded);
            }
            else
            {
                n = (2 * n) - 1;
                return Index.MkCnst(new Rational(n, System.Numerics.BigInteger.One), out wasAdded);
            }
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (!t.FuncDecl.Equals(BoxingFun) || !t.Args[0].IsIntNum)
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

        public Z3IntExpr MkIntCoercion(Z3Expr neg)
        {
            Contract.Requires(neg.Sort.Equals(Representation));
            var i = (Z3IntExpr)UnboxingFun.Apply(neg);
            var zero = Context.MkInt(0);
            var one = Context.MkInt(1);
            var none = Context.MkInt(-1);
            var two = Context.MkInt(2);
            var ntwo = Context.MkInt(-2);

            return (Z3IntExpr)i.Eq(Context, zero).Ite(
                      Context,
                      none,
                      i.Gt(Context, zero).Ite(
                        Context,
                        i.Mul(Context, ntwo),
                        i.Mul(Context, two).Sub(Context, one)));
        }

        public void Debug_Print()
        {
            Console.WriteLine(
                "NegInteger embedding, boxing fun: {0}, unboxing fun: {1}, sort {2}",
                BoxingFun.Name,
                UnboxingFun.Name,
                Representation.Name);
            Console.WriteLine();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntegerEmbedding te)
        {
            return MkNegCoercion((Z3IntExpr)t);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntRangeEmbedding te)
        {
            return MkNegCoercion(te.MkIntCoercion(t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, RealEmbedding te)
        {
            return MkNegCoercion(Context.MkReal2Int((Z3.RealExpr)t));
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, SingletonEmbedding te)
        {
            return MkGround(te.Value.Symbol, null);
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, UnionEmbedding te)
        {
            Z3Expr unbox;
            var intTE = Owner.GetEmbedding(BaseSortKind.Integer);

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
            var coercions = intTE.DefaultMember.Item2;

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
                                intTE.MkGround(((SingletonEmbedding)tep).Value.Symbol, null),
                                coercions);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return MkNegCoercion((Z3IntExpr)coercions);
        }
    }
}
