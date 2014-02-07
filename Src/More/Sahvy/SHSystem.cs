using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Z3;
using System.Diagnostics.Contracts;
using System.Collections.Concurrent;

namespace Sahvy
{
    public class PlotData
    {
        public enum PlotType {
            SAFE,
            UNSAFE,
            UNKNOWN,
            BOUND
        };
        public PlotData()
        {
        }
        public PlotData(State q, PlotType type, int searchIndex)
        {
            this.q = q;
            this.type = type;
            this.searchIndex = searchIndex;
        }
        public State q;
        public PlotType type;
        public int searchIndex;
    }
    public class RemovePlotEventArgs : EventArgs
    {
        public RemovePlotEventArgs(State q)
        {
            this.q = q;
        }
        public State q;
    }

    public abstract class SHSystem
    {
        public SHSystem()
        {
            this.ctx = new Context();
            this.solver = ctx.MkSimpleSolver();

            this.ctx2 = new Context();
            this.solver2 = ctx.MkSimpleSolver();

            unverifiedRegions = ctx2.MkTrue();
        }
        public void Initialize(string[] continuousNames, DoubleInterval[] continuousInitialState, string[] discreteNames, FPIntegerInterval[] discreteInitialState, int order, double period)
        {            
            this.initialState = new State(0, continuousNames, new DoubleBoundingBox(continuousInitialState), discreteNames, new FPIntegerBoundingBox(discreteInitialState));

            this.period = period;
            this.order = order;
            this.discreteVariables = this.initialState.discreteNames;
                        
            IsPolynomial = true;
            ContainsSqrt = false;
            for (int i = 0; i < ode.Count; ++i)
            {
                if (!ode[i].IsPolynomial()) 
                    IsPolynomial = false;
                if (ode[i].ContainsSqrt()) 
                    ContainsSqrt = true;
            }

            // since flow* doesn't handle sqrt, do taylor expansion
            if (ContainsSqrt)
            {
                string[] varNames = new string[continuousNames.Length + 1];
                varNames[0] = "time";
                continuousNames.CopyTo(varNames, 1);
                expandedOde = new List<List<TaylorExpansion.TaylorStructure>>();
                for (int i = 0; i < ode.Count; ++i)
                {
                    var e = TaylorExpansion.GetExpansionStruct(ode[i], varNames, order);
                    expandedOde.Add(e.Item1);
                }
            }

            foreach (string s in continuousNames)
                Flowstar.DeclareStateVariable(s);
            Flowstar.SetCutoff(cutoffThreshold);

            estimate = new double[initialState.continuousNames.Length];
            for (int i = 0; i < estimate.Length; ++i)
                estimate[i] = errorEstimate;

            localVarNames = new string[continuousNames.Length];
            continuousNames.CopyTo(localVarNames, 0);
            stateVarNames = new string[continuousNames.Length];
            for (int i = 0; i < continuousNames.Length; ++i)
                stateVarNames[i] = continuousNames[i] + "'";
            tmVarNames = new string[continuousNames.Length + 1];
            tmVarNames[0] = "local_t";            
            for (int i = 0; i < continuousNames.Length; ++i)
                tmVarNames[i + 1] = continuousNames[i] + "0";
            tmVarNamesWithout0 = new string[continuousNames.Length + 1];
            tmVarNamesWithout0[0] = "time";
            continuousNames.CopyTo(tmVarNamesWithout0, 1);
        }
        protected Context ctx, ctx2;
        protected Solver solver, solver2;

        public string[] controlledVariables;
        public string[] discreteVariables;
        public double period;
        public int order;
        public State initialState;
        public int stepBound;
        
        public string[] localVarNames;
        public string[] stateVarNames;
        public string[] tmVarNames;
        public string[] tmVarNamesWithout0;

        // Solver control
        protected double[] estimate;
        protected double errorEstimate = 1E-6;
        protected double cutoffThreshold = 1E-8;
        protected double solverStep = 1;
        protected double solverMiniStep = 1;

        public bool ContainsSqrt { get; private set; }
        public bool IsPolynomial { get; private set; }
        public List<AST> ode;
        public List<List<TaylorExpansion.TaylorStructure>> expandedOde;

        public Plot3d plotter { get; set; }

        private int RoundDownMid(int lo, int hi)
        {
            int sum = lo + hi;
            if (sum % 2 == 0) return sum / 2;
            else return (sum - 1) / 2;
        }
        private int RoundUpMid(int lo, int hi)
        {
            int sum = lo + hi;
            if (sum % 2 == 0) return sum / 2;
            else return (sum + 1) / 2;
        }

