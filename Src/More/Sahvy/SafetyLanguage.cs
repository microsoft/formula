using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sahvy
{
    abstract public class BoolAST
    {
        public enum TYPE { LE, GE, LT, GT, AND, OR, NOT }
        public TYPE type;
        public BoolAST[] children;

        abstract public bool Eval(Dictionary<string, DoubleInterval> values);
        
        public static BoolAST operator ! (BoolAST term)
        {
            return new NOT(term);
        }
        public static BoolAST operator & (BoolAST A, BoolAST B)
        {
            return new AND(A,B);
        }
        public static BoolAST operator | (BoolAST A, BoolAST B)
        {
            return new OR(A,B);
        }
    }

    public class LE : BoolAST
    {
        AST lhs;
        AST rhs;
        public LE(AST lhs, AST rhs)
        {
            this.type = TYPE.LE;
            this.lhs = lhs;
            this.rhs = rhs;
        }        
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return lhs.Eval(values).right <= rhs.Eval(values).left;
        }
        public override string ToString()
        {
            return String.Format("{0} <= {1}", lhs, rhs);
        }
    }
    public class GE : BoolAST
    {
        AST lhs;
        AST rhs;
        public GE(AST lhs, AST rhs)
        {
            this.type = TYPE.GE;
            this.lhs = lhs;
            this.rhs = rhs;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return lhs.Eval(values).left >= rhs.Eval(values).right;
        }
        public override string ToString()
        {
            return String.Format("{0} >= {1}", lhs, rhs);
        }
    }
    public class LT : BoolAST
    {
        AST lhs;
        AST rhs;
        public LT(AST lhs, AST rhs)
        {
            this.type = TYPE.LT;
            this.lhs = lhs;
            this.rhs = rhs;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return lhs.Eval(values).right < rhs.Eval(values).left;
        }
        public override string ToString()
        {
            return String.Format("{0} < {1}", lhs, rhs);
        }
    }
    public class GT : BoolAST
    {
        AST lhs;
        AST rhs;
        public GT(AST lhs, AST rhs)
        {
            this.type = TYPE.GT;
            this.lhs = lhs;
            this.rhs = rhs;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return lhs.Eval(values).left > rhs.Eval(values).right;
        }
        public override string ToString()
        {
            return String.Format("{0} > {1}", lhs, rhs);
        }
    }
    public class NOT : BoolAST
    {
        public NOT(BoolAST term)
        {
            this.type = TYPE.NOT;
            this.children = new BoolAST[1];
            this.children[0] = term;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return !children[0].Eval(values);
        }
        public override string ToString()
        {
            return String.Format("!{0}", children[0]);
        }
    }
    public class AND : BoolAST
    {
        public AND(BoolAST A, BoolAST B)
        {
            this.type = TYPE.AND;
            this.children = new BoolAST[2];
            this.children[0] = A;
            this.children[1] = B;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return children[0].Eval(values) && children[1].Eval(values);
        }
        public override string ToString()
        {
            return String.Format("{0} and {1}", children[0], children[1]);
        }
    }
    public class OR : BoolAST
    {
        public OR(BoolAST A, BoolAST B)
        {
            this.type = TYPE.OR;
            this.children = new BoolAST[2];
            this.children[0] = A;
            this.children[1] = B;
        }
        public override bool Eval(Dictionary<string, DoubleInterval> values)
        {
            return children[0].Eval(values) || children[1].Eval(values);
        }
        public override string ToString()
        {
            return String.Format("{0} or {1}", children[0], children[1]);
        }
    }
}
