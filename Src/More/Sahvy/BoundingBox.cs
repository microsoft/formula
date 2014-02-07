using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sahvy
{
    public class DoubleBoundingBox
    {
        private DoubleBoundingBox() { }
        public DoubleBoundingBox Clone()
        {
            DoubleBoundingBox res = new DoubleBoundingBox();
            res.axes = new DoubleInterval[this.axes.Length];
            for (int i = 0; i < res.axes.Length; ++i)
                res.axes[i] = this.axes[i].Clone();
            return res;
        }
        public DoubleBoundingBox(DoubleInterval[] axes)
        {
            this.axes = new DoubleInterval[axes.Length];
            for (int i = 0; i < this.axes.Length; ++i)
                this.axes[i] = axes[i].Clone();
        }
        
        /// <summary>
        /// Average width of the bounding box
        /// </summary>
        public double Diameter()
        {
            double diam = 0.0;
            foreach (DoubleInterval i in axes)
                diam += i.width;
            return diam / axes.Length;
        }
        /// <summary>
        /// Maximum width of the bounding box
        /// </summary>
        public double MaxWidth()
        {
            double w = 0.0;
            foreach (DoubleInterval i in axes)
                w = Math.Max(w, i.width);
            return w;
        }
        public double Volume()
        {
            double V = 1.0;
            foreach (DoubleInterval i in axes)
            {
                double L = i.width;
                if (L != 0.0)
                    V *= L;
            }
            return V;
        }
        public bool Contains(DoubleBoundingBox A)
        {
            for (int i = 0; i < axes.Length; ++i)
                if (!A.axes[i].Subseteq(this.axes[i]))
                    return false;
            return true;
        }

        public DoubleInterval[] axes;
    }


    public class FPIntegerBoundingBox
    {
        private FPIntegerBoundingBox() { }
        public FPIntegerBoundingBox Clone()
        {
            FPIntegerBoundingBox res = new FPIntegerBoundingBox();
            res.axes = new FPIntegerInterval[this.axes.Length];
            for (int i = 0; i < res.axes.Length; ++i)
                res.axes[i] = this.axes[i].Clone();
            return res;
        }
        public FPIntegerBoundingBox(FPIntegerInterval[] axes)
        {
            this.axes = new FPIntegerInterval[axes.Length];
            for (int i = 0; i < this.axes.Length; ++i)
                this.axes[i] = axes[i].Clone();
        }
        public int Diameter()
        {
            if (axes.Length == 0) return 0;
            int diam = 0;
            foreach (FPIntegerInterval i in axes)
                diam += i.width;
            return diam / axes.Length;
        }
        public int Width()
        {
            if (axes.Length == 0) return 0;
            int diam = 0;
            foreach (FPIntegerInterval i in axes)
                diam = Math.Max(diam, i.width);
            return diam;
        }
        public bool Contains(FPIntegerBoundingBox A)
        {
            for (int i = 0; i < axes.Length; ++i)
                if (!A.axes[i].Subseteq(this.axes[i]))
                    return false;
            return true;
        }

        public FPIntegerInterval[] axes;
    }
}