        /// <summary>
        /// Find the lower bound for variable var in the interval of [lo,up] by branch-and-bound
        /// </summary>        
        public int LowerBound(Context ctx, Solver solver, string var, uint bits, int lo, int up)
        {
            while (up > lo)
            {
                int mid = RoundDownMid(lo, up);
                
                solver.Push();
                var bvar = ctx.MkBVConst(var, bits);
                solver.Assert(ctx.MkBVSGE(bvar, ctx.MkBV(lo, bits)));
                solver.Assert(ctx.MkBVSLE(bvar, ctx.MkBV(mid, bits)));
                var status = solver.Check();
                solver.Pop();

                if (status == Status.SATISFIABLE)
                {
                    up = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }
            return lo;
        }
        /// <summary>
        /// Find the upper bound for variable var in the interval of [lo,up] by branch-and-bound
        /// </summary>
        public int UpperBound(Context ctx, Solver solver, string var, uint bits, int lo, int up)
        {
            while (up > lo)
            {
                int mid = RoundUpMid(lo, up);

                solver.Push();
                var bvar = ctx.MkBVConst(var, bits);
                solver.Assert(ctx.MkBVSGE(bvar, ctx.MkBV(mid, bits)));
                solver.Assert(ctx.MkBVSLE(bvar, ctx.MkBV(up, bits)));
                var status = solver.Check();
                solver.Pop();
                
                if (status == Status.SATISFIABLE)
                {
                    lo = mid;
                }
                else
                    up = mid - 1;
            }
            return up;
        }

        protected FPIntegerInterval Measure(DoubleInterval d, uint bits, uint decimals)
        {
            double lower = d.left;
            double upper = d.right;
            for (int i = 0; i < decimals; ++i)
            {
                lower *= 2;
                upper *= 2;
            }
            return new FPIntegerInterval((int)lower, (int)(upper + 1), bits, decimals);
        }

        /// <summary>
        /// Calculate bounds for the control variables by branch-and-bound
        /// </summary>
        /// <param name="q">Current state</param>
        /// <returns>Bounds for discrete variables</returns>
        private FPIntegerBoundingBox ControllerBounds(State q)
        {
            // branch and bound
            Dictionary<string, FPIntegerInterval> measuredData = Sample(q);

            Log.Debug.WriteLine("Controller measures");
            foreach (var kvp in measuredData)
                Log.Debug.WriteLine("\t{0} = {1}", kvp.Key, kvp.Value);

            FPIntegerInterval[] result = new FPIntegerInterval[q.discreteNames.Length];
            for (int i = 0; i < q.discreteNames.Length; ++i)
            {
                solver.Reset();
                Solver solverOverflow = ctx.MkSimpleSolver();

                // Add controller code
                AddController(ctx, solver, q.discreteNames[i]);
                AddControllerOverflow(ctx, solverOverflow, q.discreteNames[i]);

                // Add measured data
                foreach (var data in measuredData)
                {
                    var bits = data.Value.bits;
                    var decimals = data.Value.decimals;
                    solver.Assert(
                        ctx.MkFPSBetween(
                            ctx.MkFPConst(data.Key, bits, decimals),
                            ctx.MkFPscaled(data.Value.left, bits, decimals),
                            ctx.MkFPscaled(data.Value.right, bits, decimals)).bv);
                    solverOverflow.Assert(
                        ctx.MkFPSBetween(
                            ctx.MkFPConst(data.Key, bits, decimals),
                            ctx.MkFPscaled(data.Value.left, bits, decimals),
                            ctx.MkFPscaled(data.Value.right, bits, decimals)).bv);
                }
                // Add discrete values from previous state
                for (int j = 0; j < q.discreteNames.Length; ++j)
                    if (i != j)
                    {
                        var data = q.discreteState.axes[j];
                        var bits = data.bits;
                        var decimals = data.decimals;
                        solver.Assert(
                            ctx.MkFPSBetween(
                                ctx.MkFPConst(q.discreteNames[j], bits, decimals),
                                ctx.MkFPscaled(data.left, bits, decimals),
                                ctx.MkFPscaled(data.right, bits, decimals)).bv);
                        solverOverflow.Assert(
                            ctx.MkFPSBetween(
                                ctx.MkFPConst(q.discreteNames[j], bits, decimals),
                                ctx.MkFPscaled(data.left, bits, decimals),
                                ctx.MkFPscaled(data.right, bits, decimals)).bv);
                    }
                                
                uint qbits = q.discreteState.axes[i].bits;
                uint qdecimals = q.discreteState.axes[i].decimals;

                // Ask lower and upper bound for controller variable
                int lo = ControllerLowerBound(q.discreteNames[i]);
                int up = ControllerUpperBound(q.discreteNames[i]);
                int lower = LowerBound(ctx, solver, q.discreteNames[i], qbits, lo, up);
                int upper = UpperBound(ctx, solver, q.discreteNames[i], qbits, lower, up);

                result[i] = new FPIntegerInterval(lower, upper, qbits, qdecimals);
                Log.Debug.WriteLine("Control variable '{0}' between {1} and {2}", q.discreteNames[i], result[i].left, result[i].right);
                Log.Debug.WriteLine(solver.ToString());

                var check = solverOverflow.Check();
                if (check == Status.SATISFIABLE)
                {
                    Log.WriteLine("Overflow error at state:");
                    q.Print(Log.Output);
                    Log.WriteLine(q.discreteNames[i]);
                    Log.WriteLine(solverOverflow.ToString());
                    Log.WriteLine(solverOverflow.Model.ToString());
                    Log.Debug.Flush();
                    throw new Exception("Controller overflow detected");
                }
            }

            return new FPIntegerBoundingBox(result);
        }

        protected abstract void VerifiedSafeState(State q);

        // Tracking the verified regions
        protected BoolExpr unverifiedRegions;
        private void VerifiedSafe(State q)
        {
            unverifiedRegions = 
                ctx2.MkAnd(
                    unverifiedRegions, 
                    ctx2.MkNot(GetCoveredRegion(ctx2, q)));
            VerifiedSafeState(q);

            Log.Debug.WriteLine("==============");
            Log.Debug.WriteLine("Verified state");
            q.Print(Log.Debug);
            Log.Debug.WriteLine("==============");
        }
        private BoolExpr GetCoveredRegion(Context context, State q)
        {
            Dictionary<string, DoubleInterval> cstate = CompactState(q);
            BoolExpr res = 
                context.MkGe(
                    context.MkRealConst("time"),
                    context.MkReal(q.step));
            foreach (var data in cstate)
            {
                res = context.MkAnd(
                        res,
                        context.MkBetween(
                            context.MkRealConst(data.Key),
                            context.MkReal(data.Value.left.ToString()),
                            context.MkReal(data.Value.right.ToString())));
            }
            return res;
        }
        private bool IsVerifiedSafe(State q)
        {
            // check if already verified
            var solver = ctx2.MkSimpleSolver();
            solver.Assert(unverifiedRegions);
            solver.Assert(GetCoveredRegion(ctx2, q));
            bool verified = solver.Check() != Status.SATISFIABLE;
            if (verified)
            {
                Log.Debug.WriteLine("==============");
                Log.Debug.WriteLine("Already verified");
                q.Print(Log.Debug);
                Log.Debug.Flush();
                Log.Debug.WriteLine("==============");
            }
            else
            {
                Log.Debug.WriteLine("==============");
                Log.Debug.WriteLine("Unverified state");
                q.Print(Log.Debug);
                Log.Debug.WriteLine("==============");
            }
            return verified;
        }
        
        abstract protected Dictionary<string, FPIntegerInterval> Sample(State q);
        abstract protected Dictionary<string, DoubleInterval> Hold(State q);
        abstract protected Dictionary<string, DoubleInterval> Reset(State q);
        abstract protected void AddController(Context ctx, Solver solver, string varName);
        virtual protected void AddControllerOverflow(Context ctx, Solver solver, string varName) { }
        abstract protected int ControllerLowerBound(string varName);
        abstract protected int ControllerUpperBound(string varName);
        abstract protected bool Unsafe(State q);
        abstract protected Dictionary<string, DoubleInterval> CompactState(State q);
        
        /// <summary>
        /// Obtain the Taylor Model for the non-linear ODE after substituting the controller response at the current state.
        /// </summary>
        private TaylorModelVec GetODE(State current)
        {
            var controlled = Hold(current);

            string[] varNames = new string[current.continuousNames.Length + 1];
            varNames[0] = "time";
            current.continuousNames.CopyTo(varNames, 1);

            // update ODEs with the controlled variables
            TaylorModel[] taylorModels = new TaylorModel[ode.Count];
            for (int i = 0; i < ode.Count; ++i)
            {
                var odeC = ode[i];
                foreach (var kvp in controlled)
                {
                    //Log.WriteLine("Controlled {0} = {1}", kvp.Key, kvp.Value);
                    odeC = odeC.Substitute(kvp.Key, new REAL(kvp.Value));
                }
                
                // calculate ODE
                Polynomial p = TaylorExpansion.ConvertPolynomial(odeC, current.continuousNames);
                taylorModels[i] = new TaylorModel(p, new DoubleInterval(0));                
            }

            return new TaylorModelVec(taylorModels);
        }

        /// <summary>
        /// Obtain the Taylor Model for the non-linear ODE after substituting the controller response at the current state.
        /// </summary>
        private TaylorModelVec GetODEExpanded(State current)
        {
            // extract current values
            Dictionary<string, DoubleInterval> x0 = current.ToDictionary();
            x0.Add("time", new DoubleInterval(current.step * period));

            // add controlled variables
            var controlled = Hold(current);
            foreach (var kvp in controlled)
                x0.Add(kvp.Key, kvp.Value);
                        
            // update ODEs with the controlled variables
            TaylorModel[] taylorModels = new TaylorModel[ode.Count];
            for (int i = 0; i < expandedOde.Count; ++i)
            {   
                // calculate ODE
                Polynomial p = TaylorExpansion.Expansion(expandedOde[i], tmVarNamesWithout0, x0, order);
                // Should calculate the error bounds...
                taylorModels[i] = new TaylorModel(p, new DoubleInterval(0));

                Console.Write("Polynomial: {0} ->", ode[i]);
                p.Dump(tmVarNames, true);                
            }

            return new TaylorModelVec(taylorModels);
        }

        private string[] GetODEString(State current)
        {
            string[] result = new string[ode.Count];
            var controlled = Hold(current);
            for (int i = 0; i < ode.Count; ++i)
            {
                var odeC = ode[i];
                foreach (var kvp in controlled)
                    odeC = odeC.Substitute(kvp.Key, new REAL(kvp.Value));
                var odeS = odeC.ToString();
                Log.WriteLine("Ode: {0}", odeS);
                result[i] = odeS;
            }
            return result;
        }

        private State ContinuousStep(State q)
        {
            var qp = q.Clone();           
            ContinuousSystem system = new ContinuousSystem(qp);
            if (IsPolynomial)
            {
                var ODE = GetODE(qp);
                system.SetODE(ODE);
                qp.flowpipe = system.ReachLowDegree(solverStep, ref solverMiniStep, period, order, estimate);
                qp.continuousState = qp.flowpipe.Eval();
            }
            else if (!ContainsSqrt)
            {
                var ODES = GetODEString(qp);
                system.SetODE(ODES);
                qp.flowpipe = system.ReachNonPolynomial(solverStep, period, order, estimate);
                qp.continuousState = qp.flowpipe.Eval();
            }
            else
            {
                var ODE = GetODEExpanded(qp);
                system.SetODE(ODE);
                qp.flowpipe = system.ReachLowDegree(solverStep, ref solverMiniStep, period, order, estimate);
                qp.continuousState = qp.flowpipe.Eval();
            }

            return qp;
        }

        private State Step(State q, int lower = Int32.MinValue, int upper = Int32.MaxValue)
        {            

            // perform continuous step based on discrete state
            Task<State> task1 = Task<State>.Factory.StartNew(() => ContinuousStep(q));
                        
            // perform discrete step (branch-and-bound on the discrete response)
            ControllerBounds(q); // based on the previous state
            Task<FPIntegerBoundingBox> task2 = Task<FPIntegerBoundingBox>.Factory.StartNew(() => ControllerBounds(q));

            task1.Wait();
            task2.Wait();
            State qp = task1.Result;
            qp.discreteState = task2.Result;
            qp.step++;

            // Reset continuous-time variables
            var reset = Reset(qp);
            foreach (var kvp in reset)
            {
                int idx = Array.FindIndex(qp.continuousNames, x => x.Equals(kvp.Key));
                if (idx != -1)
                    qp.continuousState.axes[idx] = kvp.Value;
            }

            return qp;
        }
        
        public int branchCounter = 0;
        
        private string CalculateIndent(int step)
        {
            StringBuilder indentb = new StringBuilder("\t|");
            for (int i = 0; i < step; ++i)
                indentb.Append("--");
            return indentb.ToString();
        }

        /// <summary>
        /// The basic DFS reachability algorithm
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool DFS(State q, int stepBound, int searchIndex)
        {
            if (IsVerifiedSafe(q) || q.step >= stepBound)
            {
                return false;
            }
            
            // Log state
            string indent = CalculateIndent(q.step);
            Log.Debug.WriteLine("Reached state at Time {0}s", q.step * period);
            q.Print(Log.Debug, indent.Replace('-', ' '));
            
            // Add state as unknown 
            int idx = AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);

            // if unsafe, return true
            if (Unsafe(q))
            {
                Log.Debug.WriteLine("{0}Unsafe", indent);
                q.Print(Log.Debug, indent);
                ChangePlot(idx, PlotData.PlotType.UNSAFE);
                return true;
            }
            // else continue reaching out                
            else
            {
                // Perform one transition in the LTS
                State qp;
                try{
                    qp = Step(q);
                }
                catch (FlowpipeException)
                {
                    ChangePlot(idx, PlotData.PlotType.UNSAFE);
                    return true; // Unsafe because we cannot advance the flowpipe
                }

                // Ask for the reachability of an unsafe state from qp
                bool notSafe = DFS(qp, stepBound, searchIndex);
                if (notSafe)
                {
                    ChangePlot(idx, PlotData.PlotType.UNSAFE);
                    return true;
                }
            }
            ChangePlot(idx, PlotData.PlotType.SAFE);
            VerifiedSafe(q);
            return false;
        }

