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

    internal class SingletonEmbedding : ITypeEmbedding
    {
        private const string CreatorName = "Mk_{0}";
        private const string TesterName = "Is_{0}";
        private const string SortName = "Singleton_{0}";

        private Z3Con singletonCon;

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.Singleton;
            }
        }

        public Z3Fun CreationFun
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

        /// <summary>
        /// If the singleton is an integer n, then will be the type term n..n
        /// </summary>
        public Term Type
        {
            get;
            private set;
        }

        /// <summary>
        /// If the type term is n..n, then returns n. Otherwise returns the singleton type.
        /// </summary>
        public Term Value
        {
            get
            {
                if (Type.Groundness == Groundness.Ground)
                {
                    return Type;
                }
                else
                {
                    //// The singleton range n..n
                    Contract.Assert(Type.Symbol == Type.Owner.RangeSymbol);
                    return Type.Args[0];
                }
            }
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

        public SingletonEmbedding(TypeEmbedder owner, Symbol symbol)
        {
            Contract.Requires(owner != null);
            Contract.Requires(symbol != null && symbol.IsNonVarConstant);
            Owner = owner;
            bool wasAdded;

            if (symbol.Kind == SymbolKind.BaseCnstSymb)
            {
                var bc = (BaseCnstSymb)symbol;
                if (bc.CnstKind == CnstKind.Numeric && ((Rational)bc.Raw).IsInteger)
                {
                    var r = Index.MkApply(symbol, TermIndex.EmptyArgs, out wasAdded);
                    Type = Index.MkApply(Index.RangeSymbol, new Term[] { r, r }, out wasAdded);
                }
                else
                {
                    Type = Index.MkApply(symbol, TermIndex.EmptyArgs, out wasAdded);
                }
            }
            else
            {
                Type = Index.MkApply(symbol, TermIndex.EmptyArgs, out wasAdded);
            }

            singletonCon = Context.MkConstructor(
                string.Format(CreatorName, symbol.PrintableName),
                string.Format(TesterName, symbol.PrintableName));

            Representation = Context.MkDatatypeSort(string.Format(SortName, symbol.PrintableName), new Z3Con[] { singletonCon });
            CreationFun = singletonCon.ConstructorDecl;
            TesterFun = singletonCon.TesterDecl;
            DefaultMember = new Tuple<Term, Z3Expr>(Value, CreationFun.Apply());
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            return Index.IsGroundMember(type, Value) ? Context.MkTrue() : Context.MkFalse();
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
                case TypeEmbeddingKind.Real:
                    return MkCoercion(t, unn, (RealEmbedding)srcTE);
                case TypeEmbeddingKind.String:
                    return MkCoercion(t, unn, (StringEmbedding)srcTE);
                case TypeEmbeddingKind.Union:
                    return MkCoercion(t, unn, (UnionEmbedding)srcTE);
                default:
                    throw new NotImplementedException();
            }
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb != null && symb == Value.Symbol);
            Contract.Assert(args == null || args.Length == 0);
            return CreationFun.Apply();
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(args == null || args.Length == 0);
            return Value;
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            return Type;
        }

        public void Debug_Print()
        {
            Console.WriteLine("Singleton embedding of {0}", Type.Debug_GetSmallTermString());
            Console.WriteLine("Sort: {0}", Representation.Name);
            Console.WriteLine("Creator fun: {0}", CreationFun.Name);
            Console.WriteLine("Tester fun: {0}", TesterFun.Name);
            Console.WriteLine();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntegerEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NaturalEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, PosIntegerEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, NegIntegerEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, IntRangeEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, EnumEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, RealEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, StringEmbedding te)
        {
            return CreationFun.Apply();
        }

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, UnionEmbedding te)
        {
            return CreationFun.Apply();
        }
    }
}
