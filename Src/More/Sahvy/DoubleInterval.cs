using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Sahvy
{
    public class DoubleInterval
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateInterval(double left, double right);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateIntervalCopy(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeleteInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern double SupInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern double InfInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern double MidpointInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern double WidthInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool SubseteqInterval(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr AddInterval(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SubInterval(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MulInterval(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DivInterval(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void AddAssignInterval(IntPtr A, double c);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SubAssignInterval(IntPtr A, double c);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void MulAssignInterval(IntPtr A, double c);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DivAssignInterval(IntPtr A, double c);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SqrtInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr InvInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr RecInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SinInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CosInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ExpInterval(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void ToStringInterval(IntPtr A, int length, StringBuilder buffer);

        public IntPtr ptr { get; private set; }

        private DoubleInterval() { }
        public DoubleInterval(IntPtr ptr)
        {
            this.ptr = ptr;
        }
        ~DoubleInterval()
        {
            DeleteInterval(ptr);
        }
        public DoubleInterval Clone()
        {
            return new DoubleInterval(CreateIntervalCopy(ptr));
        }
        public DoubleInterval(double value)
        {
            this.ptr = CreateInterval(value, value);
        }
        public DoubleInterval(double left, double right)
        {
            this.ptr = CreateInterval(left, right);
        }
        public double left { get { return InfInterval(ptr); } private set { } }
        public double right { get { return SupInterval(ptr); } private set { } }
        public double midpoint { get { return MidpointInterval(ptr); } private set { } }
        public double width { get { return WidthInterval(ptr); } private set { } }

        public static DoubleInterval operator +(DoubleInterval A, DoubleInterval B)
        {
            return new DoubleInterval(AddInterval(A.ptr, B.ptr));
        }
        public static DoubleInterval operator -(DoubleInterval A, DoubleInterval B)
        {
            return new DoubleInterval(SubInterval(A.ptr, B.ptr));
        }
        public static DoubleInterval operator *(DoubleInterval A, DoubleInterval B)
        {
            return new DoubleInterval(MulInterval(A.ptr, B.ptr));
        }
        public static DoubleInterval operator /(DoubleInterval A, DoubleInterval B)
        {
            return new DoubleInterval(DivInterval(A.ptr, B.ptr));
        }
        public static DoubleInterval operator +(DoubleInterval A, double c)
        {
            var r = A.Clone();
            AddAssignInterval(r.ptr, c);
            return r;
        }
        public static DoubleInterval operator -(DoubleInterval A, double c)
        {
            var r = A.Clone();
            SubAssignInterval(r.ptr, c);
            return r;
        }
        public static DoubleInterval operator *(DoubleInterval A, double c)
        {
            var r = A.Clone();
            MulAssignInterval(r.ptr, c);
            return r;
        }
        public static DoubleInterval operator /(DoubleInterval A, double c)
        {
            var r = A.Clone();
            DivAssignInterval(r.ptr, c);
            return r;
        }
        public static DoubleInterval operator -(DoubleInterval A)
        {
            return new DoubleInterval(InvInterval(A.ptr));
        }
        public DoubleInterval Sqrt()
        {
            return new DoubleInterval(SqrtInterval(ptr));
        }
        public DoubleInterval Rec()
        {
            return new DoubleInterval(RecInterval(ptr));
        }
        public DoubleInterval Sin()
        {
            return new DoubleInterval(SinInterval(ptr));
        }
        public DoubleInterval Cos()
        {
            return new DoubleInterval(CosInterval(ptr));
        }
        public DoubleInterval Exp()
        {
            return new DoubleInterval(ExpInterval(ptr));
        }

        /// <summary>
        /// Returns true if this interval is subset of A
        /// </summary>
        /// <param name="A"></param>
        /// <returns></returns>
        public bool Subseteq(DoubleInterval A)
        {
            return SubseteqInterval(this.ptr, A.ptr);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(60);
            ToStringInterval(ptr, 60, sb);
            return sb.ToString();
        }
    }
}