        /// <summary>
        /// The basic BFS reachability algorithm
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool BFS(State init, int stepBound, int searchIndex)
        {
            Queue<State> queue = new Queue<State>();
            queue.Enqueue(init);
            while (queue.Count > 0)
            {
                var q = queue.Dequeue();
                if (IsVerifiedSafe(q) || q.step >= stepBound)
                    continue;

                // Log state
                string indent = CalculateIndent(q.step);
                Log.Debug.WriteLine("Reached state at Time {0}s", q.step * period);
                q.Print(Log.Debug, indent.Replace('-', ' '));

                // Add state as unknown 
                int idx = AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);

                // if unsafe, return true
                if (Unsafe(q))
                {
                    Log.Debug.WriteLine("{0}Unsafe", indent);
                    q.Print(Log.Debug, indent);
                    return true;
                }
                // else continue reaching out                
                else
                {
                    // Perform one transition in the LTS
                    State qp;
                    try
                    {
                        qp = Step(q);
                        // Ask for the reachability of an unsafe state from qp
                        bool notSafe = BFS(qp, stepBound, searchIndex);
                        if (notSafe)
                        {
                            ChangePlot(idx, PlotData.PlotType.UNSAFE);
                            return true;
                        }
                        else
                            ChangePlot(idx, PlotData.PlotType.SAFE);
                    }
                    catch (FlowpipeException)
                    {
                        ChangePlot(idx, PlotData.PlotType.UNSAFE);
                        return true; // Unsafe because we cannot advance the flowpipe
                    }
                }
            }            
            VerifiedSafe(init);
            return false;
        }

