namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Common.Extras;

    internal static class TermPrinting
    {
        private enum TypeEnumState { None, Opened };

        internal static void PrintTerm(
                    Term t, 
                    TextWriter wr, 
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)
        {
            t.Compute<Unit>(
                (x, s) =>
                {
                    wr.Write(x.Symbol.PrintableName);
                    return PrintTerm_Unfold(x, wr);             
                },
                (x, ch, s) =>
                {
                    return default(Unit);
                });
        }

        internal static void PrintTypeTerm(
                    Term t, 
                    TextWriter wr, 
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)
        {
            Contract.Requires(t != null && wr != null);
            bool success;
            string name;
            bool isFirstComponent = true;
            var enumState = TypeEnumState.None;
            BaseCnstSymb bcs1, bcs2;
            t.Visit(
                x => x.Symbol == t.Owner.TypeUnionSymbol ? x.Args : null,
                x =>
                {
                    if (x.Symbol == t.Owner.TypeUnionSymbol)
                    {
                        return;
                    }

                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            OpenEnum(wr, isFirstComponent, ref enumState);
                            bcs1 = (BaseCnstSymb)x.Symbol;
                            switch (bcs1.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    wr.Write(((Rational)bcs1.Raw).ToString());
                                    break;
                                case CnstKind.String:
                                    wr.Write(ASTSchema.Instance.Encode((string)bcs1.Raw));
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            break;
                        case SymbolKind.BaseOpSymb:
                            if (x.Symbol != t.Owner.RangeSymbol)
                            {
                                throw new InvalidOperationException("Not a type term");
                            }

                            OpenEnum(wr, isFirstComponent, ref enumState);
                            if (x.Args[0] == x.Args[1])
                            {
                                bcs1 = (BaseCnstSymb)x.Args[0].Symbol;
                                wr.Write(((Rational)bcs1.Raw).ToString());
                            }
                            else
                            {
                                bcs1 = (BaseCnstSymb)x.Args[0].Symbol;
                                bcs2 = (BaseCnstSymb)x.Args[1].Symbol;
                                wr.Write(string.Format("{0}..{1}", (Rational)bcs1.Raw, (Rational)bcs2.Raw));
                            }

                            break;
                        case SymbolKind.BaseSortSymb:
                            CloseEnum(wr, isFirstComponent, ref enumState);
                            success = ASTSchema.Instance.TryGetSortName(((BaseSortSymb)x.Symbol).SortKind, out name);
                            Contract.Assert(success);
                            wr.Write(name);
                            break;
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            CloseEnum(wr, isFirstComponent, ref enumState);
                            for (int i = 0; i < x.Symbol.Arity; ++i)
                            {
                                if (i == 0)
                                {
                                    wr.Write(string.Format("{0}(", ((UserSymbol)x.Symbol).FullName));
                                }

                                PrintTypeTerm(x.Args[i], wr, cancel, envParams);
                                wr.Write(i == x.Symbol.Arity - 1 ? ")" : ", ");
                            }

                            break;
                        case SymbolKind.UnnSymb:
                            CloseEnum(wr, isFirstComponent, ref enumState);
                            wr.Write(((UnnSymb)x.Symbol).FullName);
                            break;
                        case SymbolKind.UserCnstSymb:
                            OpenEnum(wr, isFirstComponent, ref enumState);
                            if (x.Groundness != Groundness.Ground)
                            {
                                throw new InvalidOperationException("Not a type term");
                            }

                            wr.Write(((UserCnstSymb)x.Symbol).FullName);
                            break;
                        case SymbolKind.UserSortSymb:
                            CloseEnum(wr, isFirstComponent, ref enumState);
                            wr.Write(((UserSortSymb)x.Symbol).DataSymbol.FullName);
                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    isFirstComponent = false;
                });

            if (enumState != TypeEnumState.None)
            {
                wr.Write("}");
            }
        }

        private static IEnumerable<Term> PrintTerm_Unfold(Term t, TextWriter wr)
        {
            if (t.Symbol.Arity == 0)
            {
                yield break;
            }

            wr.Write("(");
            for (int i = 0; i < t.Args.Length; ++i)
            {
                yield return t.Args[i];
                if (i < t.Args.Length - 1)
                {
                    wr.Write(", ");
                }
                else
                {
                    wr.Write(")");
                }
            }
        }

        private static void OpenEnum(TextWriter wr, bool isFirstElement, ref TypeEnumState state)
        {
            switch (state)
            {
                case TypeEnumState.None:
                    if (!isFirstElement)
                    {
                        wr.Write(" + ");
                    }

                    state = TypeEnumState.Opened;
                    wr.Write("{");
                    break;
                case TypeEnumState.Opened:
                    wr.Write(", ");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static void CloseEnum(TextWriter wr, bool isFirstElement, ref TypeEnumState state)
        {
            switch (state)
            {
                case TypeEnumState.Opened:
                    state = TypeEnumState.None;
                    wr.Write("}");
                    break;
                case TypeEnumState.None:
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (!isFirstElement)
            {
                wr.Write(" + ");
            }
        }
    }
}
