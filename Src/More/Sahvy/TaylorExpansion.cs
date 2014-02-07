using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sahvy
{    
    abstract public class AST
    {
        public enum TYPE { REAL, VAR, NEG, ADD, SUB, MUL, DIV, SIN, COS, POW, EXP, SQRT }
        public TYPE type;
        public AST[] children;
        
        abstract public DoubleInterval Eval(Dictionary<string,DoubleInterval> values);
        abstract public AST GetDerivative(string dvar);
        abstract public AST Substitute(string var, AST term);
        abstract public AST Simplify();
                
        public static AST operator -(AST term)
        {
            return new NEG(term);
        }
        public static AST operator +(AST A, AST B)
        {
            return new ADD(A,B);
        }
        public static AST operator -(AST A, AST B)
        {
            return new SUB(A, B);
        }
        public static AST operator *(AST A, AST B)
        {
            return new MUL(A, B);
        }
        public static AST operator /(AST A, AST B)
        {
            return new DIV(A, B);
        }
        public int Size()
        {
            int size = 1;
            if (children != null)
                for (int i = 0; i < children.Length; ++i)
                    size += children[i].Size();
            return size;
        }

        public bool IsPolynomial()
        {
            if (type == TYPE.SIN ||
                type == TYPE.COS ||
                type == TYPE.EXP ||
                type == TYPE.DIV ||
                type == TYPE.SQRT)
                return false;
            if (children != null)
                foreach (AST child in children)
                    if (!child.IsPolynomial())
                        return false;
            return true;
        }
        public bool ContainsSqrt()
        {
            if (type == TYPE.SQRT)
                return true;
            if (children != null)
                foreach (AST child in children)
                    if (child.ContainsSqrt())
                        return true;
            return false;
        }
    }

    public class REAL : AST
    {
        public DoubleInterval value;
        public REAL(double value)
        {
            this.type = TYPE.REAL;
            this.value = new DoubleInterval(value);
        }
        public REAL(DoubleInterval value)
        {
            this.value = value;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return value;
        }
        public override AST GetDerivative(string dvar)
        {
            return new REAL(new DoubleInterval(0.0));
        }
        public override string ToString()
        {
            return String.Format("[{0:g},{1:g}]", value.left, value.right);
        }
        public override AST Substitute(string var, AST term)
        {
            return this;
        }
        public override AST Simplify()
        {
            return this;
        }
    }
    public class VAR : AST
    {
        public string name;
        public VAR(string name)
        {
            this.type = TYPE.VAR;
            this.name = name;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return values[name];
        }
        public override AST GetDerivative(string dvar)
        {
            return dvar.Equals(name) ? new REAL(new DoubleInterval(1.0)) : new REAL(new DoubleInterval(0.0));
        }
        public override string ToString()
        {
            return String.Format("{0}", name);
        }
        public override AST Substitute(string var, AST term)
        {
            return name.Equals(var) ? term : this;
        }
        public override AST Simplify()
        {
            return this;
        }
    }
    public class NEG : AST
    {
        public NEG(AST term)
        {
            children = new AST[1];
            children[0] = term;
            type = TYPE.NEG;
        }
        public override DoubleInterval Eval(Dictionary<string, DoubleInterval> values)
        {
            return -children[0].Eval(values);
        }
        public override AST GetDerivative(string dvar)
        {
            return new NEG(children[0].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("-{0}", children[0]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new NEG(children[0].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                if (value.left == 0 && value.right == 0)
                    return A;
                return new REAL(-value);
            }
            return new NEG(A);
        }
    }
    public class ADD : AST
    {
        public ADD(AST A, AST B)
        {
            children = new AST[2];
            children[0] = A;
            children[1] = B;
            type = TYPE.ADD;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values) + children[1].Eval(values);
        }
        public override AST GetDerivative(string dvar)
        {
            return new ADD(children[0].GetDerivative(dvar), children[1].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("({0}+{1})", children[0], children[1]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new ADD(children[0].Substitute(var, term), children[1].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            var B = children[1].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                if (value.left == 0 && value.right == 0)
                    return B;
            }
            if (B.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)B).value;
                if (value.left == 0 && value.right == 0)
                    return A;
            }
            return new ADD(A, B);
        }
    }
    public class SUB : AST
    {
        public SUB(AST A, AST B)
        {
            children = new AST[2];
            children[0] = A;
            children[1] = B;
            type = TYPE.SUB;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values) - children[1].Eval(values);
        }
        public override AST GetDerivative(string dvar)
        {
            return new SUB(children[0].GetDerivative(dvar), children[1].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("({0}-{1})", children[0], children[1]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new SUB(children[0].Substitute(var, term), children[1].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            var B = children[1].Simplify();
            if (B.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)B).value;
                if (value.left == 0 && value.right == 0)
                    return A;
            }
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                if (value.left == 0 && value.right == 0)
                    return new NEG(B);
            }            
            return new SUB(A, B);
        }
    }
    public class MUL : AST
    {
        public MUL(AST A, AST B)
        {
            children = new AST[2];
            children[0] = A;
            children[1] = B;
            type = TYPE.MUL;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values) * children[1].Eval(values);
        }
        public override AST GetDerivative(string dvar)
        {
            return new ADD(
                new MUL(children[0].GetDerivative(dvar), children[1]),
                new MUL(children[0], children[1].GetDerivative(dvar)));
        }
        public override string ToString()
        {
            return String.Format("({0}*{1})", children[0], children[1]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new MUL(children[0].Substitute(var, term), children[1].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            var B = children[1].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                if (value.left == 1 && value.right == 1)
                    return B;
                if (value.left == 0 && value.right == 0)
                    return A;
            }
            if (B.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)B).value;
                if (value.left == 1 && value.right == 1)
                    return A;
                if (value.left == 0 && value.right == 0)
                    return B;
            }
            return new MUL(A,B);
        }
    }
    public class DIV : AST
    {
        public DIV(AST A, AST B)
        {
            children = new AST[2];
            children[0] = A;
            children[1] = B;
            type = TYPE.DIV;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values) / children[1].Eval(values);
        }
        public override AST GetDerivative(string dvar)
        {
            var A = children[0].GetDerivative(dvar).Simplify();
            var B = children[1].GetDerivative(dvar).Simplify();
            if (B.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)B).value;
                if (value.left == 0 && value.right == 0)
                    return new DIV(A, children[1]);
            }
            return
                new DIV(
                    new SUB(
                        new MUL(A, children[1]),
                        new MUL(children[0], B)),
                    new MUL(children[1], children[1]));
        }
        public override string ToString()
        {
            return String.Format("({0}/{1})", children[0], children[1]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new DIV(children[0].Substitute(var, term), children[1].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            var B = children[1].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                if (value.left == 0 && value.right == 0)
                    return A;
            }
            if (B.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)B).value;
                if (value.left == 1 && value.right == 1)
                    return A;
            }
            return new DIV(A, B);
        }
    }

    public class SIN : AST
    {
        public SIN(AST term)
        {
            children = new AST[1];
            children[0] = term;
            type = TYPE.SIN;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values).Sin();
        }
        public override AST GetDerivative(string dvar)
        {
            return new MUL(new COS(children[0]), children[0].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("sin({0})", children[0]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new SIN(children[0].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                return new REAL(value.Sin());
            }
            return new SIN(A);
        }
    }
    public class COS : AST
    {
        public COS(AST term)
        {
            children = new AST[1];
            children[0] = term;
            type = TYPE.COS;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            return children[0].Eval(values).Cos();
        }
        public override AST GetDerivative(string dvar)
        {
            return new SUB(new REAL(new DoubleInterval(0.0)), new MUL(new SIN(children[0]), children[0].GetDerivative(dvar)));
        }
        public override string ToString()
        {
            return String.Format("cos({0})", children[0]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new COS(children[0].Substitute(var, term));
        }
        public override AST Simplify()
        {            
            var A = children[0].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                return new REAL(value.Cos());
            }
            return new COS(A);
        }
    }
    public class EXP : AST
    {
        public EXP(AST term)
        {
            children = new AST[1];
            children[0] = term;
            type = TYPE.EXP;
        }
        public override DoubleInterval Eval(Dictionary<string, DoubleInterval> values)
        {
            return children[0].Eval(values).Exp();
        }
        public override AST GetDerivative(string dvar)
        {
            return new MUL(children[0].GetDerivative(dvar), new EXP(children[0]));
        }
        public override string ToString()
        {
            return String.Format("exp({0})", children[0]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new EXP(children[0].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            return new EXP(A);
        }
    }
    
    public class POW : AST
    {
        public int power;
        public POW(AST term, int k)
        {
            power = k;
            children = new AST[1];
            children[0] = term;
            type = TYPE.POW;
        }
        public override DoubleInterval Eval(Dictionary<string,DoubleInterval> values)
        {
            DoubleInterval value = children[0].Eval(values);
            DoubleInterval result = new DoubleInterval(1.0);
            if (power > 0)
                for (int k = 0; k < power; ++k)
                    result *= value;
            else
                for (int k = 0; k > power; --k)
                    result /= value;
            return result;
        }
        public override AST GetDerivative(string dvar)
        {
            return new MUL(new MUL(new REAL(new DoubleInterval(power)), new POW(children[0], power-1)), children[0].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("{0}^{1}", children[0], power);
        }
        public override AST Substitute(string var, AST term)
        {
            return new POW(children[0].Substitute(var, term), power);
        }
        public override AST Simplify()
        {
            if (power == 0)
                return new REAL(1);

            var A = children[0].Simplify();
            if (power == 1)
                return A;
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                DoubleInterval v = value;
                for (int i = 1; i < power; ++i)
                    v = value * v;
                return new REAL(v);
            }
            return new POW(A, power);
        }
    }

    public class SQRT : AST
    {
        public SQRT(AST term)
        {
            children = new AST[1];
            children[0] = term;
            type = TYPE.SQRT;
        }
        public override DoubleInterval Eval(Dictionary<string, DoubleInterval> values)
        {
            return children[0].Eval(values).Sqrt();
        }
        public override AST GetDerivative(string dvar)
        {
            return new MUL(new DIV(new REAL(1), new MUL(new REAL(2), new SQRT(children[0]))), children[0].GetDerivative(dvar));
        }
        public override string ToString()
        {
            return String.Format("sqrt({0})", children[0]);
        }
        public override AST Substitute(string var, AST term)
        {
            return new SQRT(children[0].Substitute(var, term));
        }
        public override AST Simplify()
        {
            var A = children[0].Simplify();
            if (A.type == TYPE.REAL)
            {
                DoubleInterval value = ((REAL)A).value;
                return new REAL(value.Sqrt());
            }
            return new SQRT(A);
        }
    }

    public class TaylorExpansion
    {
        static private int MaxIndex(int numVars, int maxOrder)
        {
            int result = 0;
            int mul = 1;
            for (int i = 0; i < numVars; ++i)
            {
                result += mul * maxOrder;
                mul *= (maxOrder + 1);
            }
            return result;
        }
        static private int GetIndex(int[] degrees, int maxOrder)
        {
            int result = 0;
            int mul = 1;
            for (int i = 0; i < degrees.Length; ++i)
            {
                result += mul * degrees[i];
                mul *= (maxOrder+1);
            }
            return result;
        }
        static private string GetEquation(int[] degrees, string[] variables)
        {
            StringBuilder sb = new StringBuilder();
            for (int j = 0; j < degrees.Length; ++j)
            {
                if (degrees[j] > 0)
                {
                    sb.AppendFormat("d{0}", variables[j]);
                    if (degrees[j] > 1)
                        sb.AppendFormat("^{0}", degrees[j]);
                }
            }
            return sb.ToString();
        }
        public class TaylorStructure
        {
            public int[] degrees;
            public int order;
            public int coeff;
            public AST func;
        }
        static public Tuple<List<TaylorStructure>, List<TaylorStructure>> GetExpansionStruct(AST f, string[] variables, int maxOrder)
        {
            TaylorStructure[] result = new TaylorStructure[MaxIndex(variables.Length, maxOrder+1) + 1];
            Queue<TaylorStructure> queue = new Queue<TaylorStructure>();

            Log.Debug.WriteLine("Original ODE: F = {0}", f);

            TaylorStructure ts = new TaylorStructure();
            ts.degrees = new int[variables.Length];
            ts.func = f;
            ts.order = 0;
            ts.coeff = 1;
            queue.Enqueue(ts);
            while (queue.Count > 0)
            {
                TaylorStructure e = queue.Dequeue();
                int idx = GetIndex(e.degrees, maxOrder);
                result[idx] = e;
                Log.Debug.WriteLine("dF / {0} = {1}", GetEquation(e.degrees, variables), e.func);
                
                if (e.order == maxOrder)
                    continue;
                for (int i = 0; i < variables.Length; ++i)
                {
                    TaylorStructure next = new TaylorStructure();
                    next.degrees = (int[])e.degrees.Clone();
                    next.degrees[i]++;
                    int nextidx = GetIndex(next.degrees, maxOrder);
                    if (result[nextidx] != null) continue; // already calculated
                    next.order = e.order + 1;
                    next.coeff = 1;
                    foreach (var d in next.degrees)
                        next.coeff *= fact(d);
                    next.func = e.func.GetDerivative(variables[i]).Simplify();                    
                    queue.Enqueue(next);
                }
            }
            Log.Debug.Flush();

            List<TaylorStructure> res = new List<TaylorStructure>();
            List<TaylorStructure> error = new List<TaylorStructure>();
            foreach (TaylorStructure e in result)
                if (e != null)
                {
                    if (e.order <= maxOrder)
                        res.Add(e);
                    else
                        error.Add(e);
                }
            return Tuple.Create(res,error);
        }        
        static List<int> factTable = new List<int>();
        static private int fact(int n)
        {
            int i = factTable.Count;
            if (i == 0)
            {
                factTable.Add(1);
                i++;
            }
            if (i > n)
                return factTable[n];
            int result = factTable[i-1];
            while (i <= n)
            {
                result *= i;
                factTable.Add(result);
                i++;
            }
            return result;
        }
        
        static public Polynomial ConvertPolynomial(AST f, string[] continuousNames)
        {
            if (f.type == AST.TYPE.ADD)
                return ConvertPolynomial(f.children[0], continuousNames) + ConvertPolynomial(f.children[1], continuousNames);
            else if (f.type == AST.TYPE.SUB)
                return ConvertPolynomial(f.children[0], continuousNames) - ConvertPolynomial(f.children[1], continuousNames);
            else if (f.type == AST.TYPE.MUL)
                return ConvertPolynomial(f.children[0], continuousNames) * ConvertPolynomial(f.children[1], continuousNames);
            else if (f.type == AST.TYPE.NEG)
                return -ConvertPolynomial(f.children[0], continuousNames);            
            else if (f.type == AST.TYPE.REAL)
                return new Polynomial(((REAL)f).value, continuousNames.Length + 1);
            else if (f.type == AST.TYPE.VAR)
            {
                int[] degrees = new int[continuousNames.Length + 1];
                // 0 is time
                degrees[ 1 + Array.FindIndex(continuousNames, x => x.Equals(((VAR)f).name))] = 1;
                return new Polynomial(new Monomial(new DoubleInterval(1), degrees));
            }
            else if (f.type == AST.TYPE.POW)
            {
                int[] degrees = new int[continuousNames.Length + 1];
                // 0 is time
                degrees[1 + Array.FindIndex(continuousNames, x => x.Equals(((VAR)f).name))] = 1;
                return new Polynomial(new Monomial(new DoubleInterval(1), degrees));
            }
            throw new System.Exception(String.Format("Invalid polynomial {0}", f));
        }

        static public Polynomial Expansion(List<TaylorStructure> fs, string[] variables, Dictionary<string, DoubleInterval> x0, int maxOrder)
        {
            Polynomial polynomial = new Polynomial();
            int[] deg = new int[variables.Length];
            foreach (TaylorStructure e in fs)
            {
                DoubleInterval fval = e.func.Eval(x0) / e.coeff;
                // Constant: d^alphaF / alpha!
                Polynomial p = new Polynomial(new Monomial(fval, variables.Length));
                // Multiply by h^alpha = (x-a)^alpha (first var is time)
                for (int i = 1; i < variables.Length; ++i)
                {
                    DoubleInterval a = x0[variables[i]];
                    deg[i] = 1;
                    var mx = new Monomial(new DoubleInterval(1.0), deg);
                    var ma = new Monomial(a, deg.Length);
                    for (int j = 0; j < e.degrees[i]; ++j)
                        p *= (new Polynomial(mx) - new Polynomial(ma));
                    deg[i] = 0;
                }
                p.Cutoff();
                polynomial += p;
            }

            return polynomial;
        }
    }
}
