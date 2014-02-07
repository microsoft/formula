using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Sahvy.Domain;
using Microsoft.Z3;
using Microsoft.Formula;
using Microsoft.Formula.API;
using Microsoft.Formula.Common;
using System.Threading.Tasks;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace Sahvy
{
    class FormulaSystem : SHSystem
    {
        public FormulaSystem(string filename, Plot3d plotter)
            : base()
        {
            axisValue[0] = new REAL(0.0);
            axisValue[1] = new REAL(0.0);
            axisValue[2] = new REAL(0.0);
            Load(filename, "M");

            string[] cNames = new string[cVariables.Count];
            string[] dNames = new string[dVariables.Count];
            DoubleInterval[] cInitial = new DoubleInterval[cVariables.Count];
            FPIntegerInterval[] dInitial = new FPIntegerInterval[dVariables.Count];
            int i = 0;
            foreach (var kvp in cVariables)
            {
                cNames[i] = kvp.Key.name;
                cInitial[i] = kvp.Value.Clone();
                i++;
            }
            i = 0;
            foreach (var kvp in dVariables)
            {
                dNames[i] = kvp.Key.Expr;
                dInitial[i] = kvp.Value.Clone();
                i++;
            }
            
            // build the ODEs
            ode = new List<AST>();
            foreach (var kvp in cVariables)
            {
                AST f;
                if (odes.TryGetValue(kvp.Key.name, out f))
                    ode.Add(f);
            }

            Print(Log.Output);
            Print(Log.Debug);

            Initialize(cNames, cInitial, dNames, dInitial, order, period);

            if (IsPolynomial)
                Log.WriteLine("Using polynomial solver");
            else if (ContainsSqrt)
                Log.WriteLine("Using approximate Taylor expansion solver");
            else
                Log.WriteLine("Using non-polynomial Taylor model solver");

            // look for lower and upper bounds            
            foreach (var kvp in dVariables)
            {
                FixedPointNumber fp = kvp.Key;
                int lo = -(1 << ((int)fp.bits - 1));
                int up = (1 << ((int)fp.bits - 1)) - 1;
                var solver = ctx.MkSimpleSolver();
                AddController(ctx, solver, kvp.Key.Expr);
                lo = LowerBound(ctx, solver, fp.Expr, fp.bits, lo, up);
                up = UpperBound(ctx, solver, fp.Expr, fp.bits, lo, up);
                var ival = new FPIntegerInterval(lo, up, fp.bits, fp.decimals);
                controlBounds.Add(fp.Expr, ival);                
                Log.Debug.WriteLine("Control variable '{0}' always in {1}", kvp.Key, ival);
            }

            Dictionary<string, DoubleInterval> initialValues = GetSystemState(initialState); // initialState.ToDictionary();
            if (!timeAxis)
                axisInitialValue[0] = axisValue[0].Eval(initialValues);
            axisInitialValue[1] = axisValue[1].Eval(initialValues);
            if (!searchIndexAxis)
                axisInitialValue[2] = axisValue[2].Eval(initialValues);                       

            this.plotter = plotter;
            if (plotter != null)
            {
                plotter.SetMinMax(axisMin[0], axisMax[0], axisMin[1], axisMax[1], axisMin[2], axisMax[2]);
                plotter.DefaultSettings();
                plotter.DrawEvent += Draw;                
            }
        }

        private Dictionary<string, FPIntegerInterval> controlBounds = new Dictionary<string, FPIntegerInterval>();

        private Dictionary<VAR, DoubleInterval> cVariables = new Dictionary<VAR, DoubleInterval>();
        private Dictionary<FixedPointNumber, FPIntegerInterval> dVariables = new Dictionary<FixedPointNumber, FPIntegerInterval>();

        // A list of ODEs (lhs is a variable)
        private Dictionary<string, AST> odes = new Dictionary<string, AST>();

        private Dictionary<string, FixedPointNumber> discreteVariablesByName = new Dictionary<string, FixedPointNumber>();

        // Assigns and pre-s in the discrete controller
        private Dictionary<string, FixedPointNumber> assigns = new Dictionary<string, FixedPointNumber>();
        
        // sample and hold assignments
        private List<KeyValuePair<AST, FixedPointNumber>> sample = new List<KeyValuePair<AST, FixedPointNumber>>();
        private List<KeyValuePair<FixedPointNumber, VAR>> hold = new List<KeyValuePair<FixedPointNumber, VAR>>();
        private List<KeyValuePair<AST, VAR>> reset = new List<KeyValuePair<AST, VAR>>();

        // Safety formula
        private List<BoolAST> safetyFormula = new List<BoolAST>();             

        // For visualization
        private string[] axisTitle = new string[3];
        private AST[] axisValue = new AST[3];
        private DoubleInterval[] axisInitialValue = new DoubleInterval[3];
        private double[] axisMin = new double[3];
        private double[] axisMax = new double[3];
        private bool timeAxis = false;
        private bool searchIndexAxis = false;

        protected Dictionary<string, DoubleInterval> GetSystemState(State q)
        {
            Dictionary<string, DoubleInterval> result = q.ToDictionary();
            Dictionary<string, DoubleInterval> hold = Hold(q);
            foreach (var kvp in hold)
                result.Add(kvp.Key, kvp.Value);
            return result;
        }

        protected override Dictionary<string, FPIntegerInterval> Sample(State q)
        {
            Dictionary<string, FPIntegerInterval> result = new Dictionary<string, FPIntegerInterval>();
            Dictionary<string, DoubleInterval> values = q.ToDictionary();
            foreach (var kvp in sample)
            {
                AST c = kvp.Key;
                FixedPointNumber fp = kvp.Value;
                var valueOfAST = c.Eval(values);
                result.Add(fp.Expr, Measure(valueOfAST, fp.bits, fp.decimals));
            }
            return result;
        }

        protected override Dictionary<string, DoubleInterval> Hold(State q)
        {
            Dictionary<string, DoubleInterval> result = new Dictionary<string, DoubleInterval>();
            foreach (var kvp in hold)
            {
                VAR c = kvp.Value;
                FixedPointNumber fp = kvp.Key;
                int i = Array.FindIndex(q.discreteNames, x => x.Equals(fp.Expr));
                result.Add(c.name, q.discreteState.axes[i].ToDoubleInterval());
            }
            return result;
        }

        protected override Dictionary<string, DoubleInterval> Reset(State q)
        {
            Dictionary<string, DoubleInterval> result = new Dictionary<string, DoubleInterval>();
            Dictionary<string, DoubleInterval> values = q.ToHybridDictionary();
            foreach (var kvp in reset)
            {
                var valueOfAST = kvp.Key.Eval(values);
                result.Add(kvp.Value.name, valueOfAST);
            }
            return result;
        }

        /// <summary>
        /// Add controller code for output/memory variable 'var'
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="solver"></param>
        /// <param name="variable"></param>
        protected override void AddController(Microsoft.Z3.Context ctx, Microsoft.Z3.Solver solver, string varName)
        {
            FixedPointNumber var = discreteVariablesByName[varName];
            FixedPointNumber expr = assigns[varName];
            BoolExprWithOverflow assert = ctx.MkFPEq(var, expr);
            solver.Assert(assert.bv);
        }

        protected override void AddControllerOverflow(Microsoft.Z3.Context ctx, Microsoft.Z3.Solver solver, string varName)
        {
            FixedPointNumber var = discreteVariablesByName[varName];
            FixedPointNumber expr = assigns[varName];
            solver.Assert(expr.overflow);            
        }

        protected override int ControllerLowerBound(string varName)
        {
            return controlBounds[varName].left;
        }

        protected override int ControllerUpperBound(string varName)
        {
            return controlBounds[varName].right;
        }

        protected override bool Unsafe(State q)
        {
            Dictionary<string, DoubleInterval> values = GetSystemState(q);
            foreach (var formula in safetyFormula)
                if (!formula.Eval(values))
                    return true;
            return false;
        }

        protected override Dictionary<string, DoubleInterval> CompactState(State q)
        {
            Dictionary<string, DoubleInterval> values = new Dictionary<string, DoubleInterval>();
            for (int i = 0; i < q.continuousNames.Length; ++i)
                values.Add(q.continuousNames[i], q.continuousState.axes[i]);
            for (int i = 0; i < q.discreteNames.Length; ++i)
            {
                var ds = q.discreteState.axes[i];
                values.Add(q.discreteNames[i], new DoubleInterval(ds.left, ds.right));
            }
            return values;
        }

        public void Print(TextWriter writer, string indent = "")
        {
            writer.WriteLine("{0}Continuous variables", indent);
            foreach (var kvp in cVariables)
                writer.WriteLine("{0}\t{1} in {2}", indent, kvp.Key, kvp.Value);
            writer.WriteLine("{0}Discrete variables", indent);
            foreach (var kvp in dVariables)
                writer.WriteLine("{0}\t{1} in {2}", indent, kvp.Key, kvp.Value);
            writer.WriteLine("{0}ODEs", indent);
            foreach (var kvp in odes)
                writer.WriteLine("{0}\t{1}' = {2}", indent, kvp.Key, kvp.Value);
            writer.WriteLine("{0}Controller code", indent);
            foreach (var kvp in assigns)
                writer.WriteLine("{0}\t{1} := {2}", indent, kvp.Key, kvp.Value);
            writer.WriteLine("{0}Hold operator", indent);
            foreach (var kvp in hold)
                writer.WriteLine("{0}\t{2} := zoh({1}) / {3}", indent, kvp.Key, kvp.Value, 1 << (int)kvp.Key.decimals);
            writer.WriteLine("{0}Sampling operator", indent);
            foreach (var kvp in sample)
                writer.WriteLine("{0}\t{2} := sample({1}) * {3}", indent, kvp.Key, kvp.Value, 1 << (int)kvp.Value.decimals);
            writer.WriteLine("{0}Safety formula", indent);
            foreach (var formula in safetyFormula)
                writer.WriteLine("{0}\t{1}", indent, formula);
        }

        // FORMULA parsing

        static public int ExtractInteger(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.RealCnst)
            {
                var t = (SHSystem_Root.RealCnst)model;
                return (int)t.Value.Numerator;
            }
            throw new Exception(String.Format("Expected real constant instead of {0}", model.Symbol));
        }
        static public double ExtractDouble(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.RealCnst)
            {
                var t = (SHSystem_Root.RealCnst)model;
                return (double)t.Value.Numerator / (double)t.Value.Denominator;
            }
            throw new Exception(String.Format("Expected real constant instead of {0}", model.Symbol));
        }

        public AST ExtractContinuousModel(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.RealCnst)
            {
                var t = (SHSystem_Root.RealCnst)model;
                return new REAL(ExtractDouble(t));
            }
            else if (model is SHSystem_Root.O.Const)
            {
                var t = (SHSystem_Root.O.Const)model;
                return new REAL(ExtractDouble(t.value));
            }
            else if (model is SHSystem_Root.O.Var)
            {
                var t = (SHSystem_Root.O.Var)model;
                var name = (string)t.name.Symbol;
                return new VAR(name);
            }
            else if (model is SHSystem_Root.O.Neg)
            {
                var t = (Domain.SHSystem_Root.O.Neg)model;
                return new NEG(ExtractContinuousModel(t.arg1));
            }
            else if (model is SHSystem_Root.O.Sin)
            {
                var t = (Domain.SHSystem_Root.O.Sin)model;
                return new SIN(ExtractContinuousModel(t.arg1));
            }
            else if (model is SHSystem_Root.O.Cos)
            {
                var t = (Domain.SHSystem_Root.O.Cos)model;
                return new COS(ExtractContinuousModel(t.arg1));
            }
            else if (model is SHSystem_Root.O.Exp)
            {
                var t = (Domain.SHSystem_Root.O.Exp)model;
                return new EXP(ExtractContinuousModel(t.arg1));
            }
            else if (model is SHSystem_Root.O.Sqrt)
            {
                var t = (Domain.SHSystem_Root.O.Sqrt)model;
                return new SQRT(ExtractContinuousModel(t.arg1));
            }
            else if (model is SHSystem_Root.O.Pow)
            {
                var t = (Domain.SHSystem_Root.O.Pow)model;
                return new POW(ExtractContinuousModel(t.arg1), ExtractInteger(t.k));
            }
            else if (model is SHSystem_Root.O.Add)
            {
                var t = (Domain.SHSystem_Root.O.Add)model;
                return new ADD(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.O.Sub)
            {
                var t = (Domain.SHSystem_Root.O.Sub)model;
                return new SUB(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.O.Mul)
            {
                var t = (Domain.SHSystem_Root.O.Mul)model;
                return new MUL(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.O.Div)
            {
                var t = (Domain.SHSystem_Root.O.Div)model;
                return new DIV(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            throw new Exception(String.Format("Unsupported language element: {0}", model.Symbol));
        }

        public BoolExprWithOverflow ExtractDiscreteBoolModel(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.C.EQ)
            {
                var t = (SHSystem_Root.C.EQ)model;
                return ctx.MkFPEq(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.LE)
            {
                var t = (SHSystem_Root.C.LE)model;
                return ctx.MkFPSLE(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.GE)
            {
                var t = (SHSystem_Root.C.GE)model;
                return ctx.MkFPSGE(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.LT)
            {
                var t = (SHSystem_Root.C.LT)model;
                return ctx.MkFPSLT(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.GT)
            {
                var t = (SHSystem_Root.C.GT)model;
                return ctx.MkFPSGT(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Not)
            {
                var t = (SHSystem_Root.C.Not)model;
                var m = ExtractDiscreteBoolModel(t.arg1);
                return new BoolExprWithOverflow(ctx.MkNot(m.bv), m.overflow, String.Format("not {0}", m.Expr));
            }
            else if (model is SHSystem_Root.C.And)
            {
                var t = (SHSystem_Root.C.And)model;
                var m1 = ExtractDiscreteBoolModel(t.arg1);
                var m2 = ExtractDiscreteBoolModel(t.arg2);
                var calc = ctx.MkAnd(m1.bv, m2.bv);
                return new BoolExprWithOverflow(calc, ctx.MkOr(m1.overflow, m2.overflow), String.Format("{0} and {1}", m1.Expr, m2.Expr));
            }
            else if (model is SHSystem_Root.C.Or)
            {
                var t = (SHSystem_Root.C.Or)model;
                var m1 = ExtractDiscreteBoolModel(t.arg1);
                var m2 = ExtractDiscreteBoolModel(t.arg2);
                var calc = ctx.MkOr(m1.bv, m2.bv);
                return new BoolExprWithOverflow(calc, ctx.MkOr(m1.overflow, m2.overflow), String.Format("{0} or {1}", m1.Expr, m2.Expr));
            }
            throw new Exception(String.Format("Unsupported language element: {0}", model.Symbol));
        }

        public BoolAST ExtractSafetyFormula(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.LE)
            {
                var t = (SHSystem_Root.LE)model;
                return new LE(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.GE)
            {
                var t = (SHSystem_Root.GE)model;
                return new GE(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.LT)
            {
                var t = (SHSystem_Root.LT)model;
                return new LT(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.GT)
            {
                var t = (SHSystem_Root.GT)model;
                return new GT(ExtractContinuousModel(t.arg1), ExtractContinuousModel(t.arg2));
            }
            else if (model is SHSystem_Root.Not)
            {
                var t = (SHSystem_Root.Not)model;
                return new NOT(ExtractSafetyFormula(t.arg1));
            }
            else if (model is SHSystem_Root.And)
            {
                var t = (SHSystem_Root.And)model;
                return new AND(ExtractSafetyFormula(t.arg1), ExtractSafetyFormula(t.arg2));
            }
            else if (model is SHSystem_Root.Or)
            {
                var t = (SHSystem_Root.Or)model;
                return new OR(ExtractSafetyFormula(t.arg1), ExtractSafetyFormula(t.arg2));
            }
            throw new Exception(String.Format("Unsupported language element: {0}", model.Symbol));
        }

        private Dictionary<Domain.SHSystem_Root.C.Pre, string> preName = new Dictionary<SHSystem_Root.C.Pre, string>();
        public string ExtractPre(Domain.SHSystem_Root.C.Pre t)
        {
            string name;
            if (!preName.TryGetValue(t, out name))
            {
                name = "#datastore#" + (preName.Count + 1);
                preName.Add(t, name);
            }
            return name;
        }

        public FixedPointNumber ExtractDiscreteModel(Microsoft.Formula.API.Generators.ICSharpTerm model)
        {
            if (model is SHSystem_Root.C.Const)
            {
                var t = (SHSystem_Root.C.Const)model;
                return ctx.MkFPscaled(ExtractInteger(t.value), (uint)ExtractInteger(t.bits), (uint)ExtractInteger(t.decimals));
            }
            if (model is SHSystem_Root.C.RConst)
            {
                var t = (SHSystem_Root.C.RConst)model;
                return ctx.MkFPfromReal(ExtractDouble(t.value), (uint)ExtractInteger(t.bits), (uint)ExtractInteger(t.decimals));
            }
            else if (model is SHSystem_Root.C.Var)
            {
                var t = (SHSystem_Root.C.Var)model;
                var name = (string)t.name.Symbol;
                var var = ctx.MkFPConst(name, (uint)ExtractInteger(t.bits), (uint)ExtractInteger(t.decimals));
                if (!discreteVariablesByName.ContainsKey(name))
                    discreteVariablesByName.Add(name, var);
                return var;
            }
            else if (model is SHSystem_Root.C.Neg)
            {
                var t = (Domain.SHSystem_Root.C.Neg)model;
                return ctx.MkFPNeg(ExtractDiscreteModel(t.arg1));
            }
            else if (model is SHSystem_Root.C.Pre)
            {
                var t = (Domain.SHSystem_Root.C.Pre)model;
                string name = ExtractPre(t);
                FixedPointNumber arg1 = ExtractDiscreteModel(t.arg1);
                var var = ctx.MkFPConst(name, arg1.bits, arg1.decimals);
                if (!discreteVariablesByName.ContainsKey(name))
                {
                    assigns.Add(name, arg1);
                    discreteVariablesByName.Add(name, var);
                }
                return var;
            }
            else if (model is SHSystem_Root.C.Add)
            {
                var t = (Domain.SHSystem_Root.C.Add)model;
                return ctx.MkFPAdd(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Sub)
            {
                var t = (Domain.SHSystem_Root.C.Sub)model;
                return ctx.MkFPSub(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Mul)
            {
                var t = (Domain.SHSystem_Root.C.Mul)model;
                return ctx.MkFPMul(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Div)
            {
                var t = (Domain.SHSystem_Root.C.Div)model;
                return ctx.MkFPSDiv(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Min)
            {
                var t = (Domain.SHSystem_Root.C.Min)model;
                return ctx.MkFPMin(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.Max)
            {
                var t = (Domain.SHSystem_Root.C.Max)model;
                return ctx.MkFPMax(ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            else if (model is SHSystem_Root.C.ITE)
            {
                var t = (Domain.SHSystem_Root.C.ITE)model;
                return ctx.MkFPITE(ExtractDiscreteBoolModel(t.test), ExtractDiscreteModel(t.arg1), ExtractDiscreteModel(t.arg2));
            }
            throw new Exception(String.Format("Unsupported language element: {0}", model.Symbol));
        }

        public void Load(string filename, string modelname)
        {
            Env env = new Microsoft.Formula.API.Env();
            InstallResult result;
            env.Install(filename, out result);
            if (result.Succeeded)
            {
                ProgramName pname = new ProgramName(filename);
                Task<ObjectGraphResult> resTask;
                Domain.SHSystem_Root.CreateObjectGraph(env, pname, modelname, out resTask);
                resTask.Wait();
                ObjectGraphResult model = resTask.Result;

                var body = Microsoft.Formula.API.Factory.Instance.MkBody();
                var find = Microsoft.Formula.API.Factory.Instance.MkFind(null, Microsoft.Formula.API.Factory.Instance.MkId("conforms"));
                body = Microsoft.Formula.API.Factory.Instance.AddConjunct(body, find);

                List<Microsoft.Formula.API.AST<Microsoft.Formula.API.Nodes.Body>> bodies = new List<AST<Microsoft.Formula.API.Nodes.Body>>();
                bodies.Add(body);
                List<Flag> flags;
                Task<QueryResult> task;
                Microsoft.Formula.Common.Rules.ExecuterStatistics exeStats;
                if (!env.Query(pname, modelname, bodies, true, true, out flags, out task, out exeStats))
                    throw new Exception(String.Format("Could not start query at {0}/{1}", filename, modelname));
                if (task == null)
                    throw new Exception(String.Format("Could not query {0}/{1}", filename, modelname));
                task.Start();
                task.Wait();
                if (task.Result.Conclusion != LiftedBool.True)
                    throw new Exception(String.Format("Model does not conform at {0}/{1}", filename, modelname));
                //body.Print(Console.Out);

                foreach (var m in model.Objects)
                {
                    if (m is SHSystem_Root.O.DiffEq)
                    {
                        var t = (SHSystem_Root.O.DiffEq)m;
                        VAR v = (VAR)ExtractContinuousModel(t.x);
                        AST rhs = ExtractContinuousModel(t.rhs);                        
                        odes.Add(v.name, rhs);
                    }
                    else if (m is SHSystem_Root.C.Assign)
                    {
                        var t = (SHSystem_Root.C.Assign)m;
                        FixedPointNumber var = ExtractDiscreteModel(t.var);
                        FixedPointNumber rhs = ExtractDiscreteModel(t.rhs);
                        assigns.Add(var.Expr, rhs);
                    }
                    else if (m is SHSystem_Root.Safe)
                    {
                        var t = (SHSystem_Root.Safe)m;
                        safetyFormula.Add(ExtractSafetyFormula(t.formula));
                    }
                    else if (m is SHSystem_Root.Sample)
                    {
                        var t = (SHSystem_Root.Sample)m;
                        AST c = ExtractContinuousModel(t.lhs);
                        FixedPointNumber d = ExtractDiscreteModel(t.rhs);
                        sample.Add(new KeyValuePair<AST, FixedPointNumber>(c, d));
                    }
                    else if (m is SHSystem_Root.Hold)
                    {
                        var t = (SHSystem_Root.Hold)m;
                        FixedPointNumber d = ExtractDiscreteModel(t.lhs);
                        VAR c = (VAR)ExtractContinuousModel(t.rhs);
                        hold.Add(new KeyValuePair<FixedPointNumber, VAR>(d, c));
                    }
                    else if (m is SHSystem_Root.Reset)
                    {
                        var t = (SHSystem_Root.Reset)m;
                        AST d = ExtractContinuousModel(t.lhs);
                        VAR c = (VAR)ExtractContinuousModel(t.rhs);
                        reset.Add(new KeyValuePair<AST, VAR>(d, c));
                    }
                    else if (m is SHSystem_Root.InitialRangeC)
                    {
                        var t = (SHSystem_Root.InitialRangeC)m;
                        VAR v = (VAR)ExtractContinuousModel(t.var);
                        double lower = ExtractDouble(t.lower);
                        double upper = ExtractDouble(t.upper);
                        cVariables.Add(v, new DoubleInterval(lower, upper));
                    }
                    else if (m is SHSystem_Root.InitialRangeD)
                    {
                        var t = (SHSystem_Root.InitialRangeD)m;
                        FixedPointNumber v = ExtractDiscreteModel(t.var);
                        int lower = ExtractInteger(t.lower);
                        int upper = ExtractInteger(t.upper);
                        dVariables.Add(v, new FPIntegerInterval(lower, upper, v.bits, v.decimals));
                    }
                    else if (m is SHSystem_Root.DiscretePeriod)
                    {
                        var t = (SHSystem_Root.DiscretePeriod)m;
                        period = ExtractDouble(t._0);
                    }
                    else if (m is SHSystem_Root.Order)
                    {
                        var t = (SHSystem_Root.Order)m;
                        order = ExtractInteger(t._0);
                    }
                    else if (m is SHSystem_Root.StepBound)
                    {
                        var t = (SHSystem_Root.StepBound)m;
                        stepBound = ExtractInteger(t._0);
                    }
                    else if (m is SHSystem_Root.CutoffThreshold)
                    {
                        var t = (SHSystem_Root.CutoffThreshold)m;
                        cutoffThreshold = ExtractDouble(t.threshold);
                    }
                    else if (m is SHSystem_Root.ErrorEstimate)
                    {
                        var t = (SHSystem_Root.ErrorEstimate)m;
                        errorEstimate = ExtractDouble(t.estimate);
                    }
                    else if (m is SHSystem_Root.SolverStep)
                    {
                        var t = (SHSystem_Root.SolverStep)m;
                        solverStep = ExtractDouble(t.step); 
                        solverMiniStep = ExtractDouble(t.ministep);
                    }
                    else if (m is SHSystem_Root.SearchProcedure)
                    {
                        var t = (SHSystem_Root.SearchProcedure)m;
                        string name = (string)t.proc.Symbol;
                        if (name.Equals("BFS")) searchProcedure = SearchProcedures.BFS;
                        else if (name.Equals("BFS_exhaustive")) searchProcedure = SearchProcedures.BFS_exhaustive;
                        else if (name.Equals("BFS_refinement")) searchProcedure = SearchProcedures.BFS_refinement;
                        else if (name.Equals("DFS")) searchProcedure = SearchProcedures.DFS;
                        else if (name.Equals("DFS_exhaustive")) searchProcedure = SearchProcedures.DFS_exhaustive;
                        else if (name.Equals("DFS_refinement")) searchProcedure = SearchProcedures.DFS_refinement;
                    }
                    else if (m is SHSystem_Root.AxisX)
                    {
                        var t = (SHSystem_Root.AxisX)m;
                        axisTitle[0] = (string)t.title.Symbol;
                        if (t.var is SHSystem_Root.UserCnst)
                            timeAxis = true;
                        else
                            axisValue[0] = ExtractContinuousModel(t.var);
                        axisMin[0] = ExtractDouble(t.minValue);
                        axisMax[0] = ExtractDouble(t.maxValue);
                    }
                    else if (m is SHSystem_Root.AxisY)
                    {
                        var t = (SHSystem_Root.AxisY)m;
                        axisTitle[1] = (string)t.title.Symbol;
                        axisValue[1] = ExtractContinuousModel(t.var);
                        axisMin[1] = ExtractDouble(t.minValue);
                        axisMax[1] = ExtractDouble(t.maxValue);
                    }
                    else if (m is SHSystem_Root.AxisZ)
                    {
                        var t = (SHSystem_Root.AxisZ)m;
                        axisTitle[2] = (string)t.title.Symbol;
                        if (t.var is SHSystem_Root.UserCnst)
                            searchIndexAxis = true;
                        else
                            axisValue[2] = ExtractContinuousModel(t.var);
                        axisMin[2] = ExtractDouble(t.minValue);
                        axisMax[2] = ExtractDouble(t.maxValue);
                    }
                }
            }
            else
            {
                StringBuilder msg = new StringBuilder();
                msg.Append("Could not load ").AppendLine(filename);
                foreach (var flag in result.Flags)
                    msg.AppendFormat("{0} in line {1}, column {2}", flag.Item2.Message, flag.Item2.Span.StartLine, flag.Item2.Span.StartCol).AppendLine();
                throw new Exception(msg.ToString());
            }
        }

        class VizData
        {
            public PlotData data;
            public Vector3d[] quad = new Vector3d[4];
        }

        private double NormalizeAlongAxis(double value, int axis)
        {
            return value;
        }

        private double[] NormalizeAlongAxis(DoubleInterval value, int axis)
        {
            double[] result =
            {
                value.left, value.right
            };

            return result;
        }

        private const double searchZDepth = 0.5;
        private List<VizData> GetStateRepresentation()
        {            
            List<VizData> result = new List<VizData>();
            bool allUnsafe = true;
            for (int j = 0; j < plotData.Count; ++j)
                if (plotData[j].type != PlotData.PlotType.UNSAFE)
                {
                    allUnsafe = false;
                    break;
                }
            for (int j = 0; j < plotData.Count; ++j)
            {
                PlotData data = plotData[j];
                if (data.q == null) 
                    continue;
                Dictionary<string, DoubleInterval> varValues = GetSystemState(data.q);
                                
                if (timeAxis)
                {
                    PlotData datanext = j < plotData.Count - 1 ? plotData[j + 1] : null;
                    if (datanext == null || datanext.q == null || data.searchIndex != datanext.searchIndex)
                        continue;
                    Dictionary<string, DoubleInterval> varValuesNext = datanext != null ? GetSystemState(datanext.q) : null;
                    // X is time
                    double time = NormalizeAlongAxis(data.q.step, 0);
                    double timenext = NormalizeAlongAxis(datanext.q.step, 0);
                    if (searchIndexAxis)
                    {                        
                        // Z is search index
                        double index = NormalizeAlongAxis(data.searchIndex, 2) * searchZDepth;
                        
                        DoubleInterval value = axisValue[1].Eval(varValues);
                        DoubleInterval valueNext = axisValue[1].Eval(varValuesNext);
                        
                        double[] nvalue = NormalizeAlongAxis(value, 1);
                        double[] nvalueNext = NormalizeAlongAxis(valueNext, 1);
                        VizData viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(time, nvalue[1], index);
                        viz.quad[1] = new Vector3d(time, nvalue[0], index);
                        viz.quad[2] = new Vector3d(timenext, nvalueNext[0], index);
                        viz.quad[3] = new Vector3d(timenext, nvalueNext[1], index);
                        result.Add(viz);
                    }
                    else
                    {
                        if (data.type == PlotData.PlotType.UNSAFE && !allUnsafe)
                            continue;
                        // Y-Z are variables
                        DoubleInterval valueY = axisValue[1].Eval(varValues);
                        DoubleInterval valueZ = axisValue[2].Eval(varValues);

                        DoubleInterval valueYnext = axisValue[1].Eval(varValuesNext);
                        DoubleInterval valueZnext = axisValue[2].Eval(varValuesNext);

                        double[] nvalueY = NormalizeAlongAxis(valueY, 1);
                        double[] nvalueZ = NormalizeAlongAxis(valueZ, 2);
                        double[] nvalueYnext = NormalizeAlongAxis(valueYnext, 1);
                        double[] nvalueZnext = NormalizeAlongAxis(valueZnext, 2);
                                                
                        VizData viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(time, nvalueY[1], nvalueZ[1]);
                        viz.quad[1] = new Vector3d(time, nvalueY[0], nvalueZ[1]);
                        viz.quad[2] = new Vector3d(timenext, nvalueYnext[0], nvalueZnext[1]);
                        viz.quad[3] = new Vector3d(timenext, nvalueYnext[1], nvalueZnext[1]);
                        result.Add(viz);

                        viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(time, nvalueY[1], nvalueZ[0]);
                        viz.quad[1] = new Vector3d(time, nvalueY[0], nvalueZ[0]);
                        viz.quad[2] = new Vector3d(timenext, nvalueYnext[0], nvalueZnext[0]);
                        viz.quad[3] = new Vector3d(timenext, nvalueYnext[1], nvalueZnext[0]);
                        result.Add(viz);

                        viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(time, nvalueY[1], nvalueZ[0]);
                        viz.quad[1] = new Vector3d(time, nvalueY[1], nvalueZ[1]);
                        viz.quad[2] = new Vector3d(timenext, nvalueYnext[1], nvalueZnext[1]);
                        viz.quad[3] = new Vector3d(timenext, nvalueYnext[1], nvalueZnext[0]);
                        result.Add(viz);

                        viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(time, nvalueY[0], nvalueZ[0]);
                        viz.quad[1] = new Vector3d(time, nvalueY[0], nvalueZ[1]);
                        viz.quad[2] = new Vector3d(timenext, nvalueYnext[0], nvalueZnext[1]);
                        viz.quad[3] = new Vector3d(timenext, nvalueYnext[0], nvalueZnext[0]);
                        result.Add(viz);               
                    }
                }
                else
                {
                    if (searchIndexAxis)
                    {
                        // X-Y are variables
                        double index = NormalizeAlongAxis(data.searchIndex, 2) * searchZDepth;
                        DoubleInterval valueX = axisValue[0].Eval(varValues);
                        DoubleInterval valueY = axisValue[1].Eval(varValues);

                        double[] nvalueX = NormalizeAlongAxis(valueX, 0);
                        double[] nvalueY = NormalizeAlongAxis(valueY, 1);

                        VizData viz = new VizData();
                        viz.data = data;
                        viz.quad[0] = new Vector3d(nvalueX[1], nvalueY[0], index);
                        viz.quad[1] = new Vector3d(nvalueX[0], nvalueY[0], index);
                        viz.quad[2] = new Vector3d(nvalueX[0], nvalueY[1], index);
                        viz.quad[3] = new Vector3d(nvalueX[1], nvalueY[1], index);
                        result.Add(viz);
                    }
                }
            }

            return result;
        }
        protected void DrawStateSpace(double top, double left, double bottom, double right)
        {
            GL.Begin(BeginMode.Quads);
            GL.Color4(OpenTK.Graphics.Color4.Black);
            GL.Vertex2(left, top);
            GL.Vertex2(left, bottom);
            GL.Vertex2(right, bottom);
            GL.Vertex2(right, top);
            GL.End();
        }
        protected void DrawState2D(double top, double left, double bottom, double right, RectangleF rect, Color color)
        {            
            double wX = right - left;
            double wY = bottom - top;
            GL.Begin(BeginMode.Quads);
            GL.Color4(color);
            GL.Vertex2(left + rect.Left * wX, top + rect.Top * wY);
            GL.Vertex2(left + rect.Left * wX, top + rect.Bottom * wY);
            GL.Vertex2(left + rect.Right * wX, top + rect.Bottom * wY);
            GL.Vertex2(left + rect.Right * wX, top + rect.Top * wY);
            GL.End();
        }
        List<RectangleF> verifiedRegions = new List<RectangleF>();
        protected override void VerifiedSafeState(State q)
        {
            if (plotter == null || q.step > 0) return;
            if (!timeAxis && !searchIndexAxis) return;            

            // Extract visualization data
            double top = 0;
            double bottom = 1;
            double left = 0;
            double right = 1;
            Dictionary<string, DoubleInterval> currentValues = GetSystemState(q);
            if (timeAxis && searchIndexAxis)
            {
                DoubleInterval current = axisValue[1].Eval(currentValues);
                top = (current.left - axisInitialValue[1].left) / axisInitialValue[1].width;
                bottom = (current.right - axisInitialValue[1].left) / axisInitialValue[1].width;
            }
            else if (timeAxis)
            {
                DoubleInterval currentY = axisValue[1].Eval(currentValues);
                DoubleInterval currentZ = axisValue[2].Eval(currentValues);
                if (axisInitialValue[1].width > 1E-6)
                {
                    top = (currentY.left - axisInitialValue[1].left) / axisInitialValue[1].width;
                    bottom = (currentY.right - axisInitialValue[1].left) / axisInitialValue[1].width;
                }
                if (axisInitialValue[2].width > 1E-6)
                {
                    left = (currentZ.left - axisInitialValue[2].left) / axisInitialValue[2].width;
                    right = (currentZ.right - axisInitialValue[2].left) / axisInitialValue[2].width;
                }
            }
            else if (searchIndexAxis)
            {
                DoubleInterval currentX = axisValue[0].Eval(currentValues);
                DoubleInterval currentY = axisValue[1].Eval(currentValues);
                if (axisInitialValue[0].width > 1E-6)
                {
                    top = (currentX.left - axisInitialValue[0].left) / axisInitialValue[0].width;
                    bottom = (currentX.right - axisInitialValue[0].left) / axisInitialValue[0].width;
                }
                if (axisInitialValue[1].width > 1E-6)
                {
                    left = (currentY.left - axisInitialValue[1].left) / axisInitialValue[1].width;
                    right = (currentY.right - axisInitialValue[1].left) / axisInitialValue[1].width;
                }
            }
            lock (plotter.plotDataLock)
            {
                verifiedRegions.Add(new RectangleF((float)left, (float)top, (float)(right - left), (float)(bottom - top)));
            }
        }
        private void DrawStatusIndicator()
        {
            if (!timeAxis && !searchIndexAxis) return;
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.MatrixMode(MatrixMode.Projection);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Ortho(-1, 1, -1, 1, -1, 1);
            GL.Disable(EnableCap.DepthTest);
                               
            double left = 0.75;
            double top = 0.75;
            double right = 0.9;
            double bottom = 0.9;
            if (timeAxis && searchIndexAxis)
                left = 0.85;
            DrawStateSpace(top, left, bottom, right);
            foreach (var rect in verifiedRegions)
                DrawState2D(top, left, bottom, right, rect, Color.Green);

            GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Modelview);
            GL.PopMatrix();
            GL.PopAttrib();
        }
        public void Draw(Object o, EventArgs e)
        {
            List<VizData> viz = GetStateRepresentation();
            double minx = axisMin[0];
            double miny = axisMin[1];
            double minz = axisMin[2];
            double maxx = axisMax[0];
            double maxy = axisMax[1];
            double maxz = axisMax[2];
            if (searchIndexAxis)
            {
                minz = axisMin[2] * 10;
                maxz = axisMax[2] * 10;
            }

            for (int i = 0; i < viz.Count; ++i)
            {
                var q = viz[i].quad;
                for (int j = 0; j < q.Length; ++j)
                {                    
                    minx = Math.Min(minx, q[j].X);
                    maxx = Math.Max(maxx, q[j].X);
                    miny = Math.Min(miny, q[j].Y);
                    maxy = Math.Max(maxy, q[j].Y);
                    minz = Math.Min(minz, q[j].Z);
                    maxz = Math.Max(maxz, q[j].Z);
                }
            }
            plotter.SetMinMax(minx, maxx, miny, maxy, minz, maxz);
            plotter.DrawGrids(10);
            DrawStatusIndicator();

            //GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            GL.PolygonOffset(1f, 1f);
            for (int i = 0; i < viz.Count; ++i)
            {                
                var v = viz[i];
                GL.Begin(BeginMode.Quads);
                switch (v.data.type)
                {
                    case PlotData.PlotType.UNKNOWN: GL.Color4(0, 0.3f, 1f, 0.8f); break;
                    case PlotData.PlotType.SAFE: GL.Color4(0, 0.4f, 0, 0.8f); break;
                    case PlotData.PlotType.UNSAFE: GL.Color4(1f, 0, 0, 0.8f); break;
                    default: GL.Color4(0, 0, 0, 0.8f); break;
                }
                foreach (var vertex in v.quad)
                    GL.Vertex3(vertex);
                GL.End();

                GL.Begin(BeginMode.Lines);
                GL.Color4(Color.Black);
                for (int j = 0; j < v.quad.Length - 1; ++j)
                {
                    GL.Vertex3(v.quad[j]);
                    GL.Vertex3(v.quad[j+1]);
                }
                GL.Vertex3(v.quad[v.quad.Length-1]);
                GL.Vertex3(v.quad[0]);
                GL.End();
            }
            GL.Disable(EnableCap.Blend);

            plotter.DrawAxisHelp(axisTitle);
        }
    }
}
