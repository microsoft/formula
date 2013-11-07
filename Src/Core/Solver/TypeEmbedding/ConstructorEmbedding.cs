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
    using Z3Pattern = Microsoft.Z3.Pattern;

    /// <summary>
    /// Represents a set of non-integral constants be mapping bit vectors to constants.
    /// </summary>
    internal class ConstructorEmbedding : ITypeEmbedding
    {
        public TypeEmbeddingKind Kind 
        {
            get
            {
                return TypeEmbeddingKind.Constructor;
            }        
        }

        public Z3Con Z3Constructor
        {
            get;
            private set;
        }

        public UserSymbol Constructor
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

        public ConstructorEmbedding(TypeEmbedder owner, UserSymbol conOrMap, Map<Term, Tuple<uint, UserSymbol>> sortIndices)
        {
            Contract.Requires(owner != null && conOrMap != null);
            Contract.Requires(conOrMap.IsDataConstructor);
            Constructor = conOrMap;
            Owner = owner;

            bool wasAdded;
            Type = Index.MkApply(
                conOrMap.Kind == SymbolKind.ConSymb ? ((ConSymb)conOrMap).SortSymbol : ((MapSymb)conOrMap).SortSymbol,
                TermIndex.EmptyArgs,
                out wasAdded);

            var fldNames = new string[conOrMap.Arity];
            var fldSorts = new Z3Sort[conOrMap.Arity];
            var fldRefs = new uint[conOrMap.Arity];

            IEnumerable<Field> flds;
            if (conOrMap.Kind == SymbolKind.ConSymb)
            {
                flds = ((ConDecl)(conOrMap.Definitions.First().Node)).Fields;
            }
            else
            {
                var mapDecl = (MapDecl)(conOrMap.Definitions.First().Node);
                flds = mapDecl.Dom.Concat(mapDecl.Cod);
            }

            int i = 0;
            Tuple<uint, UserSymbol> sortData;
            Term argType;
            foreach (var f in flds)
            {
                argType = Index.GetCanonicalTerm(conOrMap, i);
                fldNames[i] = string.Format("Get_{0}_{1}", conOrMap.FullName, string.IsNullOrEmpty(f.Name) ? i.ToString() : f.Name);
                if (sortIndices.TryFindValue(argType, out sortData))
                {
                    fldSorts[i] = null;
                    fldRefs[i] = sortData.Item1;
                }
                else
                {
                    fldSorts[i] = owner.GetEmbedding(argType).Representation;
                    fldRefs[i] = 0;
                }

                ++i;
            }

            Z3Constructor = Context.MkConstructor(conOrMap.FullName, "Is" + conOrMap.FullName, fldNames, fldSorts, fldRefs);
        }

        public void SetRepresentation(Z3Sort sort)
        {
            Contract.Requires(Representation == null);
            Representation = sort;
        }

        public Z3BoolExpr MkTest(Z3Expr t, Term type)
        {
            Term intr;
            var unn = Owner.GetIntersection(Type, type, out intr);
            if (intr == null)
            {
                return Context.MkFalse();
            }
            else
            {
                return Context.MkTrue();
            }
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

            //// Otherwise, the srcTE must be a union embedding.
            var sort = Constructor.Kind == SymbolKind.ConSymb ? ((ConSymb)Constructor).SortSymbol : ((MapSymb)Constructor).SortSymbol;
            Z3Expr unboxed;
            var test = ((UnionEmbedding)srcTE).MkTestAndUnbox(sort, t, out unboxed);
            Contract.Assert(test != null);
            return test.Ite(Context, unboxed, DefaultMember.Item2);
        }

        public Z3Expr MkGround(Symbol symb, Z3Expr[] args)
        {
            Contract.Assert(symb != null && symb == Constructor);
            Contract.Assert(args != null && args.Length == symb.Arity);
            return Z3Constructor.ConstructorDecl.Apply(args);
        }

        public Term MkGround(Z3Expr t, Term[] args)
        {
            Contract.Assert(t != null && t.FuncDecl.Equals(Z3Constructor.ConstructorDecl));
            Contract.Assert(args != null && args.Length == Constructor.Arity);
            bool wasAdded;
            return Index.MkApply(Constructor, args, out wasAdded);
        }

        public Term GetSubtype(Z3Expr t)
        {
            Contract.Assert(t != null && t.Sort.Equals(Representation));
            return Type;
        }

        /// <summary>
        /// The default value is F applied to args. If args[i] is null, then a constant
        /// can be choosen for arg[i]
        /// </summary>
        internal void SetDefaultMember(Term[] args)
        {
            Contract.Requires(args != null && args.Length == Constructor.Arity);
            Contract.Requires(DefaultMember == null);
            bool wasAdded;
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i] == null)
                {
                    args[i] = Owner.GetSomeConstant(Constructor.CanonicalForm[i]);
                }
            }

            var defTerm = Index.MkApply(Constructor, args, out wasAdded);
            Contract.Assert(Index.IsGroundMember(Type, defTerm));
            DefaultMember = new Tuple<Term, Z3Expr>(defTerm, Owner.MkGround(defTerm, this));
        }

        public void Debug_Print()
        {
            Console.WriteLine("Constructor embedding: {0}", Constructor.FullName);
            Console.WriteLine("{0}(", Z3Constructor.ConstructorDecl.Name);
            for (int i = 0; i < Constructor.Arity; ++i)
            {
                Console.WriteLine("\t{0} : {1}", Z3Constructor.AccessorDecls[i].Name, Z3Constructor.AccessorDecls[i].Range.Name);
            }

            Console.WriteLine(") : {0}", Representation.Name);
            Console.WriteLine("Tester: {0}", Z3Constructor.TesterDecl.Name);
            Console.WriteLine();
        }
    }
}