        int globalIndex = 0;
        /// <summary>
        /// The DFS reachability algorithm with internal branching (refinement)
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool DFS_refinement(State q, int stepBound, int searchIndex, out int changeAtStep)
        {
            changeAtStep = q.step;
            if (IsVerifiedSafe(q) || q.step >= stepBound)
            {
                Log.Debug.WriteLine("Is already verified or stepbound reached");
                return false;
            }

            // log
            string indent = CalculateIndent(q.step);
            Log.Debug.WriteLine("Reached state at Time {0}s", q.step * period);
            q.Print(Log.Debug, indent.Replace('-', ' '));
            
            // if unsafe, return true
            if (Unsafe(q))
            {
                Log.Debug.WriteLine("{0}Unsafe", indent);
                q.Print(Log.Debug, indent);
                return true;
            }
            // else continue reaching out                
            else
            {
                // Perform one transition in the LTS
                try 
                {
                    Queue<State> branches = new Queue<State>();
                    branches.Enqueue(q);
                    bool first = true, first2 = true;
                    while (branches.Count > 0)
                    {
                        State qp = branches.Dequeue();
                        State cur = Step(qp);
                        if (!first)
                            globalIndex++;
                        else
                            first = false;
                        int idx = AddPlot(qp, PlotData.PlotType.UNKNOWN, globalIndex);
                        if (first2 && cur.discreteState.axes[1].width >= 30)
                        {
                            first2 = false;
                            List<State> refinement = qp.SplitDim(1, 4);
                            foreach (var v in refinement)
                                branches.Enqueue(v);
                        }
                        else {
                            // Ask for the reachability of an unsafe state from qp                            
                            bool notSafe = DFS_refinement(cur, stepBound, searchIndex, out changeAtStep);
                            if (notSafe)
                            {                                
                                // unsafe, if any branches unsafe
                                ChangePlot(idx, PlotData.PlotType.UNSAFE);
                                return true;
                            }
                            else
                            {
                                ChangePlot(idx, PlotData.PlotType.SAFE);
                            }
                        }
                    }
                }
                catch (FlowpipeException)
                {
                    Log.Debug.WriteLine("Cannot advance pipeline");
                    return true; // Unsafe because we cannot advance the flowpipe
                }
            }
            VerifiedSafe(q);
            return false;
        }

