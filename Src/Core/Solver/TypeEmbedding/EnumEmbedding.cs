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

    /// <summary>
    /// Represents a set of non-integral constants be mapping bit vectors to constants.
    /// </summary>
    internal class EnumEmbedding : ITypeEmbedding
    {
        private const string BoxingName = "BoxBV2Enum_{0}";
        private const string UnboxingName = "UnboxEnum2BV_{0}";
        private const string TesterName = "IsEnum_{0}";
        private const string SortName = "Enum_{0}";

        private Z3Con boxingCon;
        private Z3BVSort bvSort;

        /// <summary>
        /// Maps an integer value to the constant is stands for.
        /// </summary>
        private Map<uint, Symbol> valToSymb = new Map<uint, Symbol>((x, y) => ((int)x) - ((int)y));
        private Map<Symbol, uint> symbToVal = new Map<Symbol, uint>(Symbol.Compare);

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.Enum;
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

        private Z3Context Context
        {
            get { return Owner.Context; }
        }

        private TermIndex Index
        {
            get { return Owner.Index; }
        }

        public EnumEmbedding(TypeEmbedder owner, Term type, string name)
        {
            Contract.Requires(owner != null);
            Contract.Requires(type != null);
            Owner = owner;

            uint id;
            Type = type;
            foreach (var t in type.Enumerate(x => x.Args))
            {
                if (t.Symbol.Arity == 0)
                {
                    Contract.Assert(t.Symbol.Kind == SymbolKind.BaseCnstSymb || t.Symbol.Kind == SymbolKind.UserCnstSymb);
                    id = (uint)valToSymb.Count;
                    valToSymb.Add(id, t.Symbol);
                    symbToVal.Add(t.Symbol, id);
                }
            }

            var size = ((uint)valToSymb.Count).MostSignificantOne();
            Contract.Assert(((uint)valToSymb.Count).PopulationCount() == 1);
            bvSort = Context.MkBitVecSort(size);

            bool wasAdded;
            boxingCon = Context.MkConstructor(
                string.Format(BoxingName, name),
                string.Format(TesterName, name),
                new string[] { string.Format(UnboxingName, name) }, 
                new Z3Sort[] { bvSort });

            Representation = Context.MkDatatypeSort(string.Format(SortName, name), new Z3Con[] { boxingCon });
            BoxingFun = boxingCon.ConstructorDecl;
            UnboxingFun = boxingCon.AccessorDecls[0];
            TesterFun = boxingCon.TesterDecl;
            DefaultMember = new Tuple<Term, Z3Expr>(
                Index.MkApply(valToSymb[0], TermIndex.EmptyArgs, out wasAdded),
                BoxingFun.Apply(Context.MkBV(0, size)));
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
            foreach (var m in unn.NonRangeMembers)
            {
                if (m.Kind != SymbolKind.BaseCnstSymb && m.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                test = test.Or(Context, bt.Eq(Context, Context.MkBV(symbToVal[m], bvSort.Size)));
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
                case TypeEmbeddingKind.Real:
                    return MkCoercion(t, unn, (RealEmbedding)srcTE);
                case TypeEmbeddingKind.Singleton:
                    return MkCoercion(t, unn, (SingletonEmbedding)srcTE);
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
            Contract.Assert(symb != null && symb.IsNonVarConstant);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(symbToVal.ContainsKey(symb));
            return BoxingFun.Apply(Context.MkBV(symbToVal[symb], bvSort.Size));
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null);
            Contract.Assert(args == null || args.Length == 0);
            Contract.Assert(t.FuncDecl.Equals(BoxingFun));
            var i = (Z3BVNum)t.Args[0];
            bool wasAdded;
            return Index.MkApply(valToSymb[i.UInt], TermIndex.EmptyArgs, out wasAdded);
        }

        public bool IsMember(Symbol s)
        {
            Contract.Requires(s != null && s.IsNonVarConstant);
            return symbToVal.ContainsKey(s);
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
                return MkGround(t, null);
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine("Enumeration of {0} elements with bv width {1}", symbToVal.Count, bvSort.Size);
            Console.WriteLine(
                "Boxing fun: {0}, Unboxing fun: {1}, sort: {2}",
                BoxingFun.Name,
                UnboxingFun.Name,
                Representation.Name);

            foreach (var kv in valToSymb)
            {
                Console.WriteLine("{0} --> {1}", kv.Key, kv.Value.PrintableName);
            }

            Console.WriteLine();
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

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, RealEmbedding te)
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

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, SingletonEmbedding te)
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

        private Z3Expr MkCoercion(Z3Expr t, AppFreeCanUnn unn, StringEmbedding te)
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
            Z3BoolExpr test;
            Z3Expr unboxed;
            ITypeEmbedding ubxTE;
            var coercions = DefaultMember.Item2;
            foreach (var s in unn.NonRangeMembers)
            {
                test = te.MkTestAndUnbox(s, t, out unboxed);
                Contract.Assert(test != null);
                ubxTE = Owner.GetEmbedding(unboxed.FuncDecl.Range);
                test = test.And(Context, ubxTE.MkGround(s, null).Eq(Context, unboxed));
                coercions = test.Ite(Context, MkGround(s, null), coercions);
            }

            return coercions;
        }
    }
}