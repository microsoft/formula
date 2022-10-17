using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Formula.Common.Terms;
using Pair = Microsoft.Formula.API.ASTQueries.ASTSchema.SearchState.Pair;
using ReplaceData = Microsoft.Formula.API.ASTQueries.ASTSchema.ReplaceData;

namespace Microsoft.Formula.Common
{
    public sealed class TermComparer : IComparer<Terms.Term>
    {
        private TermComparer() { }

        private static readonly TermComparer termComparer = new TermComparer();

        public static TermComparer GetTermComparer()
        {
            return termComparer;
        }

        public int Compare(Term t1, Term t2)
        {
            if (t1.UId < t2.UId)
            {
                return -1;
            }
            else if (t1.UId > t2.UId)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }
    }

    public sealed class SymbolComparer : IComparer<Terms.Symbol>
    {
        private SymbolComparer() { }

        private static readonly SymbolComparer symbolComparer = new SymbolComparer();

        public static SymbolComparer GetSymbolComparer()
        {
            return symbolComparer;
        }

        public int Compare(Symbol s1, Symbol s2)
        {
            return s1.Id - s2.Id;
        }
    }

    public sealed class PairComparer : IComparer<Pair>
    {
        private PairComparer() { }

        private static readonly PairComparer pairComparer = new PairComparer();

        public static PairComparer GetPairComparer()
        {
            return pairComparer;
        }

        public int Compare(Pair p1, Pair p2)
        {
            if (p1.nodeKind != p2.nodeKind)
            {
                return (int)p1.nodeKind - (int)p2.nodeKind;
            }

            return (int)p1.context - (int)p2.context;
        }
    }

    public sealed class StringComparer : IComparer<string>
    {
        private StringComparer() { }

        private static readonly StringComparer stringComparer = new StringComparer();

        public static StringComparer GetStringComparer()
        {
            return stringComparer;
        }

        public int Compare(String s1, String s2)
        {
            return string.CompareOrdinal(s1, s2);
        }
    }

    public sealed class ReplaceDataComparer : IComparer<ReplaceData>
    {
        private ReplaceDataComparer() { }

        private static readonly ReplaceDataComparer replaceDataComparer = new ReplaceDataComparer();

        public static ReplaceDataComparer GetReplaceDataComparer()
        {
            return replaceDataComparer;
        }

        public int Compare(ReplaceData d1, ReplaceData d2)
        {
            if (d1.parentKind != d2.parentKind)
            {
                return (int)d1.parentKind - (int)d2.parentKind;
            }

            if (d1.context != d2.context)
            {
                return (int)d1.context - (int)d2.context;
            }

            if (d1.childKind != d2.childKind)
            {
                return (int)d1.childKind - (int)d2.childKind;
            }

            return (int)d1.replaceKind - (int)d2.replaceKind;
        }
    }
}
