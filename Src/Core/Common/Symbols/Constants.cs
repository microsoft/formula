namespace Microsoft.Formula.Common.Terms
{
    public enum Groundness
    {
        Ground,
        Variable,
        Type
    }

    public enum SymbolKind
    {
        BaseSortSymb,
        BaseCnstSymb,
        BaseOpSymb,
        UserCnstSymb,
        UserSortSymb,
        UnnSymb,
        ConSymb,
        MapSymb
    }

    public enum BaseSortKind
    {
        NegInteger,
        PosInteger,
        Natural,
        Integer,
        Real,
        String
    }

    public enum UserCnstSymbKind
    {
        New,
        Derived,
        Variable
    }
}
