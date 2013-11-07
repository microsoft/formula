namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Numerics;

    public struct Rational : IComparable<Rational>, IEquatable<Rational>
    {
        private BigInteger num;
        private BigInteger den;

        public static Rational One
        {
            get { return new Rational(BigInteger.One, BigInteger.One); }
        }

        public static Rational Zero
        {
            get { return new Rational(); }
        }

        public int Sign
        {
            get { return num.Sign; }
        }

        public bool IsInteger
        {
            get { return Denominator.IsOne; }
        }

        public bool IsZero
        {
            get { return Numerator.IsZero; }
        }

        public bool IsOne
        {
            get { return Numerator.IsOne && Denominator.IsOne; }
        }

        public BigInteger Denominator
        {
            get { return den.IsZero ? BigInteger.One : den; }
        }

        public BigInteger Numerator
        {
            get { return num; }
        }

        public Rational(BigInteger num, BigInteger den)
        {
            Contract.Requires(!den.IsZero);
            var gcd = BigInteger.GreatestCommonDivisor(num, den);
            this.num = num / gcd;
            this.den = den / gcd;
            if (den < BigInteger.Zero)
            {
                this.num = BigInteger.Negate(this.num);
                this.den = BigInteger.Negate(this.den);
            }
        }

        public Rational(int i)
        {
            num = new BigInteger(i);
            den = (i == 0) ? BigInteger.Zero : BigInteger.One;
        }

        public Rational(double d)
        {
            ////Contract.Requires(!double.IsInfinity(d) && !double.IsNaN(d));
            if (double.IsInfinity(d) || double.IsNaN(d))
            {
                throw new Exception("Bad user value");
            }

            if (d == 0)
            {
                num = BigInteger.Zero;
                den = BigInteger.Zero;
                return;
            }

            var frac = d - Math.Floor(d);
            if (frac == 0)
            {
                num = new BigInteger(d);
                den = BigInteger.One;
            }
            else
            {
                long pow = 1;
                while (frac > Math.Floor(frac))
                {
                    frac *= 10D;
                    pow *= 10;
                }

                var w = new BigInteger(Math.Floor(d));
                var x = new BigInteger(frac);
                den = new BigInteger(pow);
                num = w * den + x;                
                var gcd = BigInteger.GreatestCommonDivisor(num, den);
                num = num / gcd;
                den = den / gcd;
            }
        }

        public static int Compare(Rational r1, Rational r2)
        {
            return r1.CompareTo(r2);
        }

        public static Rational operator +(Rational r1, Rational r2)
        {
            return new Rational((r1.Numerator * r2.Denominator) + (r2.Numerator * r1.Denominator), r1.Denominator * r2.Denominator);
        }

        public static Rational operator -(Rational r1, Rational r2)
        {
            return new Rational((r1.Numerator * r2.Denominator) - (r2.Numerator * r1.Denominator), r1.Denominator * r2.Denominator);
        }

        public static Rational operator -(Rational r1)
        {
            return new Rational(-r1.Numerator, r1.Denominator);
        }

        public static Rational operator *(Rational r1, Rational r2)
        {
            return new Rational(r1.Numerator * r2.Numerator, r1.Denominator * r2.Denominator);
        }

        public static Rational operator /(Rational r1, Rational r2)
        {
            Contract.Requires(!r2.IsZero);
            return new Rational(r1.Numerator * r2.Denominator, r1.Denominator * r2.Numerator);
        }

        public static bool operator <(Rational r1, Rational r2)
        {
            return r1.CompareTo(r2) < 0;
        }

        public static bool operator >(Rational r1, Rational r2)
        {
            return r1.CompareTo(r2) > 0;
        }

        public static bool operator <=(Rational r1, Rational r2)
        {
            return r1.CompareTo(r2) <= 0;
        }

        public static bool operator >=(Rational r1, Rational r2)
        {
            return r1.CompareTo(r2) >= 0;
        }

        public static bool operator ==(Rational r1, Rational r2)
        {
            return r1.Numerator == r2.Numerator && r1.Denominator == r2.Denominator;
        }

        public static bool operator !=(Rational r1, Rational r2)
        {
            return r1.Numerator != r2.Numerator || r1.Denominator != r2.Denominator;
        }

        /// <summary>
        /// Computes the Euclidian quotient of r1 / r2. 
        /// Specifically, computes q such that 
        /// (1) q \in Z
        /// (2) r1 = r2 * q + rem
        /// (3) 0 &lt;= rem &lt; r2
        /// 
        /// If r2 = 0, then returns 0 and the result does not have an interpretion.
        /// </summary>
        public static Rational Quotient(Rational r1, Rational r2)
        {
            if (r2.IsZero)
            {
                return r2;
            }
            else if (r1.IsZero)
            {
                return r1;
            }

            //// Neither are zero. Compute Quotient(|r1|, |r2|)
            BigInteger rem;
            var absQuotient = BigInteger.DivRem(
                                    BigInteger.Abs(r1.Numerator * r2.Denominator),
                                    BigInteger.Abs(r1.Denominator * r2.Numerator),
                                    out rem);
            //// There are four cases depending on the sign of the operands
            if (r1.Sign > 0 && r2.Sign > 0)
            {
                return new Rational(absQuotient, BigInteger.One);
            }
            else if (r1.Sign > 0 && r2.Sign < 0)
            {
                return new Rational(BigInteger.Negate(absQuotient), BigInteger.One);
            }
            else if (r1.Sign < 0 && r2.Sign > 0)
            {
                return rem.IsZero
                            ? new Rational(BigInteger.Negate(absQuotient), BigInteger.One)
                            : new Rational(BigInteger.Negate(absQuotient) - 1, BigInteger.One);
            }
            else
            {
                return rem.IsZero
                            ? new Rational(absQuotient, BigInteger.One)
                            : new Rational(absQuotient + 1, BigInteger.One);
            }
        }

        /// <summary>
        /// Computes the Euclidian remainder of r1 / r2. 
        /// Specifically, computes rem such that 
        /// (1) q \in Z
        /// (2) r1 = r2 * q + rem
        /// (3) 0 &lt;= rem &lt; r2
        /// 
        /// If r2 = 0, then returns 0 and the result does not have an interpretion.
        /// </summary>
        public static Rational Remainder(Rational r1, Rational r2)
        {
            if (r2.IsZero)
            {
                return Rational.Zero;
            }

            return r1 - (r2 * Quotient(r1, r2));
        }

        public override string ToString()
        {
            return IsInteger ? Numerator.ToString() : string.Format("{0}/{1}", Numerator, Denominator);
        }

        public bool Equals(Rational r)
        {
            return r.Numerator.Equals(Numerator) && r.Denominator.Equals(Denominator);
        }

        public override bool Equals(object obj)
        {
            return obj is Rational && Equals((Rational)obj);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public int CompareTo(Rational r)
        {
            if (Equals(r))
            {
                return 0;
            }
            else if ((this - r).Numerator < BigInteger.Zero)
            {
                return -1;
            }
            else
            {
                return 1;
            }
        }

        public static bool TryParseFraction(string str, out Rational r)
        {
            Contract.Requires(str != null);
            var bar = str.IndexOf('/');
            if (bar < 0)
            {
                BigInteger num;
                if (!BigInteger.TryParse(str, out num))
                {
                    r = Rational.Zero;
                    return false;
                }

                r = new Rational(num, BigInteger.One);
                return true;
            }
            else if (bar == 0 || bar == str.Length - 1)
            {
                r = Rational.Zero;
                return false;
            }
            else
            {
                BigInteger num, den;
                if (!BigInteger.TryParse(str.Substring(0, bar), out num))
                {
                    r = Rational.Zero;
                    return false;
                }

                if (!BigInteger.TryParse(str.Substring(bar + 1), out den))
                {
                    r = Rational.Zero;
                    return false;
                }

                if (den.IsZero)
                {
                    r = Rational.Zero;
                    return false;
                }

                r = new Rational(num, den);
                return true;
            }
        }

        public static bool TryParseDecimal(string str, out Rational r)
        {
            Contract.Requires(str != null);
            var point = str.IndexOf('.');
            if (point < 0)
            {
                BigInteger num;
                if (!BigInteger.TryParse(str, out num))
                {
                    r = Rational.Zero;
                    return false;
                }

                r = new Rational(num, BigInteger.One);
                return true;
            }
            else
            {
                var wholeStr = point == 0 ? "0" : str.Substring(0, point).Trim();
                var fracStr = point == str.Length - 1 ? "0" : str.Substring(point + 1);
                BigInteger whole;
                bool isNeg = wholeStr[0] == '-';

                if (!BigInteger.TryParse(wholeStr, out whole))
                {
                    r = Rational.Zero;
                    return false;
                }

                string trimmedFrac = string.Empty;
                int start = 0;
                char c;
                for (int i = 0; i < fracStr.Length; ++i)
                {
                    c = fracStr[i];
                    if (!char.IsDigit(c))
                    {
                        r = Rational.Zero;
                        return false;
                    }

                    if (c != '0')
                    {
                        trimmedFrac += fracStr.Substring(start, i - start + 1);
                        start = i + 1;
                    }
                }

                if (trimmedFrac == string.Empty)
                {
                    r = new Rational(whole, BigInteger.One);
                }
                else if (isNeg)
                {
                    r = new Rational(whole, BigInteger.One) -
                        new Rational(BigInteger.Parse(trimmedFrac), BigInteger.Pow(10, trimmedFrac.Length));
                }
                else
                {
                    r = new Rational(whole, BigInteger.One) +
                        new Rational(BigInteger.Parse(trimmedFrac), BigInteger.Pow(10, trimmedFrac.Length));
                }

                return true;
            }
        }

        /// <summary>
        /// Converts the rational to a string of decimal digits with a maximum number
        /// of decimal places. The format of the string is: [-]N[.M] where M does not contain
        /// more than maxDecimalPlaces digits, and every digit in N and M is significant.
        /// If the magnitude of this number is between 0 and 1, then N will be a leading zero.
        /// </summary>
        /// <param name="maxSigDigits"></param>
        /// <returns></returns>
        public string ToString(int maxDecimalPlaces)
        {
            Contract.Requires(maxDecimalPlaces >= 0);
            var mag = Sign < 0 ? new Rational(-Numerator, Denominator) : this;
            var whole = mag.Numerator / mag.Denominator;

            //// Need to produce the decimal places by repeated subtraction
            //// of powers of 10^-i
            var fracDigits = new LinkedList<int>();
            var frac = mag - new Rational(whole, BigInteger.One);
            var place = BigInteger.One;
            var needsRound = maxDecimalPlaces == 0 && frac.CompareTo(new Rational(BigInteger.One, new BigInteger(2))) >= 0;
            int digit;
            while (!frac.IsZero && fracDigits.Count < maxDecimalPlaces)
            {
                place *= 10;
                digit = (int)((frac.Numerator * place) / frac.Denominator);
                fracDigits.AddLast(digit);
                frac = frac - new Rational(new BigInteger(digit), place);
                if (fracDigits.Count == maxDecimalPlaces && !frac.IsZero)
                {
                    place *= 10;
                    digit = (int)((frac.Numerator * place) / frac.Denominator);
                    needsRound = digit >= 5;
                }
            }

            //// Perform rounding
            if (needsRound)
            {
                var crnt = fracDigits.Last;
                while (crnt != null)
                {
                    if (crnt.Value < 9)
                    {
                        crnt.Value = crnt.Value + 1;
                        needsRound = false;
                        break;
                    }
                    else
                    {
                        crnt.Value = -1; //// Flags that this digit is no longer significant
                        crnt = crnt.Previous;
                    }
                }

                if (needsRound)
                {
                    ++whole;
                }
            }

            var numString = Sign < 0 ? "-" : "";
            numString += whole.ToString();
            if (fracDigits.Count > 0 && fracDigits.First.Value >= 0)
            {
                numString += ".";
            }

            foreach (var f in fracDigits)
            {
                if (f < 0)
                {
                    break;
                }

                numString += f.ToString();
            }

            return numString;
        }
    }
}
