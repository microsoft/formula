namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public struct BuilderRef
    {
        internal ulong refId;

        /// <summary>
        /// A heap location that never points to a valid object
        /// </summary>
        public static BuilderRef Null
        {
            get { return new BuilderRef(0); }
        }

        internal ulong RefId
        {
            get { return refId; }
        }

        internal BuilderRef(ulong refId)
        {
            this.refId = refId;
        }

        public static bool operator ==(BuilderRef r1, BuilderRef r2)
        {
            return r1.refId == r2.refId;
        }

        public static bool operator !=(BuilderRef r1, BuilderRef r2)
        {
            return r1.refId != r2.refId;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is BuilderRef))
            {
                return false;
            }

            return ((BuilderRef)obj).refId == refId;
        }

        public override int GetHashCode()
        {
            return refId.GetHashCode();
        }

        internal static int Compare(BuilderRef r1, BuilderRef r2)
        {
            if (r1.refId < r2.refId)
            {
                return -1;
            }
            else if (r1.refId > r2.refId)
            {
                return 1;
            }

            return 0;
        }
    }
}
