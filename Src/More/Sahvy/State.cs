using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sahvy
{
    public class State
    {
        private State() { }
        public State Clone()
        {
            return new State(this);
        }
        public State(State state)
        {
            this.step = state.step;
            this.continuousNames = (string[])state.continuousNames.Clone();
            this.discreteNames = (string[])state.discreteNames.Clone();
            this.continuousState = state.continuousState.Clone();
            this.discreteState = state.discreteState.Clone();
            this.flowpipe = new Flowpipe(this);
        }
        public State(int step, string[] continuousNames, DoubleBoundingBox continuousState, string[] discreteNames, FPIntegerBoundingBox discreteState)
        {
            this.step = step;
            this.continuousNames = continuousNames;
            this.discreteNames = discreteNames;
            this.continuousState = continuousState.Clone();
            this.discreteState = discreteState.Clone();
            this.flowpipe = new Flowpipe(this);
        }
        public double LeftBound(string axis)
        {
            int idx = Array.FindIndex(this.continuousNames, x => x.Equals(axis));
            if (idx != -1)
                return this.continuousState.axes[idx].left;
            throw new Exception("Axis not found");
        }
        public double RightBound(string axis)
        {
            int idx = Array.FindIndex(this.continuousNames, x => x.Equals(axis));
            if (idx != -1)
                return this.continuousState.axes[idx].right;
            throw new Exception("Axis not found");
        }
        public void Print(System.IO.TextWriter stream, string indent = "\t")
        {
            stream.WriteLine("{0}Step: {1}", indent, step);
            for (int i = 0; i < continuousNames.Length; ++i)
                stream.WriteLine("{0}{1:5} = {2}", indent, continuousNames[i], continuousState.axes[i].ToString());
            for (int i = 0; i < discreteNames.Length; ++i)
                stream.WriteLine("{0}{1:5} = {2}", indent, discreteNames[i], discreteState.axes[i]);
            stream.WriteLine("{0}CT diameter: {1}", indent, continuousState.Diameter());
            stream.WriteLine("{0}CT width: {1}", indent, continuousState.MaxWidth());
            stream.WriteLine("{0}DT diameter: {1}", indent, discreteState.Diameter());
            stream.WriteLine("{0}DT width: {1}", indent, discreteState.Width());
        }
        public bool Contains(State s)
        {
            return this.continuousState.Contains(s.continuousState) && 
                   this.discreteState.Contains(s.discreteState);
        }
        public List<State> Split(int divs = 2)
        {
            // find axis with maximum width
            double maxL = 0.0;
            int maxI = 0;
            for (int i = 0; i < continuousState.axes.Length; ++i)
            {
                if (continuousState.axes[i].width > maxL)
                {
                    maxL = continuousState.axes[i].width;
                    maxI = i;
                }
            }
            List<State> list = new List<State>();
            list.Add(this);
            return Split(list, maxI, divs);
        }
        public List<State> SplitDim(int dim, int divs = 2)
        {            
            List<State> list = new List<State>();
            list.Add(this);
            return Split(list, dim, divs);
        }
        public List<State> SplitDimDiscrete(int dim, int divs = 2)
        {
            List<State> list = new List<State>();
            list.Add(this);
            return SplitDiscrete(list, dim, divs);
        }
        static public List<State> Split(List<State> states, int dim, int divs)
        {
            State[] result = new State[divs];
            
            foreach (State s in states)
            {
                List<DoubleInterval[]> sec = new List<DoubleInterval[]>(divs);
                double dimL = s.continuousState.axes[dim].left;
                double dimR = s.continuousState.axes[dim].right;
                var next = new DoubleInterval[s.continuousState.axes.Length];
                for (int i = 0; i < s.continuousState.axes.Length; ++i)
                    next[i] = s.continuousState.axes[i];
                double prevR = dimL;
                for (int j = 1; j < divs; ++j)
                {
                    double R = (dimR - dimL) * j / divs + dimL;
                    next[dim] = new DoubleInterval(prevR, R);
                    // intermix the results for faster coverage
                    int index = (j % 2 == 1) ? (j/2+1) : (divs - j/2);
                    result[index] = new State(s.step, s.continuousNames, new DoubleBoundingBox(next), s.discreteNames, s.discreteState);
                    prevR = R;
                }
                next[dim] = new DoubleInterval(prevR, dimR);
                result[0] = new State(s.step, s.continuousNames, new DoubleBoundingBox(next), s.discreteNames, s.discreteState);
            }
            List<State> list = new List<State>(divs);
            foreach (var s in result)
                list.Add(s);
            return list;
        }
        static public List<State> SplitDiscrete(List<State> states, int dim, int divs)
        {
            State[] result = new State[divs];
            
            foreach (State s in states)
            {
                List<DoubleInterval[]> sec = new List<DoubleInterval[]>(divs);
                var bits = s.discreteState.axes[dim].bits;
                var decimals = s.discreteState.axes[dim].decimals;
                int dimL = s.discreteState.axes[dim].left;
                int dimR = s.discreteState.axes[dim].right;
                var next = new FPIntegerInterval[s.discreteState.axes.Length];
                for (int i = 0; i < s.discreteState.axes.Length; ++i)
                    next[i] = s.discreteState.axes[i];
                int prevR = dimL;
                for (int j = 1; j < divs; ++j)
                {
                    int R = (dimR - dimL) * j / divs + dimL;
                    next[dim] = new FPIntegerInterval(prevR, R, bits, decimals);
                    // intermix the results for faster coverage
                    int index = (j % 2 == 1) ? (j / 2 + 1) : (divs - j / 2);
                    result[index] = new State(s.step, s.continuousNames, s.continuousState, s.discreteNames, new FPIntegerBoundingBox(next));
                    prevR = R;
                }
                next[dim] = new FPIntegerInterval(prevR, dimR, bits, decimals);
                result[0] = new State(s.step, s.continuousNames, s.continuousState, s.discreteNames, new FPIntegerBoundingBox(next));
            }
            List<State> list = new List<State>(divs);
            foreach (var s in result)
                list.Add(s);
            return list;
        }
        // split in each dimension
        public List<State> SplitEach(int divs = 2)
        {
            List<State> list = new List<State>();
            list.Add(this);
            for (int i = 0; i < continuousState.axes.Length; ++i)
                list = Split(list, i, divs);
            return list;
        }
        public List<State> SplitEachDiscrete(int divs = 2)
        {
            List<State> list = new List<State>();
            list.Add(this);
            for (int i = 0; i < discreteState.axes.Length; ++i)
                list = SplitDiscrete(list, i, divs);
            return list;
        }
        public List<State> SplitDiscreteAll(int dim)
        {
            List<State> result = new List<State>();
            
            var next = new FPIntegerInterval[discreteState.axes.Length];
            for (int i = 0; i < discreteState.axes.Length; ++i)
                next[i] = discreteState.axes[i];
            var bits = discreteState.axes[dim].bits;
            var decimals = discreteState.axes[dim].decimals;
            int L = discreteState.axes[dim].left;
            int R = discreteState.axes[dim].right;
            for (int value = L; value <= R; ++value)
            {
                next[dim] = new FPIntegerInterval(value, value, bits, decimals);
                result.Add(new State(step, continuousNames, continuousState, discreteNames, new FPIntegerBoundingBox(next)));
            }
            return result;
        }
        public Dictionary<string, DoubleInterval> ToDictionary()
        {
            Dictionary<string, DoubleInterval> result = new Dictionary<string, DoubleInterval>();
            for (int i = 0; i < continuousNames.Length; ++i)
                result.Add(continuousNames[i], continuousState.axes[i]);
            return result;
        }
        public Dictionary<string, DoubleInterval> ToHybridDictionary()
        {
            Dictionary<string, DoubleInterval> result = ToDictionary();
            for (int i = 0; i < discreteNames.Length; ++i)
                result.Add(discreteNames[i], discreteState.axes[i].ToDoubleInterval());
            return result;
        }

        public static int BITS = 16;
        public static int DECIMALS = 6;
        public int step;
        public Flowpipe flowpipe;
        public ContinuousSystem cSystem;
        public String[] continuousNames;
        public String[] discreteNames;
        public DoubleBoundingBox continuousState;
        public FPIntegerBoundingBox discreteState;
    }
}
