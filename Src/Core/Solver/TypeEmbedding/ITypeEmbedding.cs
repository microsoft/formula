namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using Common;
    using Common.Terms;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Quantifier = Microsoft.Z3.Quantifier;

    internal enum TypeEmbeddingKind
    {
        Constructor,
        Enum,
        Integer,
        IntRange,
        Natural,
        NegInteger,
        PosInteger,
        Real,
        String,
        Union,
        Singleton
    }

    internal interface ITypeEmbedding
    {
        TypeEmbeddingKind Kind { get; }

        /// <summary>
        /// The owner of the this embedding
        /// </summary>
        TypeEmbedder Owner { get; }

        /// <summary>
        /// The Z3 Sort used to represent elements of this type.
        /// </summary>
        Z3Sort Representation { get; }

        /// <summary>
        /// The type that is encoded by this encoder.
        /// </summary>
        Term Type { get; }

        /// <summary>
        /// Returns a FORMULA/Z3 term pair that is some member of this type.
        /// </summary>
        Tuple<Term, Z3Expr> DefaultMember { get; }

        /// <summary>
        /// If t is a Z3 term in this representation,
        /// then MkTest returns a Z3 term that evaluates to true whenever
        /// t evaluates to a member of type.
        /// </summary>
        /// <param name="t"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Z3BoolExpr MkTest(Z3Expr t, Term type);

        /// <summary>
        /// Returns a total function that coerces t into an equivalent term in this representation
        /// if t evaluates to a member of this representation. If t does not evaluate to
        /// a member this of this type, then the function returns an arbitrary value in this type.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        Z3Expr MkCoercion(Z3Expr t);

        /// <summary>
        /// Constructs a ground term in the representation, where args are encoded ground terms.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        Z3Expr MkGround(Symbol symb, Z3Expr[] args);

        /// <summary>
        /// If t is a Z3 term in this representation and args are decoded arguments, then
        /// then MkGround converts this Z3 term into a formula ground term.
        /// </summary>
        /// <param name="t"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        Term MkGround(Z3Expr t, Term[] args);

        /// <summary>
        /// If t is a Z3 term in this representation, then returns a smaller type expression 
        /// containing all possible valuations of t.
        /// </summary>
        Term GetSubtype(Z3Expr t);

        void Debug_Print();
    }
}
