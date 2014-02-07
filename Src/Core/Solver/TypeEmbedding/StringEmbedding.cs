namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Text;
    using API;
    using Common;
    using Common.Terms;
    using Common.Extras;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3BVSort = Microsoft.Z3.BitVecSort;
    using Z3BVNum = Microsoft.Z3.BitVecNum;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3Con = Microsoft.Z3.Constructor;
    using Z3Fun = Microsoft.Z3.FuncDecl;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;

    /// <summary>
    /// Encodes strings as follows:
    /// String      ::= BoxNEStr(NonEmptyStr) | EmptyString()
    /// NonEmptyStr ::= Char(BV8) | Append(NonEmptyStr, BV8)
    /// </summary>
    internal class StringEmbedding : ITypeEmbedding
    {
        private const uint CharWidth = 8;

        private const string CharBoxingName = "BoxBV2Char";
        private const string CharUnboxingName = "UnboxChar2BV";
        private const string IsCharName = "IsChar";
        private const string AppStrName = "AppStr";
        private const string AppPrefixName = "GetPrefix";
        private const string AppSuffixName = "GetSuffix";
        private const string IsMultiStrName = "IsMultiStr";
        private const string NeStrSortName = "NeString";

        private const string StrBoxingName = "BoxNeStr2Str";
        private const string StrUnboxingName = "UnboxStr2NeStr";
        private const string IsEmptyStrName = "IsEmptyStr";
        private const string IsNeStrName = "IsNeStr";
        private const string EmptyStrName = "EmptyStr";
        private const string StrSortName = "String";

        private Z3BVSort charSort;

        private Z3Con charBoxing;
        private Z3Fun charUnboxing;
        private Z3Fun isChar;
        private Z3Con appStr;
        private Z3Fun appPrefix;
        private Z3Fun appSuffix;
        private Z3Fun isMultiStr;
        private Z3Sort neStrSort;

        private Z3Con strBoxing;
        private Z3Fun strUnboxing;
        private Z3Fun isNeStr;
        private Z3Con emptyStr;
        private Z3Fun isEmptyStr;
        private Z3Sort strSort;

        public TypeEmbeddingKind Kind
        {
            get
            {
                return TypeEmbeddingKind.String;
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

        public StringEmbedding(TypeEmbedder owner, uint cost)
        {
            Contract.Requires(owner != null);
            Owner = owner;
            EncodingCost = cost;
            bool wasAdded;
            Type = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.String), TermIndex.EmptyArgs, out wasAdded);

            charSort = Context.MkBitVecSort(CharWidth);

            //// Converts bit vectors to strings.
            charBoxing = Context.MkConstructor(
                            CharBoxingName,
                            IsCharName,
                            new string[] { CharUnboxingName },
                            new Z3Sort[] { charSort });

            //// Append a char to a string
            appStr = Context.MkConstructor(
                            AppStrName,
                            IsMultiStrName,
                            new string[] { AppPrefixName, AppSuffixName },
                            new Z3Sort[] { null, charSort }, 
                            new uint[] { 0, 0 });

            neStrSort = Context.MkDatatypeSort(NeStrSortName, new Z3Con[] { charBoxing, appStr });

            isChar = charBoxing.TesterDecl;
            charUnboxing = charBoxing.AccessorDecls[0];
            isMultiStr = appStr.TesterDecl;
            appPrefix = appStr.AccessorDecls[0];
            appSuffix = appStr.AccessorDecls[1];

            //// Functions for building strings.
            strBoxing = Context.MkConstructor(
                            StrBoxingName,
                            IsNeStrName,
                            new string[] { StrUnboxingName },
                            new Z3Sort[] { neStrSort });

            emptyStr = Context.MkConstructor(
                            EmptyStrName,
                            IsEmptyStrName,
                            null,
                            null,
                            null);

            strSort = Context.MkDatatypeSort(StrSortName, new Z3Con[] { emptyStr, strBoxing });
            isNeStr = strBoxing.TesterDecl;
            strUnboxing = strBoxing.AccessorDecls[0];
            isEmptyStr = emptyStr.TesterDecl;

            Representation = strSort;
            DefaultMember = new Tuple<Term, Z3Expr>(Index.EmptyStringValue, emptyStr.ConstructorDecl.Apply());
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb != null && symb.Kind == SymbolKind.BaseCnstSymb && (args == null || args.Length == 0));
            var bc = (BaseCnstSymb)symb;
            Contract.Assert(bc.CnstKind == CnstKind.String);
            var strVal = (string)bc.Raw;
            if (string.IsNullOrEmpty(strVal))
            {
                return emptyStr.ConstructorDecl.Apply();
            }

            Z3Expr neString = null;
            for (int i = 0; i < strVal.Length; ++i)
            {
                if (neString == null)
                {
                    neString = charBoxing.ConstructorDecl.Apply(Context.MkBV((uint)strVal[i], CharWidth));
                }
                else
                {
                    neString = appStr.ConstructorDecl.Apply(neString, Context.MkBV((uint)strVal[i], CharWidth));
                }
            }

            return strBoxing.ConstructorDecl.Apply(neString);
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null && (t.FuncDecl.Equals(emptyStr.ConstructorDecl) || t.FuncDecl.Equals(strBoxing.ConstructorDecl)));
            Contract.Assert(args == null || args.Length == 0);
            if (t.FuncDecl.Equals(emptyStr))
            {
                return Index.EmptyStringValue;
            }

            var strBldr = new StringBuilder();
            t.Compute<Unit>(
                (x, s) => x.Args,
                (x, c, s) =>
                {
                    if (x.ASTKind == Z3.Z3_ast_kind.Z3_NUMERAL_AST)
                    {
                        strBldr.Append((char)((Z3BVNum)x).UInt);
                    }

                    return default(Unit);
                });

            bool wasAdded;
            return Index.MkCnst(strBldr.ToString(), out wasAdded);
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            var st = (Z3Expr)t;

            if (intr == null)
            {
                return Context.MkFalse();
            }
            else if (intr == Type)
            {
                return Context.MkTrue();
            }


            Z3BoolExpr test = null;
            BaseCnstSymb bc;
            foreach (var e in unn.NonRangeMembers)
            {
                if (e.Kind != SymbolKind.BaseCnstSymb)
                {
                    continue;
                }

                bc = (BaseCnstSymb)e;
                if (bc.CnstKind != CnstKind.String)
                {
                    continue;
                }

                test = test.Or(Context, st.Eq(Context, MkGround(e, null)));
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
                case TypeEmbeddingKind.Singleton:
                    return MkCoercion(t, unn, (SingletonEmbedding)srcTE);
                case TypeEmbeddingKind.Enum:
                    return MkCoercion(t, unn, (EnumEmbedding)srcTE);
                case TypeEmbeddingKind.Union:
                    return MkCoercion(t, unn, (UnionEmbedding)srcTE);
                default:
                    throw new NotImplementedException();
            }
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            if (t.FuncDecl.Equals(emptyStr.ConstructorDecl))
            {
                return Index.EmptyStringValue;
            }
            else if (!t.FuncDecl.Equals(strBoxing.ConstructorDecl))
            {
                return Type;
            }

            var neStr = t.Args[0];
            char c;
            bool wasAdded;
            string s = string.Empty;
            while (true)
            {
                if (neStr.FuncDecl.Equals(charBoxing.ConstructorDecl))
                {
                    if (TryGetChar(neStr.Args[0], out c))
                    {
                        return Index.MkCnst(c + s, out wasAdded);
                    }
                    else
                    {
                        return Type;
                    }
                }
                else if (neStr.FuncDecl.Equals(appStr.ConstructorDecl))
                {
                    if (TryGetChar(neStr.Args[1], out c))
                    {
                        s = c + s;
                        neStr = neStr.Args[0];
                    }
                    else
                    {
                        return Type;
                    }
                }
                else
                {
                    return Type;
                }
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine("String embedding, sort {0}", Representation.Name);
            Console.WriteLine();
        }

        /// <summary>
        /// If t is bit vector expression, then tries to convert it to a char.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private bool TryGetChar(Z3Expr t, out char c)
        {
            if (!t.IsBVNumeral)
            {
                c = '\0';
                return false;
            }

            c = (char)((Z3BVNum)t).UInt;
            return true;
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
            //// If the union contains string, then it cannot contain other strings.
            var test = te.MkTestAndUnbox(Index.SymbolTable.GetSortSymbol(BaseSortKind.String), t, out unbox);
            if (test != null)
            {
                return test.Ite(Context, unbox, DefaultMember.Item2);
            }

            ITypeEmbedding tep;
            Z3Expr coercions = DefaultMember.Item2;
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