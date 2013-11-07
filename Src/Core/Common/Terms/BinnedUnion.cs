namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common.Extras;

    internal partial class TermIndex
    {
        /// <summary>
        /// Organizes the components of a type union into bins.
        /// </summary>
        private class BinnedUnion
        {
            private TermIndex index;
            private Term sourceTerm;

            /// <summary>
            /// Contains all integral constants / ranges.
            /// </summary>
            private IntIntervals intervals = null;

            /// <summary>
            /// Maps from symbols to term bins. 
            /// (1) A term f(...) is placed in a bin labeled by f, as well as the user sort labeled by f.
            /// (2) A string constant and the string sort is in a bin labeled by the string sort
            /// (3) A numeric constant/sort is placed in a bin labeled by Real, unless it is an integral constant / range.
            /// (4) All integral constants ranges are placed in intervals.
            /// (5) All user constants are placed in a bin labeled by TRUE.
            /// </summary>
            private Map<Symbol, Set<Term>> binMap =
                new Map<Symbol, Set<Term>>(Symbol.Compare);

            /// <summary>
            /// Cached symbols
            /// </summary>
            private Symbol unnSymb;
            private Symbol rngSymb;
            private Symbol realSymb;
            private Symbol stringSymb;
            private Symbol trueSymb;

            public Term Term
            {
                get
                {
                    Contract.Assert(sourceTerm != null);
                    return sourceTerm;
                }
            }

            public BinnedUnion(Term t)
            {
                Contract.Requires(t != null && t.Groundness != Groundness.Variable);
                unnSymb = t.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn);
                rngSymb = t.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Range);
                realSymb = t.Owner.SymbolTable.GetSortSymbol(BaseSortKind.Real);
                stringSymb = t.Owner.SymbolTable.GetSortSymbol(BaseSortKind.String);

                trueSymb = (UserSymbol)t.Owner.TrueValue.Symbol;
                index = t.Owner;
                sourceTerm = t;
                t.Visit(ExpandUnions, Add);
            }

            /// <summary>
            /// Private constructor for building intersections.
            /// </summary>
            private BinnedUnion(TermIndex index)
            {
                Contract.Requires(index != null);
                unnSymb = index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn);
                rngSymb = index.SymbolTable.GetOpSymbol(ReservedOpKind.Range);
                realSymb = index.SymbolTable.GetSortSymbol(BaseSortKind.Real);
                stringSymb = index.SymbolTable.GetSortSymbol(BaseSortKind.String);

                trueSymb = (UserSymbol)index.TrueValue.Symbol;
                sourceTerm = null;
                this.index = index;
            }

            internal bool MkIntersection(Term t, out Term tintr)
            {
                Contract.Assert(t != null && t.Owner == Term.Owner);
                if (t == Term)
                {
                    tintr = t;
                    return true;
                }

                var result = new DelayedIntersection(this, t);
                var intrStack = new Stack<DelayedIntersection>();
                intrStack.Push(result);

                //// Uses a depth-first expansion to compute
                //// f(t1,...,tn) /\ f(t1',...,tn') as 
                //// f(t1 /\ t1',..., tn /\ tn').
                int pos;
                Term max;
                bool wasAdded;
                Set<Term> binA, binB;
                BinnedUnion unnA = null, unnB = null, intr = null;
                DelayedIntersection top;
                while (intrStack.Count > 0)
                {
                    top = intrStack.Peek();
                    if (top.MoveBin(out pos))
                    {
                        unnA = top.AUnions[pos];
                        unnB = top.BUnions[pos];
                        intr = top.Results[pos];
                    }
                    else
                    {
                        intrStack.Pop();
                        continue;
                    }

                    foreach (var kv in unnA.binMap)
                    {
                        binA = kv.Value;
                        if (!unnB.binMap.TryFindValue(kv.Key, out binB))
                        {
                            continue;
                        }

                        if (kv.Key.Kind == SymbolKind.ConSymb)
                        {
                            max = index.MkApply(((ConSymb)kv.Key).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        }
                        else if (kv.Key.Kind == SymbolKind.MapSymb)
                        {
                            max = index.MkApply(((MapSymb)kv.Key).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        }
                        else if (kv.Key == stringSymb)
                        {
                            IntersectStrings(binA, binB, top.Results[pos]);
                            continue;
                        }
                        else if (kv.Key == trueSymb)
                        {
                            IntersectUsrConsts(binA, binB, top.Results[pos]);
                            continue;
                        }
                        else if (kv.Key == realSymb)
                        {
                            IntersectNumerics(
                                binA,
                                top.AUnions[pos].intervals,
                                binB,
                                top.BUnions[pos].intervals,
                                top.Results[pos]);
                            continue;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        if (binA.Contains(max))
                        {
                            foreach (var b in binB)
                            {
                                intr.Add(b);
                            }

                            continue;
                        }
                        else if (binB.Contains(max))
                        {
                            foreach (var a in binA)
                            {
                                intr.Add(a);
                            }

                            continue;
                        }

                        //// Now both bins only contain terms starting with f(....)
                        foreach (var a in binA)
                        {
                            if (binB.Contains(a))
                            {
                                intr.Add(a);
                                continue;
                            }
                            else if (a.Groundness == Groundness.Ground)
                            {
                                foreach (var b in binB)
                                {
                                    if (index.IsGroundMember(b, a))
                                    {
                                        intr.Add(a);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                foreach (var b in binB)
                                {
                                    if (b.Groundness == Groundness.Ground)
                                    {
                                        if (index.IsGroundMember(a, b))
                                        {
                                            intr.Add(b);
                                        }
                                    }
                                    else
                                    {
                                        intrStack.Push(new DelayedIntersection(a.Symbol, a.Args, b.Args, top));
                                    }
                                }
                            }
                        }
                    }
                }

                if (result.Results[0].binMap.Count == 0)
                {
                    tintr = null;
                    return false;
                }
                else
                {
                    tintr = result.Results[0].Term;
                    return true;
                }
            }

            private void IntersectUsrConsts(Set<Term> s1, Set<Term> s2, BinnedUnion result)
            {
                foreach (var t in s1)
                {
                    if (s2.Contains(t))
                    {
                        result.Add(t);
                    }
                }
            }

            private void IntersectNumerics(Set<Term> s1, IntIntervals i1, Set<Term> s2, IntIntervals i2, BinnedUnion result)
            {
                bool wasAdded;
                var realTrm = index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded);
                var intTrm = index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded);
                var natTrm = index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded);
                var posTrm = index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded);
                var negTrm = index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded);

                //// Handle the case where one side contains Real
                if (s1.Contains(realTrm))
                {
                    foreach (var t in s2)
                    {
                        result.Add(t);
                    }

                    if (i2 != null)
                    {
                        foreach (var kv in i2.CanonicalForm)
                        {
                            result.Add(index.MkApply(
                                        rngSymb,
                                        new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                 index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                        out wasAdded));
                        }
                    }

                    return;
                }
                else if (s2.Contains(realTrm))
                {
                    foreach (var t in s1)
                    {
                        result.Add(t);
                    }

                    if (i1 != null)
                    {
                        foreach (var kv in i1.CanonicalForm)
                        {
                            result.Add(index.MkApply(
                                        rngSymb,
                                        new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                 index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                        out wasAdded));
                        }
                    }

                    return;
                }
                else
                {
                    //// Need to keep all real constants common to both sets.
                    var sA = s1.Count <= s2.Count ? s1 : s2;
                    var sB = s2.Count >= s1.Count ? s2 : s1;
                    foreach (var t in sA)
                    {
                        if (t.Symbol.Kind == SymbolKind.BaseCnstSymb && sB.Contains(t))
                        {
                            result.Add(t);
                        }
                    }
                }

                //// Handle the case where one side contains Integer.
                if (s1.Contains(intTrm))
                {
                    foreach (var t in s2)
                    {
                        if (t.Symbol.Kind != SymbolKind.BaseCnstSymb ||
                            s1.Contains(t))
                        {
                            result.Add(t);
                        }
                    }

                    if (i2 != null)
                    {
                        foreach (var kv in i2.CanonicalForm)
                        {
                            result.Add(index.MkApply(
                                        rngSymb,
                                        new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                 index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                        out wasAdded));
                        }
                    }

                    return;
                }
                else if (s2.Contains(intTrm))
                {
                    foreach (var t in s1)
                    {
                        if (t.Symbol.Kind != SymbolKind.BaseCnstSymb ||
                            s2.Contains(t))
                        {
                            result.Add(t);
                        }

                        result.Add(t);
                    }

                    if (i1 != null)
                    {
                        foreach (var kv in i1.CanonicalForm)
                        {
                            result.Add(index.MkApply(
                                        rngSymb,
                                        new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                 index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                        out wasAdded));
                        }
                    }

                    return;
                }

                //// Neither set contains all reals or all integers.
                //// First take intersections of intervals: O(n)
                if (i1 != null && i2 != null)
                {
                    BigInteger aS, aE, bS, bE, s, e;
                    using (var itA = i1.CanonicalForm.GetEnumerator())
                    {
                        using (var itB = i2.CanonicalForm.GetEnumerator())
                        {
                            var cont = itA.MoveNext() && itB.MoveNext();
                            while (cont)
                            {
                                aS = itA.Current.Key;
                                aE = itA.Current.Value;

                                bS = itB.Current.Key;
                                bE = itB.Current.Value;

                                s = aS > bS ? aS : bS;
                                e = aE < bE ? aE : bE;
                                cont = aE <= bE ? itA.MoveNext() : itB.MoveNext();

                                if (s <= e)
                                {
                                    result.Add(index.MkApply(
                                                rngSymb,
                                                new Term[] { index.MkCnst(new Rational(s, BigInteger.One), out wasAdded),
                                                         index.MkCnst(new Rational(e, BigInteger.One), out wasAdded) },
                                                out wasAdded));
                                }
                            }
                        }
                    }
                }

                //// Next consider intervals intersecting with Nat, Pos, and Neg.
                if (i2 != null)
                {
                    bool hasNat = s1.Contains(natTrm);
                    bool hasPos = s1.Contains(posTrm);
                    bool hasNeg = s1.Contains(negTrm);
                    if (hasNat || hasPos || hasNeg)
                    {
                        var nonNegLB = hasNat ? BigInteger.Zero : BigInteger.One;
                        foreach (var kv in i2.CanonicalForm)
                        {
                            if (hasNeg)
                            {
                                if (kv.Key < BigInteger.Zero)
                                {
                                    result.Add(index.MkApply(
                                                rngSymb,
                                                new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                         index.MkCnst(new Rational(BigInteger.Min(kv.Value, BigInteger.MinusOne), BigInteger.One), out wasAdded) },
                                                out wasAdded));
                                }
                            }

                            if (hasNat || hasPos)
                            {
                                if (kv.Value >= nonNegLB)
                                {
                                    result.Add(index.MkApply(
                                                rngSymb,
                                                new Term[] { index.MkCnst(new Rational(BigInteger.Max(kv.Key, nonNegLB), BigInteger.One), out wasAdded),
                                                         index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                                out wasAdded));
                                }
                            }
                        }
                    }
                }

                if (i1 != null)
                {
                    bool hasNat = s2.Contains(natTrm);
                    bool hasPos = s2.Contains(posTrm);
                    bool hasNeg = s2.Contains(negTrm);
                    if (hasNat || hasPos || hasNeg)
                    {
                        var nonNegLB = hasNat ? BigInteger.Zero : BigInteger.One;
                        foreach (var kv in i1.CanonicalForm)
                        {
                            if (hasNeg)
                            {
                                if (kv.Key < BigInteger.Zero)
                                {
                                    result.Add(index.MkApply(
                                                rngSymb,
                                                new Term[] { index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded),
                                                         index.MkCnst(new Rational(BigInteger.Min(kv.Value, BigInteger.MinusOne), BigInteger.One), out wasAdded) },
                                                out wasAdded));
                                }
                            }

                            if (hasNat || hasPos)
                            {
                                if (kv.Value >= nonNegLB)
                                {
                                    result.Add(index.MkApply(
                                                rngSymb,
                                                new Term[] { index.MkCnst(new Rational(BigInteger.Max(kv.Key, nonNegLB), BigInteger.One), out wasAdded),
                                                         index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded) },
                                                out wasAdded));
                                }
                            }
                        }
                    }
                }

                //// Finally, consider intersections between the sorts Nat, Pos, Neg.
                if (s1.Contains(natTrm))
                {
                    if (s2.Contains(natTrm))
                    {
                        result.Add(natTrm);
                    }
                    else if (s2.Contains(posTrm))
                    {
                        result.Add(posTrm);
                    }
                }
                else if (s2.Contains(natTrm))
                {
                    if (s1.Contains(natTrm))
                    {
                        result.Add(natTrm);
                    }
                    else if (s1.Contains(posTrm))
                    {
                        result.Add(posTrm);
                    }
                }
                else if (s1.Contains(posTrm) && s2.Contains(posTrm))
                {
                    result.Add(posTrm);
                }

                if (s1.Contains(negTrm) && s2.Contains(negTrm))
                {
                    result.Add(negTrm);
                }
            }

            private void IntersectStrings(Set<Term> s1, Set<Term> s2, BinnedUnion result)
            {
                bool wasAdded;
                var stringTerm = index.MkApply(stringSymb, TermIndex.EmptyArgs, out wasAdded);
                if (s1.Contains(stringTerm))
                {
                    foreach (var t in s2)
                    {
                        result.Add(t);
                    }
                }
                else if (s2.Contains(stringTerm))
                {
                    foreach (var t in s1)
                    {
                        result.Add(t);
                    }
                }
                else
                {
                    foreach (var t in s1)
                    {
                        if (s2.Contains(t))
                        {
                            result.Add(t);
                        }
                    }
                }
            }

            private IEnumerable<Term> ExpandUnions(Term t)
            {
                if (t.Symbol == unnSymb)
                {
                    yield return t.Args[0];
                    yield return t.Args[1];
                }
                else if (t.Symbol.Kind == SymbolKind.UnnSymb)
                {
                    yield return t.Owner.GetCanonicalTerm((UserSymbol)t.Symbol, 0);
                }
            }

            /// <summary>
            /// Create a type term from this union. 
            /// </summary>
            private void SetTerm()
            {
                Contract.Assert(sourceTerm == null);
                if (binMap.Count == 0)
                {
                    return;
                }

                Term t = null;
                bool wasAdded;
                //// Step 1. Create an enum of all non-integral constants.
                foreach (var bin in binMap.Values)
                {
                    foreach (var tp in bin)
                    {
                        t = t == null ? tp : index.MkApply(unnSymb, new Term[] { tp, t }, out wasAdded);
                    }
                }

                //// Step 2. Create an enum of all integer intervals
                if (intervals != null)
                {
                    Term beg, end;
                    foreach (var kv in intervals.CanonicalForm)
                    {
                        beg = index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded);
                        end = index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded);
                        t = t == null ? index.MkApply(rngSymb, new Term[] { beg, end }, out wasAdded)
                                      : index.MkApply(unnSymb, new Term[] { index.MkApply(rngSymb, new Term[] { beg, end }, out wasAdded), t }, out wasAdded);
                    }
                }

                Contract.Assert(t != null);
                sourceTerm = t;
            }

            private void Add(Term t)
            {
                if (t.Symbol == unnSymb)
                {
                    return;
                }

                bool wasAdded;
                Set<Term> bin;
                switch (t.Symbol.Kind)
                {
                    case SymbolKind.ConSymb:
                    case SymbolKind.MapSymb:
                        {
                            if (!binMap.TryFindValue(t.Symbol, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(t.Symbol, bin);
                            }

                            Term max = null;
                            if (t.Symbol.Kind == SymbolKind.ConSymb)
                            {
                                max = index.MkApply(((ConSymb)t.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                            }
                            else
                            {
                                max = index.MkApply(((MapSymb)t.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                            }

                            if (bin.Contains(max))
                            {
                                return;
                            }
                            else
                            {
                                bin.Add(t);
                            }

                            break;
                        }
                    case SymbolKind.UserSortSymb:
                        {
                            if (!binMap.TryFindValue(((UserSortSymb)t.Symbol).DataSymbol, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(((UserSortSymb)t.Symbol).DataSymbol, bin);
                                bin.Add(t);
                            }
                            else if (!bin.Contains(t))
                            {
                                bin.Clear();
                                bin.Add(t);
                            }

                            break;
                        }
                    case SymbolKind.UserCnstSymb:
                        {
                            Contract.Assert(t.Symbol.IsNonVarConstant);
                            if (!binMap.TryFindValue(trueSymb, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(trueSymb, bin);
                            }

                            bin.Add(t);
                            break;
                        }
                    case SymbolKind.BaseSortSymb:
                        {
                            var baseSort = ((BaseSortSymb)t.Symbol);
                            var binName = baseSort.SortKind == BaseSortKind.String ? stringSymb : realSymb;
                            if (!binMap.TryFindValue(binName, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(binName, bin);
                            }

                            switch (baseSort.SortKind)
                            {
                                case BaseSortKind.String:
                                    if (!bin.Contains(t))
                                    {
                                        bin.Clear();
                                        bin.Add(t);
                                    }

                                    break;
                                case BaseSortKind.Real:
                                    if (!bin.Contains(t))
                                    {
                                        bin.Clear();
                                        bin.Add(t);
                                        intervals = null;
                                    }

                                    break;
                                case BaseSortKind.Integer:
                                    if (!bin.Contains(t) &&
                                        !bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)))
                                    {
                                        bin.Remove(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded));
                                        bin.Remove(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded));
                                        bin.Remove(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded));
                                        bin.Add(t);
                                        intervals = null;
                                    }

                                    break;
                                case BaseSortKind.Natural:
                                    if (!bin.Contains(t) &&
                                        !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded)) &&
                                        !bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)))
                                    {
                                        bin.Remove(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded));
                                        bin.Add(t);
                                        if (intervals != null && intervals.Count > 0)
                                        {
                                            BigInteger min, max;
                                            intervals.GetExtrema(out min, out max);
                                            intervals.Remove(BigInteger.Zero, BigInteger.Max(BigInteger.Zero, max));
                                        }
                                    }

                                    break;
                                case BaseSortKind.PosInteger:
                                    if (!bin.Contains(t) &&
                                        !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded)) &&
                                        !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded)) &&
                                        !bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)))
                                    {
                                        bin.Add(t);
                                        if (intervals != null && intervals.Count > 0)
                                        {
                                            BigInteger min, max;
                                            intervals.GetExtrema(out min, out max);
                                            intervals.Remove(BigInteger.One, BigInteger.Max(BigInteger.One, max));
                                        }
                                    }

                                    break;
                                case BaseSortKind.NegInteger:
                                    if (!bin.Contains(t) &&
                                        !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded)) &&
                                        !bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)))
                                    {
                                        bin.Add(t);
                                        if (intervals != null && intervals.Count > 0)
                                        {
                                            BigInteger min, max;
                                            intervals.GetExtrema(out min, out max);
                                            intervals.Remove(BigInteger.Min(min, BigInteger.MinusOne), BigInteger.MinusOne);
                                        }
                                    }

                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            break;
                        }
                    case SymbolKind.BaseCnstSymb:
                        {
                            var baseCnst = (BaseCnstSymb)t.Symbol;
                            var binName = baseCnst.CnstKind == CnstKind.String ? stringSymb : realSymb;
                            if (!binMap.TryFindValue(binName, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(binName, bin);
                            }

                            switch (baseCnst.CnstKind)
                            {
                                case CnstKind.String:
                                    if (!bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.String), TermIndex.EmptyArgs, out wasAdded)))
                                    {
                                        bin.Add(t);
                                    }

                                    break;
                                case CnstKind.Numeric:
                                    {
                                        var rat = (Rational)baseCnst.Raw;
                                        if (bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)))
                                        {
                                        }
                                        else if (!rat.IsInteger)
                                        {
                                            bin.Add(t);
                                        }
                                        else if (bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded)))
                                        {
                                        }
                                        else if (rat.Sign < 0)
                                        {
                                            if (!bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded)))
                                            {
                                                intervals = intervals == null ? new IntIntervals() : intervals;
                                                intervals.Add(rat.Numerator, rat.Numerator);
                                            }
                                        }
                                        else if (rat.Sign == 0)
                                        {
                                            if (!bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded)))
                                            {
                                                intervals = intervals == null ? new IntIntervals() : intervals;
                                                intervals.Add(rat.Numerator, rat.Numerator);
                                            }
                                        }
                                        else
                                        {
                                            if (!bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded)) &&
                                                !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded)))
                                            {
                                                intervals = intervals == null ? new IntIntervals() : intervals;
                                                intervals.Add(rat.Numerator, rat.Numerator);
                                            }
                                        }

                                        break;
                                    }
                                default:
                                    throw new NotImplementedException();
                            }

                            break;
                        }
                    case SymbolKind.BaseOpSymb:
                        {
                            Contract.Assert(t.Symbol == rngSymb);
                            if (!binMap.TryFindValue(realSymb, out bin))
                            {
                                bin = new Set<Term>(Term.Compare);
                                binMap.Add(realSymb, bin);
                            }

                            if (!bin.Contains(index.MkApply(realSymb, TermIndex.EmptyArgs, out wasAdded)) &&
                                !bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded)))
                            {
                                var end1 = ((Rational)((BaseCnstSymb)t.Args[0].Symbol).Raw).Numerator;
                                var end2 = ((Rational)((BaseCnstSymb)t.Args[1].Symbol).Raw).Numerator;
                                intervals = intervals == null ? new IntIntervals() : intervals;
                                intervals.Add(end1, end2);
                                BigInteger min, max;
                                intervals.GetExtrema(out min, out max);

                                if (bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded)))
                                {
                                    intervals.Remove(BigInteger.Zero, BigInteger.Max(BigInteger.Zero, max));
                                }
                                else if (bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded)))
                                {
                                    intervals.Remove(BigInteger.One, BigInteger.Max(BigInteger.One, max));
                                }

                                if (bin.Contains(index.MkApply(index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded)))
                                {
                                    intervals.Remove(BigInteger.Min(BigInteger.MinusOne, min), BigInteger.MinusOne);
                                }
                            }

                            break;
                        }
                    default:
                        throw new InvalidOperationException();
                }
            }

            private class DelayedIntersection
            {
                private int current = -1;
                private DelayedIntersection parent;

                public Symbol OuterSymbol
                {
                    get;
                    private set;
                }

                public BinnedUnion[] AUnions
                {
                    get;
                    private set;
                }

                public BinnedUnion[] BUnions
                {
                    get;
                    private set;
                }

                public BinnedUnion[] Results
                {
                    get;
                    private set;
                }

                public DelayedIntersection(Symbol s, ImmutableArray<Term> tAs, ImmutableArray<Term> tBs, DelayedIntersection parent)
                {
                    Contract.Requires(s != null && tAs != null && tBs != null && parent != null);
                    Contract.Requires(tAs.Length > 0 && tBs.Length > 0 && tAs.Length == tBs.Length);
                    Contract.Requires(s.Arity == tAs.Length);

                    OuterSymbol = s;
                    AUnions = new BinnedUnion[tAs.Length];
                    BUnions = new BinnedUnion[tAs.Length];
                    Results = new BinnedUnion[tAs.Length];
                    var owner = tAs[0].Owner;
                    for (int i = 0; i < tAs.Length; ++i)
                    {
                        AUnions[i] = new BinnedUnion(tAs[i]);
                        BUnions[i] = new BinnedUnion(tBs[i]);
                        Results[i] = new BinnedUnion(owner);
                    }

                    this.parent = parent;
                }

                public DelayedIntersection(BinnedUnion tA, Term tB)
                {
                    Contract.Requires(tA != null && tB != null);
                    OuterSymbol = null;
                    parent = null;
                    AUnions = new BinnedUnion[1];
                    BUnions = new BinnedUnion[1];
                    Results = new BinnedUnion[1];
                    AUnions[0] = tA;
                    BUnions[0] = new BinnedUnion(tB);
                    Results[0] = new BinnedUnion(tA.index);
                }

                public bool MoveBin(out int next)
                {
                    if (current < 0)
                    {
                        current = next = 0;
                        return true;
                    }
                    else if (current > Results.Length - 1)
                    {
                        next = current;
                        return false;
                    }
                    else if (Results[current].binMap.Count == 0)
                    {
                        next = Results.Length;
                        return false;
                    }
                    else if (current == Results.Length - 1)
                    {
                        Results[current].SetTerm();
                        current = next = Results.Length;
                        if (parent != null)
                        {
                            var args = new Term[Results.Length];
                            for (int i = 0; i < Results.Length; ++i)
                            {
                                args[i] = Results[i].Term;
                            }

                            bool wasAdded;
                            var pBin = parent.Results[parent.current];
                            pBin.Add(pBin.index.MkApply(OuterSymbol, args, out wasAdded));
                        }

                        return false;
                    }
                    else
                    {
                        Results[current].SetTerm();
                        next = ++current;
                        return true;
                    }
                }
            }
        }
    }
}