        /// <summary>
        /// The BFS reachability algorithm with internal branching (refinement)
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool BFS_refinement(State init, int stepBound, int searchIndex)
        {
            LinkedList<State> queue = new LinkedList<State>();
            queue.AddFirst(init);
            int startIdx = CountPlot();
            while (queue.Count > 0)
            {
                State q = queue.First();
                queue.RemoveFirst();
                if (IsVerifiedSafe(q) || q.step >= stepBound)
                {
                    Log.Debug.WriteLine("Is already verified or stepbound reached");
                    continue;
                }

                // log
                string indent = CalculateIndent(q.step);
                Log.Debug.WriteLine("Reached state at Time {0}", q.step * period);
                Log.WriteLine("Branching factor {0}", queue.Count());
                q.Print(Log.Debug, indent.Replace('-', ' '));

                // if unsafe, return true
                if (Unsafe(q))
                {
                    for (int i = startIdx; i < CountPlot(); ++i)
                        ChangePlot(i, PlotData.PlotType.UNSAFE);
                    Log.Debug.WriteLine("{0}Unsafe", indent);
                    q.Print(Log.Debug, indent);
                    return true;
                }
                // else continue reaching out                
                else
                {
                    // Perform one transition in the LTS
                    try
                    {
                        State cur = Step(q);
                        AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);
                        AddPlot(cur, PlotData.PlotType.UNKNOWN, searchIndex);
                        ClosePlot();
                        
                        if (cur.discreteState.axes[1].width == 1)
                        {
                            List<State> refinement = cur.SplitDiscreteAll(1);
                            foreach (var v in refinement)
                                queue.AddLast(v);
                            Log.Debug.WriteLine("Two control responses, split into {0}", refinement.Count);
                        }
                        else if (cur.discreteState.axes[1].width > 20)
                        {
                            List<State> refinement = cur.SplitDim(1,4);
                            foreach (var v in refinement)
                                queue.AddLast(v);
                            Log.Debug.WriteLine("{0} control responses, split into {1}", cur.discreteState.axes[1].width, refinement.Count);
                        }
                        else
                        {                            
                            queue.AddLast(cur);
                        }
                    }
                    catch (FlowpipeException)
                    {
                        for (int i = startIdx; i < CountPlot(); ++i)
                            ChangePlot(i, PlotData.PlotType.UNSAFE);
                        Log.Debug.WriteLine("Cannot advance pipeline");
                        return true; // Unsafe because we cannot advance the flowpipe
                    }
                }                
            }
            for (int i = startIdx; i < CountPlot(); ++i)
                ChangePlot(i, PlotData.PlotType.SAFE);
            VerifiedSafe(init);
            return false;
        }
                
        /// <summary>
        /// The DFS reachability algorithm with branching at every step
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool DFS_exhaustive(State q, int stepBound, int searchIndex)
        {
            if (IsVerifiedSafe(q) || q.step >= stepBound)
            {
                ClosePlot();
                Log.Debug.WriteLine("Is already verified or stepbound reached");
                return false;
            }

            // log
            string indent = CalculateIndent(q.step);
            Log.Debug.WriteLine("Reached state at Time {0}s", q.step * period);
            q.Print(Log.Debug, indent.Replace('-', ' '));
            
            // if unsafe, return true
            if (Unsafe(q))
            {
                Log.Debug.WriteLine("{0}Unsafe", indent);
                q.Print(Log.Debug, indent);
                return true;
            }
            // else continue reaching out                
            else
            {
                // Perform one transition in the LTS
                int idx = AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);
                try
                {
                    State qp = Step(q);                    
                    
                    // for all control value along axis[0]
                    List<State> refinement = qp.SplitDiscreteAll(0);
                    foreach (var v in refinement)
                    {                        
                        // Ask for the reachability of an unsafe state from qp                            
                        bool notSafe = DFS_exhaustive(v, stepBound, searchIndex);
                        if (notSafe)
                        {
                            // unsafe, if any branches unsafe
                            ChangePlot(idx, PlotData.PlotType.UNSAFE);
                            return true;
                        }
                    }
                }
                catch (FlowpipeException)
                {
                    ChangePlot(idx, PlotData.PlotType.UNSAFE);
                    Log.Debug.WriteLine("Cannot advance pipeline");
                    return true; // Unsafe because we cannot advance the flowpipe
                }
                ChangePlot(idx, PlotData.PlotType.SAFE);
            }
            VerifiedSafe(q);
            return false;
        }

        /// <summary>
        /// The DFS reachability algorithm with branching at every step
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool BFS_exhaustive(State init, int stepBound, int searchIndex)
        {
            Queue<State> queue = new Queue<State>();
            queue.Enqueue(init);
            while (queue.Count > 0)
            {
                State q = queue.Dequeue();
                if (IsVerifiedSafe(q) || q.step >= stepBound)
                {
                    Log.Debug.WriteLine("Is already verified or stepbound reached");
                    continue;
                }

                // log
                string indent = CalculateIndent(q.step);
                Log.Debug.WriteLine("Reached state at Time {0}s", q.step * period);
                q.Print(Log.Debug, indent.Replace('-', ' '));

                // if unsafe, return true
                if (Unsafe(q))
                {
                    Log.Debug.WriteLine("{0}Unsafe", indent);
                    q.Print(Log.Debug, indent);
                    return true;
                }
                // else continue reaching out                
                else
                {
                    // Perform one transition in the LTS
                    try
                    {
                        State qp = Step(q);
                        AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);
                        AddPlot(qp, PlotData.PlotType.UNKNOWN, searchIndex);
                        ClosePlot();
                        
                        // for all control value along axis[0]
                        List<State> refinement = qp.SplitDiscreteAll(0);
                        foreach (var v in refinement)
                            queue.Enqueue(v);
                    }
                    catch (FlowpipeException)
                    {
                        Log.Debug.WriteLine("Cannot advance pipeline");
                        return true; // Unsafe because we cannot advance the flowpipe
                    }
                }
            }
            VerifiedSafe(init);
            return false;
        }

        /// <summary>
        /// The DFS reachability algorithm with internal branching
        /// </summary>
        /// <param name="q">Current state</param>
        /// <param name="stepBound">Upper bound for the number of steps</param>
        /// <param name="searchIndex">Unique index of the flowpipe</param>
        /// <returns>True if unsafe</returns>
        private bool DFS_reach_with_control_split(State q, int stepBound, int searchIndex)
        {
            if (IsVerifiedSafe(q) || q.step >= stepBound)
            {
                Log.Debug.WriteLine("Is already verified or stepbound reached");
                return false;
            }
            // Log state
            string indent = CalculateIndent(q.step);
            Log.Debug.WriteLine("Reached state at Time {0}", q.step * period);
            q.Print(Log.Debug, indent.Replace('-', ' '));

            // Add state as unknown 
            int idx = AddPlot(q, PlotData.PlotType.UNKNOWN, searchIndex);

            // if unsafe, return true
            if (Unsafe(q))
            {
                Log.Debug.WriteLine("{0}Unsafe", indent);
                q.Print(Log.Debug, indent);
                ChangePlot(idx, PlotData.PlotType.UNSAFE);
                return true;
            }
            // else continue reaching out                
            else
            {
                // Perform one transition in the LTS
                State qp;
                try
                {
                    qp = Step(q);
                }
                catch (FlowpipeException)
                {
                    ChangePlot(idx, PlotData.PlotType.UNSAFE);
                    Log.Debug.WriteLine("Cannot advance pipeline");
                    return true; // Unsafe because we cannot advance the flowpipe
                }

                List<State> refinement;
                int nextSearchIndex = searchIndex;
                if (qp.continuousState.MaxWidth() > 15)
                {
                    nextSearchIndex++;
                    refinement = qp.Split();
                    Log.Debug.WriteLine("Too wide state space at");
                    qp.Print(Log.Debug, indent.Replace('-',' '));
                    Log.Debug.WriteLine("Split into {0} states", refinement.Count);
                }
                else
                {
                    refinement = new List<State>();
                    refinement.Add(qp);
                }
                foreach (var v in refinement)
                {
                    int idx2 = -1;
                    if (refinement.Count > 1)
                    {
                        Log.Debug.WriteLine("Continue from");
                        v.Print(Log.Debug, indent.Replace('-', ' '));
                        idx2 = AddPlot(v, PlotData.PlotType.UNKNOWN, searchIndex);
                    }
                    // Ask for the reachability of an unsafe state from qp
                    bool notSafe = DFS_reach_with_control_split(v, stepBound, nextSearchIndex);
                    if (notSafe)
                    {
                        // unsafe, if any branches unsafe
                        if (idx2 != -1)
                            ChangePlot(idx2, PlotData.PlotType.UNSAFE);
                        ChangePlot(idx, PlotData.PlotType.UNSAFE);
                        return true;
                    }
                    if (idx2 != -1)
                        ChangePlot(idx2, PlotData.PlotType.SAFE);
                    nextSearchIndex++;
                }
            }
            ChangePlot(idx, PlotData.PlotType.SAFE);
            VerifiedSafe(q);
            return false;
        }

        // Plotting functions and data

        protected List<PlotData> plotData = new List<PlotData>();
        protected int AddPlot(State q, PlotData.PlotType type, int searchIndex)
        {
            if (plotter == null) return -1;
            int idx;
            lock (plotter.plotDataLock)
            {
                idx = plotData.Count;
                plotData.Add(new PlotData(q, type, searchIndex));
            }
            return idx;
        }
        protected int ClosePlot()
        {
            if (plotter == null) return -1;
            int idx;
            lock (plotter.plotDataLock)
            {
                idx = plotData.Count;
                plotData.Add(new PlotData());
            }
            return idx;
        }
        protected int CountPlot()
        {
            if (plotter == null) return -1;
            int idx;
            lock (plotter.plotDataLock)
            {
                idx = plotData.Count;
            }
            return idx;
        }
        protected void RemovePlot(State q)
        {
            if (plotter == null) return;
            lock (plotter.plotDataLock)
            {
                int idx = plotData.FindIndex(x => x.q.Equals(q));
                if (idx != -1)
                    plotData.RemoveAt(idx);
            }
        }
        protected void RemovePlot(int idx)
        {
            if (plotter == null) return;
            lock (plotter.plotDataLock)
            {
                plotData.RemoveAt(idx);
            }
        }
        protected void ChangePlot(int idx, PlotData.PlotType type)
        {
            if (plotter == null) return;
            lock (plotter.plotDataLock)
            {
                var data = plotData[idx];
                data.type = type;
            }
        }
        
        // Statistics
        private double currentVolume;
        private double initVolume;

        // the search index for the current flowpipe
        private int searchIndex = 0;

        protected enum SearchProcedures
        {
            BFS,
            BFS_exhaustive,
            BFS_refinement,
            DFS,
            DFS_exhaustive,
            DFS_refinement
        }
        protected SearchProcedures searchProcedure;

        /// <summary>
        /// Returns true if an unsafe state is reachable
        /// </summary>
        public bool ReachDFS(State q, int stepBound, int searchDepth)
        {
            searchIndex++;

            double qWidth = q.continuousState.MaxWidth();
            if (qWidth < cutoffThreshold && qWidth > 0)
            {
                Log.WriteLine("=== Potentially unsafe ===");
                q.Print(Log.Output);
                return true;
            }

            string indent = CalculateIndent(searchDepth).Substring(1);

            Log.WriteLine("{0}Coverage: {1}%", indent, 100 - currentVolume * 100 / initVolume);
            Log.WriteLine("{0}\tVolume / InitialVolume: {1}/{2}", indent, currentVolume, initVolume);
            Log.WriteLine("{0}\tWidth: {1}; Depth: {2}", indent, qWidth, searchDepth);

            Log.WriteLine("{0}Reachability from initial state",indent);
            q.Print(Log.Output, indent + "\t");

            int changeAtStep;
            bool notSafe = false;
            switch (searchProcedure)
            {
                case SearchProcedures.BFS: notSafe = BFS(q, stepBound, searchIndex); break;
                case SearchProcedures.BFS_exhaustive: notSafe = BFS_exhaustive(q, stepBound, searchIndex); break;
                case SearchProcedures.BFS_refinement: notSafe = BFS_refinement(q, stepBound, searchIndex); break;
                case SearchProcedures.DFS: notSafe = DFS(q, stepBound, searchIndex); break;
                case SearchProcedures.DFS_exhaustive: notSafe = DFS_exhaustive(q, stepBound, searchIndex); break;
                case SearchProcedures.DFS_refinement: notSafe = DFS_refinement(q, stepBound, searchIndex, out changeAtStep); break;
                default: notSafe = DFS(q, stepBound, searchIndex); break;
            }            
            globalIndex++;
            if (notSafe)
            {
                if (qWidth == 0)
                {
                    Log.WriteLine("=== Potentially unsafe ===");
                    q.Print(Log.Output);
                    return true;
                }            
                List<State> refinement = q.Split();
                foreach (State s in refinement)
                    if (ReachDFS(s, stepBound, searchDepth + 1))
                        return true;
            }
            else
            {
                VerifiedSafe(q);
                Log.WriteLine("{0}SAFE",indent);
                currentVolume -= q.continuousState.Volume();
            }
            return false;
        }
        public void Reach()
        {
            initVolume = initialState.continuousState.Volume();
            currentVolume = initVolume;
            if (!ReachDFS(initialState, stepBound,0))
                Log.WriteLine("=== SAFE ===");
            Log.Debug.Flush();
            Log.Output.Flush();
        }
    }
}
