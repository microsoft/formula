namespace Microsoft.Formula.API.Generators
{
    using System;
    using System.Diagnostics.Contracts;

    /// <summary>
    /// This interface is implemented by Formula types embedded into C#.
    /// </summary>
    public interface ICSharpTerm
    {
        int Arity { get; }
        object Symbol { get; }
        ICSharpTerm this[int index] { get; }
    }
}
