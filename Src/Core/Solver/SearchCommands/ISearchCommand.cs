namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    public enum SearchCommandKind { Push, Pop, Halt }

    public interface ISearchCommand
    {
        string Message { get; }

        SearchCommandKind Kind { get; }
    }
}
