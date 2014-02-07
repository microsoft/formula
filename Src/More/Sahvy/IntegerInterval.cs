using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics.Contracts;

namespace Sahvy
{
    public class FPIntegerInterval
    {
        public FPIntegerInterval Clone()
        {
            return new FPIntegerInterval(left, right, bits, decimals);
        }
        public FPIntegerInterval(int value, uint bits, uint decimals)
        {
            this.left = this.right = value;
            this.bits = bits;
            this.decimals = decimals;
        }
        public FPIntegerInterval(int left, int right, uint bits, uint decimals)
        {
            this.left = left;
            this.right = right;
            this.bits = bits;
            this.decimals = decimals;
        }
        public int left { get; private set; }
        public int right { get; private set; }
        public int width { get { return right - left; } private set { } }
        public uint bits { get; private set; }
        public uint decimals { get; private set; }

        public static FPIntegerInterval operator +(FPIntegerInterval A, FPIntegerInterval B)
        {
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            return new FPIntegerInterval(A.left + B.left, A.right + B.right, A.bits, A.decimals);
        }
        public static FPIntegerInterval operator -(FPIntegerInterval A, FPIntegerInterval B)
        {
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            return new FPIntegerInterval(A.left - B.right, A.right - B.left, A.bits, A.decimals);
        }
        public static FPIntegerInterval operator -(FPIntegerInterval A)
        {
            return new FPIntegerInterval(-A.right, -A.left, A.bits, A.decimals);
        }
        public bool Subseteq(FPIntegerInterval A)
        {
            Contract.Requires(A.bits == this.bits);
            Contract.Requires(A.decimals == this.decimals);
            return left >= A.left && right <= A.right;
        }
        public DoubleInterval ToDoubleInterval()
        {
            return new DoubleInterval((double)this.left / (1 << (int)decimals), (double)this.right / (1 << (int)decimals));
        }

        public override string ToString()
        {
            return String.Format("[{0},{1}] S{2}/{3}", left, right, bits, decimals);
        }
    }
}
