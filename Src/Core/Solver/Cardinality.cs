namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    /// <summary>
    /// Represents the cardinality of a set. A cardinality can be a finite number or the value "infinity".
    /// Infinite sets are assumed to be of the same size.
    /// </summary>
    public struct Cardinality
    {
        private static readonly Cardinality infinity = new Cardinality(true);
        private static readonly Cardinality zero = new Cardinality(BigInteger.Zero);
        private static readonly Cardinality one = new Cardinality(BigInteger.One);
        /// <summary>
        /// -1 is used to represent infinity
        /// </summary>
        private BigInteger value;

        public static Cardinality Infinity
        {
            get { return infinity; }
        }

        public static Cardinality One
        {
            get { return one; }
        }

        public static Cardinality Zero
        {
            get { return zero; }
        }

        public Cardinality(BigInteger value)
        {
            Contract.Requires(value.Sign >= 0);
            this.value = value;
        }

        private Cardinality(bool isInfinity)
        {
            value = isInfinity ? BigInteger.MinusOne : BigInteger.Zero;
        }

        public static bool operator ==(Cardinality v1, Cardinality v2)
        {
            return v1.value == v2.value;
        }

        public static bool operator !=(Cardinality v1, Cardinality v2)
        {
            return v1.value != v2.value;
        }

        public static bool operator <(Cardinality v1, Cardinality v2)
        {
            if (v1.value == infinity.value)
            {
                return false;
            }
            else if (v2.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value < v2.value;
            }
        }

        public static bool operator <(Cardinality v1, int v2)
        {
            if (v1.value == infinity.value)
            {
                return false;
            }
            else
            {
                return v1.value < v2;
            }
        }

        public static bool operator <=(Cardinality v1, Cardinality v2)
        {
            if (v1.value == infinity.value)
            {
                return v2.value == infinity.value;
            }
            else if (v2.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value <= v2.value;
            }
        }

        public static bool operator <=(Cardinality v1, int v2)
        {
            if (v1.value == infinity.value)
            {
                return false;
            }
            else
            {
                return v1.value <= v2;
            }
        }

        public static bool operator >(Cardinality v1, Cardinality v2)
        {
            if (v2.value == infinity.value)
            {
                return false;
            }
            else if (v1.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value > v2.value;
            }
        }

        public static bool operator >(Cardinality v1, int v2)
        {
            if (v1.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value > v2;
            }
        }

        public static bool operator >=(Cardinality v1, Cardinality v2)
        {
            if (v2.value == infinity.value)
            {
                return v1.value == infinity.value;
            }
            else if (v1.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value >= v2.value;
            }
        }

        public static bool operator >=(Cardinality v1, int v2)
        {
            if (v1.value == infinity.value)
            {
                return true;
            }
            else
            {
                return v1.value >= v2;
            }
        }

        /// <summary>
        /// Let S1, S2 be disjoint sets with |S1| = v1 and |S2| = v2, then v1 + v2 = |S1 \union S2|.
        /// </summary>
        public static Cardinality operator +(Cardinality v1, Cardinality v2)
        {
            if (v1.value == infinity.value || v2.value == infinity.value)
            {
                return infinity;
            }
            else
            {
                return new Cardinality(v1.value + v2.value);
            }
        }

        /// <summary>
        /// Let S1, S2 be disjoint sets with |S1| = v1 and |S2| = v2, then v1 + v2 = |S1 \union S2|.
        /// </summary>
        public static Cardinality operator +(Cardinality v1, int v2)
        {
            Contract.Requires(v2 >= 0);

            if (v1.value == infinity.value)
            {
                return infinity;
            }
            else
            {
                return new Cardinality(v1.value + v2);
            }
        }

        /// <summary>
        /// Let |S1| = v1 and S2 be a finite subset of S1 where |S2| = v2, then v1 - v2 = |S1 - S2|
        /// </summary>
        public static Cardinality operator -(Cardinality v1, Cardinality v2)
        {
            Contract.Requires(v2 <= v1 && v2 != Infinity);
            return v1.value == infinity.value ? v1 : new Cardinality(v1.value - v2.value);
        }

        /// <summary>
        /// Let |S1| = v1 and S2 be a finite subset of S1 where |S2| = v2, then v1 - v2 = |S1 - S2|
        /// </summary>
        public static Cardinality operator -(Cardinality v1, int v2)
        {
            Contract.Requires(v1 >= v2 && v2 >= 0);
            return v1.value == infinity.value ? v1 : new Cardinality(v1.value - v2);
        }

        /// <summary>
        /// Divides a finite cardinality by a non-zero finite cardinality
        /// </summary>
        public static Cardinality operator /(Cardinality v1, Cardinality v2)
        {
            Contract.Requires(v1 != Infinity && v2 != Zero && v2 != Infinity);
            return new Cardinality(v1.value / v2.value);
        }

        /// <summary>
        /// Divides a finite cardinality by a non-zero finite cardinality
        /// </summary>
        public static Cardinality operator /(Cardinality v1, int v2)
        {
            Contract.Requires(v1 != Infinity && v2 > 0);
            return new Cardinality(v1.value / v2);
        }

        /// <summary>
        /// Let S1, S2 be sets where |S1| = v1 and |S2| = v2, then v1 * v2 = |S1 \times S2|.
        /// </summary>
        public static Cardinality operator *(Cardinality v1, Cardinality v2)
        {
            if (v1.value == zero.value || v2.value == zero.value)
            {
                return zero;
            }
            else if (v1.value == infinity.value || v2.value == infinity.value)
            {
                return infinity;
            }
            else
            {
                return new Cardinality(v1.value * v2.value);
            }
        }

        /// <summary>
        /// Let S1, S2 be sets where |S1| = v1 and |S2| = v2, then v1 * v2 = |S1 \times S2|.
        /// </summary>
        public static Cardinality operator *(Cardinality v1, int v2)
        {
            Contract.Requires(v2 >= 0);

            if (v1.value == zero.value || v2 == 0)
            {
                return zero;
            }
            else
            {
                return v1.value == infinity.value ? v1 : new Cardinality(v1.value * v2);
            }
        }

        public static Cardinality Min(Cardinality v1, Cardinality v2)
        {
            return v1 <= v2 ? v1 : v2;
        }

        public static Cardinality Max(Cardinality v1, Cardinality v2)
        {
            return v1 >= v2 ? v1 : v2;
        }

        public static explicit operator BigInteger(Cardinality c)
        {
            if (c == Cardinality.Infinity)
            {
                throw new InvalidCastException("Cannot cast infinity to a big integer");
            }
            else
            {
                return c.value;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Cardinality))
            {
                return false;
            }

            return value == ((Cardinality)obj).value;
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value == infinity.value ? "INFTY" : value.ToString();
        }
    }
}
