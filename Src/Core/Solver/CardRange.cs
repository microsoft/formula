namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    public struct CardRange
    {
        private static readonly CardRange all = 
            new CardRange(Cardinality.Zero, Cardinality.Infinity);

        private static readonly CardRange none =
            new CardRange(Cardinality.Zero, Cardinality.Zero);

        private Cardinality lower;
        private Cardinality upper;

        /// <summary>
        /// Returns the cardinality range zero to infinity.
        /// </summary>
        public static CardRange All
        {
            get { return all; }
        }

        /// <summary>
        /// Returns the cardinality range zero to zero.
        /// </summary>
        public static CardRange None
        {
            get { return none; }
        }

        public Cardinality Lower
        {
            get { return lower; }
        }

        public Cardinality Upper
        {
            get { return upper; }
        }

        public CardRange(Cardinality lower, Cardinality upper)
        {
            Contract.Requires(lower <= upper);
            this.lower = lower;
            this.upper = upper;
        }

        public bool TryIntersect(CardRange r, out CardRange intersection)
        {
            var min = Cardinality.Max(lower, r.lower);
            var max = Cardinality.Min(upper, r.upper);

            if (max < min)
            {
                intersection = all;
                return false;
            }
            else
            {
                intersection = new CardRange(min, max);
                return true;
            }
        }

        /// <summary>
        /// Range must be finite with midpoint the center of the range.
        /// If shiftLower then returns the range [midpoint, upper].
        /// Otherwise returns the range [lower, midpoint].
        /// </summary>
        public CardRange Bisect(bool shiftLower)
        {
            Contract.Requires(Upper != Cardinality.Infinity);
            var mid = (lower + upper) / 2;
            if (shiftLower)
            {
                return new CardRange(mid, upper);
            }
            else
            {
                return new CardRange(lower, mid);
            }
        }

        public static bool operator ==(CardRange r1, CardRange r2)
        {
            return (r1.lower == r2.lower) && (r1.upper == r2.upper);
        }

        public static bool operator !=(CardRange r1, CardRange r2)
        {
            return (r1.lower != r2.lower) || (r1.upper != r2.upper);
        }

        public static CardRange operator *(CardRange r1, CardRange r2)
        {
            return new CardRange(r1.Lower * r2.Lower, r1.Upper * r2.Upper);
        }

        public static CardRange operator +(CardRange r1, CardRange r2)
        {
            return new CardRange(r1.Lower + r2.Lower, r1.Upper + r2.Upper);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CardRange))
            {
                return false;
            }

            var r = (CardRange)obj;
            return lower == r.lower && upper == r.upper;
        }

        public override int GetHashCode()
        {
            return (lower + upper).GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("[{0}, {1}]", lower, upper);
        }
    }
}
