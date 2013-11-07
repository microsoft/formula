namespace Microsoft.Formula.Common
{
    using System;

    internal enum LiftedRationalKind { Value = 0, MinusInfty = 1, PlusInfty = 2 };

    /// <summary>
    /// Represents a rational that can also be Minus or Plus Infinity.
    /// </summary>
    internal struct LiftedRational
    {
        private LiftedRationalKind kind;
        private Rational value;

        public static LiftedRational MinusInfty
        {
            get { return new LiftedRational(true); }
        }

        public static LiftedRational PlusInfty
        {
            get { return new LiftedRational(false); }
        }

        public static LiftedRational Zero
        {
            get { return new LiftedRational(Rational.Zero); }
        }

        public static LiftedRational One
        {
            get { return new LiftedRational(Rational.One); }
        }

        public static LiftedRational MinusOne
        {
            get { return new LiftedRational(new Rational(-1)); }
        }

        public Rational Value
        {
            get { return value; }
        }

        public LiftedRationalKind Kind
        {
            get { return kind; }
        }

        public LiftedRational(Rational r)
        {
            value = r;
            kind = LiftedRationalKind.Value;
        }

        private LiftedRational(bool isMinusInfty)
        {
            value = Rational.Zero;
            kind = isMinusInfty ? LiftedRationalKind.MinusInfty : LiftedRationalKind.PlusInfty;
        }


        public static bool operator <(LiftedRational r1, LiftedRational r2)
        {
            if ((r1.kind == LiftedRationalKind.MinusInfty && r2.kind == LiftedRationalKind.PlusInfty) ||
                (r1.kind == LiftedRationalKind.MinusInfty && r2.kind == LiftedRationalKind.Value) ||
                (r1.kind == LiftedRationalKind.Value && r2.kind == LiftedRationalKind.PlusInfty))
            {
                return true;
            }
            else if (r1.kind != LiftedRationalKind.Value || r2.kind != LiftedRationalKind.Value)
            {
                return false;
            }
            else
            {
                return r1.value < r2.value;
            }
        }

        public static bool operator >(LiftedRational r1, LiftedRational r2)
        {
            if ((r1.kind == LiftedRationalKind.PlusInfty && r2.kind == LiftedRationalKind.MinusInfty) ||
                (r1.kind == LiftedRationalKind.PlusInfty && r2.kind == LiftedRationalKind.Value) ||
                (r1.kind == LiftedRationalKind.Value && r2.kind == LiftedRationalKind.MinusInfty))
            {
                return true;
            }
            else if (r1.kind != LiftedRationalKind.Value || r2.kind != LiftedRationalKind.Value)
            {
                return false;
            }
            else
            {
                return r1.value > r2.value;
            }
        }

        public static bool operator <=(LiftedRational r1, LiftedRational r2)
        {
            if ((r1.kind == LiftedRationalKind.Value && r2.kind == LiftedRationalKind.MinusInfty) ||
                (r1.kind == LiftedRationalKind.PlusInfty && r2.kind == LiftedRationalKind.Value) ||
                (r1.kind == LiftedRationalKind.PlusInfty && r2.kind == LiftedRationalKind.MinusInfty))
            {
                return false;
            }
            else if (r1.kind != LiftedRationalKind.Value || r2.kind != LiftedRationalKind.Value)
            {
                return true;
            }
            else
            {
                return r1.value <= r2.value;
            }
        }

        public static bool operator >=(LiftedRational r1, LiftedRational r2)
        {
            if ((r1.kind == LiftedRationalKind.Value && r2.kind == LiftedRationalKind.PlusInfty) ||
                (r1.kind == LiftedRationalKind.MinusInfty && r2.kind == LiftedRationalKind.Value) ||
                (r1.kind == LiftedRationalKind.MinusInfty && r2.kind == LiftedRationalKind.PlusInfty))
            {
                return false;
            }
            else if (r1.kind != LiftedRationalKind.Value || r2.kind != LiftedRationalKind.Value)
            {
                return true;
            }
            else
            {
                return r1.value >= r2.value;
            }
        }

        public static bool operator ==(LiftedRational r1, LiftedRational r2)
        {
            if (r1.kind != r2.kind)
            {
                return false;
            }
            else if (r1.kind == LiftedRationalKind.Value)
            {
                return r1.value == r2.value;
            }
            else
            {
                return true;
            }
        }

        public static bool operator !=(LiftedRational r1, LiftedRational r2)
        {
            if (r1.kind != r2.kind)
            {
                return true;
            }
            else if (r1.kind == LiftedRationalKind.Value)
            {
                return r1.value != r2.value;
            }
            else
            {
                return false;
            }
        }

        public static LiftedRational Max(LiftedRational r1, LiftedRational r2)
        {
            return r1 > r2 ? r1 : r2;
        }

        public static LiftedRational Min(LiftedRational r1, LiftedRational r2)
        {
            return r1 < r2 ? r1 : r2;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is LiftedRational))
            {
                return false;
            }

            return ((LiftedRational)obj) == this;
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public override string ToString()
        {
            switch (kind)
            {
                case LiftedRationalKind.MinusInfty:
                    return "-Infty";
                case LiftedRationalKind.PlusInfty:
                    return "+Infty";
                case LiftedRationalKind.Value:
                    return value.ToString();
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
