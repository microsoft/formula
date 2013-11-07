namespace Microsoft.Formula.Common
{
    using System;

    public struct LiftedInt
    {
        private int? value;

        public static LiftedInt Unknown
        {
            get { return new LiftedInt(); }
        }

        public static LiftedInt MinusOne
        {
            get { return new LiftedInt(-1); }
        }

        public static LiftedInt One
        {
            get { return new LiftedInt(1); }
        }

        public static LiftedInt Zero
        {
            get { return new LiftedInt(0); }
        }

        public static bool operator ==(LiftedInt v1, LiftedInt v2)
        {
            return v1.value == v2.value;
        }

        public static bool operator !=(LiftedInt v1, LiftedInt v2)
        {
            return v1.value != v2.value;
        }

        public static LiftedBool operator <(LiftedInt v1, LiftedInt v2)
        {
            return (v1.value == null || v2.value == null) ? LiftedBool.Unknown : (int)v1.value < (int)v2.value;
        }

        public static LiftedBool operator <=(LiftedInt v1, LiftedInt v2)
        {
            return (v1.value == null || v2.value == null) ? LiftedBool.Unknown : (int)v1.value <= (int)v2.value;
        }

        public static LiftedBool operator >(LiftedInt v1, LiftedInt v2)
        {
            return (v1.value == null || v2.value == null) ? LiftedBool.Unknown : (int)v1.value > (int)v2.value;
        }

        public static LiftedBool operator >=(LiftedInt v1, LiftedInt v2)
        {
            return (v1.value == null || v2.value == null) ? LiftedBool.Unknown : (int)v1.value >= (int)v2.value;
        }

        public static explicit operator int(LiftedInt v)
        {
            if (v.value == null)
            {
                throw new InvalidCastException("Cannot cast lifted int to int");
            }

            return (int)v.value;
        }

        // Define implicit bool -> lifted bool coercion: 
        public static implicit operator LiftedInt(int v)
        {
            return new LiftedInt(v);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is LiftedInt))
            {
                return false;
            }

            return value == ((LiftedInt)obj).value;
        }

        public override int GetHashCode()
        {
            return value == null ? 0 : (int)value;
        }

        public override string ToString()
        {
            if (value == null)
            {
                return "?";
            }
            else
            {
                return ((int)value).ToString();
            }
        }

        public LiftedInt(int value)
        {
            this.value = value;
        }
    }
}
