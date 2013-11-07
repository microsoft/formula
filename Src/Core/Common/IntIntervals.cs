namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    internal class IntIntervals
    {
        /// <summary>
        /// A map from the starting points of intervals to their endpoints
        /// </summary>
        private Map<BigInteger, BigInteger> starts = new Map<BigInteger, BigInteger>(BigInteger.Compare);

        /// <summary>
        /// A map from the ending points of intervals to their starting points
        /// </summary>
        private Map<BigInteger, BigInteger> ends = new Map<BigInteger, BigInteger>(BigInteger.Compare);

        /// <summary>
        /// A unique representation of the intervals as a minimal set of sorted, non-overlapping, intervals.        
        /// </summary>
        public IEnumerable<KeyValuePair<BigInteger, BigInteger>> CanonicalForm
        {
            get { return starts; }
        }

        /// <summary>
        /// Returns the number of intervals in the canonical form.
        /// </summary>
        public int Count
        {
            get { return starts.Count; }
        }

        /// <summary>
        /// Returns an new set of intervals containing the intersection of the inputs.
        /// </summary>
        public static IntIntervals MkIntersection(IntIntervals i1, IntIntervals i2)
        {
            Contract.Requires(i1 != null && i2 != null);
            if (i1 == i2)
            {
                return i1.Clone();
            }

            var intrs = new IntIntervals();
            if (i1.Count == 0 || i2.Count == 0)
            {
                return intrs;
            }

            BigInteger left, right;
            using (var it1 = i1.starts.GetEnumerator())
            {
                it1.MoveNext();
                using (var it2 = i2.starts.GetEnumerator())
                {
                    it2.MoveNext();
                    while (true)
                    {
                        left = BigInteger.Max(it1.Current.Key, it2.Current.Key);
                        right = BigInteger.Min(it1.Current.Value, it2.Current.Value);
                        if (left <= right)
                        {
                            intrs.Add(left, right);
                        }

                        if (it2.Current.Value < it1.Current.Value)
                        {
                            if (!it2.MoveNext())
                            {
                                return intrs;
                            }
                        }
                        else if (!it1.MoveNext())
                        {
                            return intrs;
                        }
                    }
                }
            }
        }

        public void Clear()
        {
            starts.Clear();
            ends.Clear();
        }

        /// <summary>
        /// Counts the number of distinct integers inhabiting all intervals.
        /// </summary>
        public BigInteger GetSize()
        {
            var size = BigInteger.Zero;
            foreach (var kv in starts)
            {
                size += (kv.Value - kv.Key) + 1;
            }

            return size;
        }

        /// <summary>
        /// Returns the smallest and largest integers in the set, if there are any integers in the set.
        /// Otherwise returns false;
        /// </summary>
        public bool GetExtrema(out BigInteger min, out BigInteger max)
        {
            if (starts.Count == 0)
            {
                min = max = BigInteger.Zero;
                return false;
            }

            using (var it = starts.GetEnumerator())
            {
                it.MoveNext();
                min = it.Current.Key;
            }

            using (var it = ends.Reverse.GetEnumerator())
            {
                it.MoveNext();
                max = it.Current.Key;
            }

            return true;
        }

        public void UnionWith(IntIntervals intrvls)
        {
            Contract.Requires(intrvls != null);
            foreach (var intr in intrvls.starts)
            {
                Add(intr.Key, intr.Value);
            }
        }

        public IntIntervals Clone()
        {
            var clone = new IntIntervals();
            clone.starts = new Map<BigInteger, BigInteger>(starts);
            clone.ends = new Map<BigInteger, BigInteger>(ends);
            return clone;
        }

        /// <summary>
        /// Returns true if the interval [end1, end2] is contained within an interval in
        /// this set.
        /// </summary>
        public bool Contains(BigInteger end1, BigInteger end2)
        {
            if (end1 > end2)
            {
                var tmp = end1;
                end1 = end2;
                end2 = tmp;
            }

            BigInteger e1S, e1E, e2S, e2E;
            var e1Ovr = starts.GetGLB(end1, out e1S, out e1E);
            var e2Ovr = ends.GetLUB(end2, out e2E, out e2S);
            return e1Ovr && e2Ovr && e1S == e2S && e1E == e2E;
        }

        /// <summary>
        /// Removes all integers in the inclusive interval [end1, end2].
        /// </summary>
        public void Remove(BigInteger end1, BigInteger end2)
        {
            if (end1 > end2)
            {
                var tmp = end1;
                end1 = end2;
                end2 = tmp;
            }

            BigInteger e1S, e1E, e2S, e2E;
            var e1Ovr = starts.GetGLB(end1, out e1S, out e1E);
            var e2Ovr = ends.GetLUB(end2, out e2E, out e2S);

            //// Then the interval to delete is subsumed by another interval.
            //// There are four cases.
            if (e1Ovr && e2Ovr && e1S == e2S && e1E == e2E)
            {
                if (e1S == end1 && e1E == end2)
                {
                    starts.Remove(e1S);
                    ends.Remove(e1E);                    
                }
                else if (e1S == end1)
                {
                    ends[e1E] = end2 + 1;
                    starts.Remove(end1);
                    starts.Add(end2 + 1, e1E);
                }
                else if (e1E == end2)
                {
                    starts[e1S] = end1 - 1;
                    ends.Remove(end2);
                    ends.Add(end1 - 1, e1S);
                }
                else
                {
                    starts[e1S] = end1 - 1;
                    ends[e1E] = end2 + 1;
                    ends.Add(end1 - 1, e1S);
                    starts.Add(end2 + 1, e1E);
                }

                return;
            }

            //// Otherwise, need to remove all subsumed intervals.
            var subsumed = new Map<BigInteger, BigInteger>(BigInteger.Compare);
            using (var it = e1Ovr ? starts.GetEnumerable(e1S).GetEnumerator() : starts.GetEnumerator())
            {
                while (it.MoveNext())
                {
                    if (it.Current.Key >= end1 && it.Current.Value <= end2)
                    {
                        subsumed.Add(it.Current.Key, it.Current.Value);
                    }
                    else if (it.Current.Value > end2)
                    {
                        break;
                    }
                }
            }

            foreach (var kv in subsumed)
            {
                starts.Remove(kv.Key);
                ends.Remove(kv.Value);
            }

            //// Finally, need to truncate overlapped intervals that are not subsumed
            e1Ovr &= e1E >= end1 && e1S < end1;
            e2Ovr &= e2S <= end2 && e2E > end2;

            if (e1Ovr)
            {
                ends.Remove(e1E);
                starts[e1S] = end1 - 1;
                ends.Add(end1 - 1, e1S);
            }

            if (e2Ovr)
            {
                starts.Remove(e2S);
                ends[e2E] = end2 + 1;
                starts.Add(end2 + 1, e2E);
            }
        }

        /// <summary>
        /// Unions the inclusive interval [end1, end2] with the current set of intervals
        /// </summary>
        public void Add(BigInteger end1, BigInteger end2)
        {
            if (end1 > end2)
            {
                var tmp = end1;
                end1 = end2;
                end2 = tmp;
            }

            BigInteger e1S, e1E, e2S, e2E;
            var e1Ovr = starts.GetGLB(end1, out e1S, out e1E);
            var e2Ovr = ends.GetLUB(end2, out e2E, out e2S);
            if (e1Ovr && e2Ovr && e1S == e2S && e1E == e2E)
            {
                //// Then this new interval is subsumed
                return;
            }
            
            //// Remove subsumed intervals
            var subsumed = new Map<BigInteger, BigInteger>(BigInteger.Compare);
            using (var it = e1Ovr ? starts.GetEnumerable(e1S).GetEnumerator() : starts.GetEnumerator())
            {
                while (it.MoveNext())
                {
                    if (it.Current.Key >= end1 && it.Current.Value <= end2)
                    {
                        subsumed.Add(it.Current.Key, it.Current.Value);
                    }
                    else if (it.Current.Value > end2)
                    {
                        break;
                    }
                }
            }

            foreach (var kv in subsumed)
            {
                starts.Remove(kv.Key);
                ends.Remove(kv.Value);
            }

            //// Now insert the interval; there are four cases
            BigInteger actualStart, actualEnd;

            e1Ovr &= e1E >= end1;
            e2Ovr &= e2S <= end2;

            if (e1Ovr && !e2Ovr)
            {
                starts[e1S] = end2;
                ends.Remove(e1E);
                ends[end2] = e1S;

                actualStart = e1S;
                actualEnd = end2;
            }
            else if (!e1Ovr && e2Ovr)
            {
                ends[e2E] = end1;
                starts.Remove(e2S);
                starts[end1] = e2E;

                actualStart = end1;
                actualEnd = e2E;
            }
            else if (e1Ovr && e2Ovr)
            {
                ends.Remove(e1E);
                starts.Remove(e2S);
                starts[e1S] = e2E;
                ends[e2E] = e1S;

                actualStart = e1S;
                actualEnd = e2E;
            }
            else
            {
                starts.Add(end1, end2);
                ends.Add(end2, end1);

                actualStart = end1;
                actualEnd = end2;
            }

            //// Finally, join contiguous intervals
            if (!e1Ovr)
            {
                BigInteger eprev = actualStart - 1;
                BigInteger sprev;
                if (ends.TryFindValue(eprev, out sprev))
                {
                    starts.Remove(actualStart);
                    ends.Remove(eprev);

                    starts[sprev] = actualEnd;
                    ends[actualEnd] = sprev;
                    actualStart = sprev;
                }
            }

            if (!e2Ovr)
            {
                BigInteger snext = actualEnd + 1;
                BigInteger enext;
                if (starts.TryFindValue(snext, out enext))
                {
                    starts.Remove(snext);
                    ends.Remove(actualEnd);

                    starts[actualStart] = enext;
                    ends[enext] = actualStart;
                }
            }
        }
    }
}
