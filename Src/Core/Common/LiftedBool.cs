namespace Microsoft.Formula.Common
{
    using System;

    public struct LiftedBool
    {
        private int value;
        
        public static LiftedBool Unknown
        {
            get { return new LiftedBool(0); }
        }

        public static LiftedBool False
        {
            get { return new LiftedBool(-1); }
        }

        public static LiftedBool True
        {
            get { return new LiftedBool(1); }
        }

        public static bool operator ==(LiftedBool b1, LiftedBool b2)
        {
            return b1.value == b2.value;
        }

        public static bool operator !=(LiftedBool b1, LiftedBool b2)
        {
            return b1.value != b2.value;
        }

        public static bool operator <(LiftedBool b1, LiftedBool b2)
        {
            return b1.value < b2.value;
        }

        public static bool operator >(LiftedBool b1, LiftedBool b2)
        {
            return b1.value > b2.value;
        }

        public static bool operator <=(LiftedBool b1, LiftedBool b2)
        {
            return b1.value <= b2.value;
        }

        public static bool operator >=(LiftedBool b1, LiftedBool b2)
        {
            return b1.value >= b2.value;
        }

        public static LiftedBool operator !(LiftedBool b)
        {
            switch (b.value)
            {
                case -1:
                    return True;
                case 0:
                    return Unknown;
                case 1:
                    return False;
                default:
                    throw new NotImplementedException();
            }
        }

        public static LiftedBool operator &(LiftedBool b1, LiftedBool b2)
        {
            if (b1.value == -1 || b2.value == -1)
            {
                return False;
            }
            else if (b1.value == 0 || b2.value == 0)
            {
                return Unknown;
            }

            return True;
        }

        public static LiftedBool operator |(LiftedBool b1, LiftedBool b2)
        {
            if (b1.value == 1 || b2.value == 1)
            {
                return True;
            }
            else if (b1.value == 0 || b2.value == 0)
            {
                return Unknown;
            }

            return False;
        }

        public static explicit operator bool(LiftedBool b)
        {
            switch (b.value)
            {
                case -1:
                    return false;
                case 1:
                    return true;
                default:
                    throw new InvalidCastException("Cannot cast lifted bool to bool");
            }
        }

        // Define implicit bool -> lifted bool coercion: 
        public static implicit operator LiftedBool(bool b)
        {
            return new LiftedBool(b ? 1 : -1);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is LiftedBool))
            {
                return false;
            }

            return value == ((LiftedBool)obj).value;
        }

        public override int GetHashCode()
        {
            return value;
        }

        public override string ToString()
        {
            switch (value)
            {
                case -1:
                    return "false";
                case 0:
                    return "?";
                case 1:
                    return "true";
                default:
                    throw new NotImplementedException();
            }
        }

        public static int Compare(LiftedBool b1, LiftedBool b2)
        {
            return b1.value - b2.value;
        }

        public static int CompareBools(bool b1, bool b2)
        {
            return (b1 == b2 ? 0 : (b1 ? 1 : -1));
        }

        private LiftedBool(int value)
        {
            this.value = value;
        }
    }
}
