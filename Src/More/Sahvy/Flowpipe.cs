using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Sahvy
{
    public class Flowstar
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeclareStateVar(string varName);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void SetCutoffThreshold(double threshold);

        public static void DeclareStateVariable(string varName)
        {
            DeclareStateVar(varName);
        }

        public static void SetCutoff(double threshold)
        {
            SetCutoffThreshold(threshold);
        }

        public const int Identity_Precondition = 0;
        public const int QR_Precondition = 1;
    }

    public class FlowpipeException : Exception
    {
        public FlowpipeException(String msg) : base(msg)
        {            
        }
    }
        
    public class Monomial
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateMonomial(IntPtr I, int numVars, int[] degs);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateConstantMonomial(IntPtr I, int numVars);
        
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DeleteMonomial(IntPtr monomial);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr AddMonomial(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr MulMonomial(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DumpMonomial(IntPtr A, int numVars, string[] varNames, bool dumpInterval);

        public IntPtr ptr { get; private set; }
        private Monomial(IntPtr ptr)
        {
            this.ptr = ptr;
        }
        public Monomial(DoubleInterval I, int numVars)
        {
            ptr = CreateConstantMonomial(I.ptr, numVars);
        }
        public Monomial(DoubleInterval I, int[] degs)
        {
            ptr = CreateMonomial(I.ptr, degs.Length, degs);
        }
        ~Monomial()
        {
            DeleteMonomial(ptr);
        }
        public static Monomial operator +(Monomial A, Monomial B)
        {
            return new Monomial(AddMonomial(A.ptr, B.ptr));
        }
        public static Monomial operator *(Monomial A, Monomial B)
        {
            return new Monomial(MulMonomial(A.ptr, B.ptr));
        }
        public void dump(string[] varNames, bool dumpInterval)
        {
            DumpMonomial(ptr, varNames.Length, varNames, dumpInterval);
            Console.WriteLine();
        }
    }

    public class Polynomial
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateEmptyPolynomial();

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreatePolynomial(IntPtr monomial);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateConstantPolynomial(IntPtr constant, int numVars);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DeletePolynomial(IntPtr polynomial);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void AddAssignPolynomial(IntPtr polynomial, IntPtr monomial);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr AddPolynomial(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr SubPolynomial(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr MulPolynomial(IntPtr A, IntPtr B);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr NegPolynomial(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void CutoffPolynomial(IntPtr A);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DumpPolynomial(IntPtr A, int numVars, string[] varNames, bool dumpInterval);

        public IntPtr ptr { get; private set; }
        private Polynomial(IntPtr ptr)
        {
            this.ptr = ptr;
        }
        public Polynomial()
        {
            ptr = CreateEmptyPolynomial();
        }
        public Polynomial(DoubleInterval I, int numVars)
        {
            ptr = CreateConstantPolynomial(I.ptr, numVars);
        }
        public Polynomial(Monomial monomial)
        {
            ptr = CreatePolynomial(monomial.ptr);
        }
        ~Polynomial()
        {
            DeletePolynomial(ptr);
        }
        public void AddAssign(Monomial monomial)
        {
            AddAssignPolynomial(ptr, monomial.ptr);
        }
        public static Polynomial operator +(Polynomial A, Polynomial B)
        {
            return new Polynomial(AddPolynomial(A.ptr, B.ptr));
        }
        public static Polynomial operator -(Polynomial A, Polynomial B)
        {
            return new Polynomial(SubPolynomial(A.ptr, B.ptr));
        }
        public static Polynomial operator *(Polynomial A, Polynomial B)
        {
            return new Polynomial(MulPolynomial(A.ptr, B.ptr));
        }
        public static Polynomial operator -(Polynomial A)
        {
            return new Polynomial(NegPolynomial(A.ptr));
        }
        public void Cutoff ()
        {
            CutoffPolynomial(ptr);
        }
        public void Dump(string[] varNames, bool dumpInterval)
        {
            DumpPolynomial(ptr, varNames.Length, varNames, dumpInterval);
            Console.WriteLine();
        }
    }

    public class TaylorModel
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateTaylorModel(IntPtr polynomial, IntPtr interval);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DeleteTaylorModel(IntPtr result);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DumpTaylorModel(IntPtr A, int numVars, string[] varNames, bool dumpInterval);

        public IntPtr ptr { get; private set; }
        public TaylorModel(Polynomial polynomial, DoubleInterval I)
        {
            ptr = CreateTaylorModel(polynomial.ptr, I.ptr);
        }
        ~TaylorModel()
        {
            DeleteTaylorModel(ptr);
        }
        public void Dump(string[] varNames, bool dumpInterval)
        {
            DumpTaylorModel(ptr, varNames.Length, varNames, dumpInterval);
            Console.WriteLine();
        }
    }
    public class TaylorModelVec
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr CreateTaylorModelVec(IntPtr[] taylorModelList, int numTaylorModels);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DeleteTaylorModelVec(IntPtr result);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern void DumpTaylorModelVec(IntPtr A, int numStateVars, string[] stateVarNames, int numTmVars, string[] tmVarNames, bool dumpInterval);

        public IntPtr ptr { get; private set; }
        public TaylorModelVec(TaylorModel[] tmv)
        {
            IntPtr[] list = new IntPtr[tmv.Length];
            for (int i = 0; i < tmv.Length; ++i)
                list[i] = tmv[i].ptr;
            ptr = CreateTaylorModelVec(list, list.Length);
        }
        ~TaylorModelVec()
        {
            DeleteTaylorModelVec(ptr);
        }
        public void Dump(string[] stateVarNames, string[] tmVarNames, bool dumpInterval)
        {
            DumpTaylorModelVec(ptr, stateVarNames.Length, stateVarNames, tmVarNames.Length, tmVarNames, dumpInterval);
            Console.WriteLine();
        }
    }

    public class Flowpipe
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFlowpipe(int numVars, IntPtr[] domain, IntPtr time);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeleteFlowpipe(IntPtr flowpipe);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void EvalFlowpipe(IntPtr flowpipe, IntPtr[] result);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceLowDegreeFlowpipe(ref IntPtr result, IntPtr flowpipe, IntPtr tmvOde, IntPtr hfOde, double step, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveStepLowDegreeFlowpipe(ref IntPtr result, IntPtr flowpipe, IntPtr tmvOde, IntPtr hfOde, double step, double miniStep, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveOrderLowDegreeFlowpipe(ref IntPtr result, IntPtr flowpipe, IntPtr tmvOde, IntPtr hfOde, double step, int order, int maxOrder, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceHighDegreeFlowpipe(ref IntPtr result, IntPtr flowpipe, IntPtr hfOde, double step, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveStepHighDegreeFlowpipe(ref IntPtr result, IntPtr flowpipe, IntPtr hfOde, double step, double miniStep, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveOrderHighDegreeFlowpipe (ref IntPtr result, IntPtr flowpipe, IntPtr hfOde, double step, int order, int maxOrder, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceNonPolynomialFlowpipe(ref IntPtr result, IntPtr flowpipe, int numOde, string[] strOde, double step, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveStepNonPolynomialFlowpipe(ref IntPtr result, IntPtr flowpipe, int numOde, string[] strOde, double step, double miniStep, int order, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool AdvanceAdaptiveOrderNonPolynomialFlowpipe(ref IntPtr result, IntPtr flowpipe, int numOde, string[] strOde, double step, int order, int maxOrder, int precondition, int numVars, double[] estimation);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DumpFlowpipe(IntPtr A, int numStateVars, string[] stateVarNames, int numTmVars, string[] tmVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateODE(IntPtr tmvOde);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeleteODE(IntPtr hfOde);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DumpODE(IntPtr A, int numVars, string[] varNames);               

        public static Flowpipe FromPtr(IntPtr flowpipe, int numVars)
        {
            Flowpipe result = new Flowpipe();
            result.ptr = flowpipe;
            result.numVars = numVars;
            return result;
        }

        public IntPtr ptr { get; private set; }
        public int numVars { get; private set; }
        private Flowpipe() { }
        public Flowpipe(State state)
        {
            this.numVars = state.continuousNames.Length;

            DoubleInterval[] domain = new DoubleInterval[numVars];
            IntPtr[] domain_ptr = new IntPtr[numVars];
            for (int i = 0; i < numVars; ++i)
            {                
                domain[i] = state.continuousState.axes[i].Clone();
                domain_ptr[i] = domain[i].ptr;
            }

            ptr = CreateFlowpipe(numVars, domain_ptr, new DoubleInterval(0.0).ptr);
        }

        ~Flowpipe()
        {
            DeleteFlowpipe(ptr);
        }

        public Flowpipe AdvancePolynomial(TaylorModelVec ODE, double time, int order, double step, double miniStep, double[] estimation)
        {
            IntPtr hfOde = CreateODE(ODE.ptr);
            IntPtr current = this.ptr;
            IntPtr result = IntPtr.Zero;
            double curStep = step;
            for (double t = 0.0; t < time; t += step)
            {
                if (t + step > time)
                    curStep = time - t;
                while (!AdvanceLowDegreeFlowpipe(ref result, current, ODE.ptr, hfOde, curStep, order, Flowstar.QR_Precondition, numVars, estimation))
                {
                    for (int i = 0; i < estimation.Length; ++i)
                        estimation[i] *= 2;
                    if (estimation[0] > 100000)
                        throw new FlowpipeException("Cannot find good estimation for the flowpipe");
                }

                current = result;
            }
            Flowpipe res = Flowpipe.FromPtr(result, numVars);
            return res;
        }

        public Flowpipe AdvanceNonPolynomial(string[] ODE, double time, int order, double step, double miniStep, double[] estimation)
        {
            IntPtr result = IntPtr.Zero;
            bool bValue = AdvanceAdaptiveStepNonPolynomialFlowpipe(ref result, this.ptr, ODE.Length, ODE, step, miniStep, order, Flowstar.QR_Precondition, numVars, estimation);
            if (!bValue)
                throw new FlowpipeException("Cannot find good estimation for the flowpipe");
            Flowpipe res = Flowpipe.FromPtr(result, numVars);
            return res;
        }

        public DoubleBoundingBox Eval()
        {
            IntPtr[] ival = new IntPtr[numVars];
            EvalFlowpipe(this.ptr, ival);
            DoubleInterval[] result = new DoubleInterval[numVars];
            for (int i = 0; i < numVars; ++i)
                result[i] = new DoubleInterval(ival[i]);
            return new DoubleBoundingBox(result);
        }

        public void Dump(string[] stateVarNames, string[] tmVarNames, bool dumpInterval)
        {
            DumpFlowpipe(ptr, stateVarNames.Length, stateVarNames, tmVarNames.Length, tmVarNames);
            Console.WriteLine();
        }
    }

    public class ContinuousSystem
    {
        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateContinuousSystem(int numVars);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DeleteContinuousSystem(IntPtr system);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DumpContinuousSystem(IntPtr system, int numVars, string[] varNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetODEContinuousSystem(IntPtr system, IntPtr tmvOde);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetStrODEContinuousSystem(IntPtr system, int numOde, string[] strOde);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SetInitialSetContinuousSystem(IntPtr system, IntPtr initialFlowpipe);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachLowDegreeContinuousSystem(IntPtr system, double step, double time, int order, int precondition, int numVars, double[] estimation, bool bPrint, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachLowDegreeAdaptiveStepContinuousSystem(IntPtr system, double step, ref double miniStep, double time, int order, int precondition, int numVars, double[] estimation, bool bPrint, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachLowDegreeAdaptiveOrderContinuousSystem(IntPtr system, double step, double time, int order, int maxOrder, int numVars, double[] estimation, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachHighDegreeContinuousSystem(IntPtr system, double step, double time, int order, int numVars, double[] estimation, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachHighDegreeAdaptiveStepContinuousSystem(IntPtr system, double step, ref double miniStep, double time, int order, int numVars, double[] estimation, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachHighDegreeAdaptiveOrderContinuousSystem(IntPtr system, double step, double time, int order, int maxOrder, int numVars, double[] estimation, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachNonPolynomialContinuousSystem(IntPtr system, double step, double time, int order, int precondition, int numVars, double[] estimation, bool bPrint, string[] stateVarNames);

        [DllImport("flowstar.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ReachNonPolynomialAdaptiveStepContinuousSystem(IntPtr system, double step, ref double miniStep, double time, int order, int precondition, int numVars, double[] estimation, bool bPrint, string[] stateVarNames);
        
        IntPtr ptr;
        Flowpipe initialSet;
        string[] variables;
        
        public ContinuousSystem(State state)
        {
            this.variables = (string[])state.continuousNames.Clone();
            initialSet = state.flowpipe; 
            ptr = CreateContinuousSystem(state.continuousNames.Length);
            SetInitialSetContinuousSystem(ptr, initialSet.ptr);
        }

        ~ContinuousSystem()
        {
            DeleteContinuousSystem(ptr);
        }

        public void Dump()
        {
            DumpContinuousSystem(ptr, variables.Length, variables);
        }

        public void SetODE(TaylorModelVec ODE)
        {
            SetODEContinuousSystem(ptr, ODE.ptr);
        }
        public void SetODE(string[] ODE)
        {
            SetStrODEContinuousSystem(ptr, ODE.Length, ODE);
        }

        public Flowpipe ReachLowDegree(double step, double time, int order, double[] estimation, bool bPrint = false)
        {
            IntPtr result = ReachLowDegreeContinuousSystem(ptr, step, time, order, Flowstar.Identity_Precondition, estimation.Length, estimation, bPrint, variables);
            if (result == IntPtr.Zero)
                throw new FlowpipeException("Cannot find good range estimator");
                        
            return Flowpipe.FromPtr(result, initialSet.numVars);
        }

        public Flowpipe ReachLowDegree(double step, ref double miniStep, double time, int order, double[] estimation, bool bPrint = false)
        {
            int numVars = variables.Length;            
            IntPtr result = ReachLowDegreeAdaptiveStepContinuousSystem(ptr, step, ref miniStep, time, order, Flowstar.Identity_Precondition, estimation.Length, estimation, bPrint, variables);
            if (result == IntPtr.Zero)
                throw new FlowpipeException("Cannot find good range estimator");
            
            return Flowpipe.FromPtr(result, initialSet.numVars);
        }

        public Flowpipe ReachLowDegree(double step, double time, int order, int maxOrder, double[] estimation)
        {
            return Flowpipe.FromPtr(ReachLowDegreeAdaptiveOrderContinuousSystem(ptr, step, time, order, maxOrder, estimation.Length, estimation, variables), initialSet.numVars);
        }

        public Flowpipe ReachHighDegree(double step, double time, int order, double[] estimation)
        {
            return Flowpipe.FromPtr(ReachHighDegreeContinuousSystem(ptr, step, time, order, estimation.Length, estimation, variables), initialSet.numVars);
        }

        public Flowpipe ReachHighDegree(double step, ref double miniStep, double time, int order, double[] estimation)
        {
            return Flowpipe.FromPtr(ReachHighDegreeAdaptiveStepContinuousSystem(ptr, step, ref miniStep, time, order, estimation.Length, estimation, variables), initialSet.numVars);
        }

        public Flowpipe ReachHighDegree(double step, double time, int order, int maxOrder, double[] estimation)
        {
            return Flowpipe.FromPtr(ReachHighDegreeAdaptiveOrderContinuousSystem(ptr, step, time, order, maxOrder, estimation.Length, estimation, variables), initialSet.numVars);
        }

        public Flowpipe ReachNonPolynomial(double step, double time, int order, double[] estimation, bool bPrint = false)
        {
            IntPtr result = ReachNonPolynomialContinuousSystem(ptr, step, time, order, Flowstar.Identity_Precondition, estimation.Length, estimation, bPrint, variables);
            if (result == IntPtr.Zero)
                throw new FlowpipeException("Cannot find good range estimator");

            return Flowpipe.FromPtr(result, initialSet.numVars);
        }

        public Flowpipe ReachNonPolynomial(double step, ref double miniStep, double time, int order, double[] estimation, bool bPrint = false)
        {
            IntPtr result = ReachNonPolynomialAdaptiveStepContinuousSystem(ptr, step, ref miniStep, time, order, Flowstar.Identity_Precondition, estimation.Length, estimation, bPrint, variables);
            if (result == IntPtr.Zero)
                throw new FlowpipeException("Cannot find good range estimator");

            return Flowpipe.FromPtr(result, initialSet.numVars);
        }
    }
}
