namespace Microsoft.Formula.Common.Extras
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3ArithExpr = Microsoft.Z3.ArithExpr;
    using Z3IntExpr = Microsoft.Z3.IntExpr;
    using Z3BVExpr = Microsoft.Z3.BitVecExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3Con = Microsoft.Z3.Constructor;
    using Z3Fun = Microsoft.Z3.FuncDecl;

    internal static class Z3Utilities
    {
        public static Z3Expr Ite(this Z3BoolExpr cond, Z3Context context, Z3Expr ifTrue, Z3Expr ifFalse)
        {
            Contract.Assert(cond != null && ifTrue != null && ifFalse != null);
            return context.MkITE(cond, ifTrue, ifFalse);
        }

        public static Z3BoolExpr Implies(this Z3BoolExpr expr1, Z3Context context, Z3BoolExpr expr2)
        {
            Contract.Assert(expr1 != null && expr2 != null);
            return context.MkImplies(expr1, expr2);
        }

        public static Z3BoolExpr Iff(this Z3BoolExpr expr1, Z3Context context, Z3BoolExpr expr2)
        {
            Contract.Assert(expr1 != null && expr2 != null);
            return context.MkIff(expr1, expr2);
        }

        public static Z3BoolExpr Not(this Z3BoolExpr expr1, Z3Context context)
        {
            Contract.Assert(expr1 != null);
            return context.MkNot(expr1);
        }

        public static Z3ArithExpr Neg(this Z3ArithExpr expr1, Z3Context context)
        {
            Contract.Assert(expr1 != null);
            return context.MkUnaryMinus(expr1);
        }

        public static Z3BoolExpr IsEven(this Z3IntExpr expr1, Z3Context context)
        {
            Contract.Assert(expr1 != null);
            return context.MkEq(context.MkRem(expr1, context.MkInt(2)), context.MkInt(0));
        }

        public static Z3BoolExpr Or(this Z3BoolExpr expr1, Z3Context context, Z3BoolExpr expr2)
        {
            if (expr1 == null)
            {
                return expr2;
            }
            else if (expr2 == null)
            {
                return expr1;
            }
            else
            {
                return context.MkOr(expr1, expr2);
            }
        }

        public static Z3BoolExpr And(this Z3BoolExpr expr1, Z3Context context, Z3BoolExpr expr2)
        {
            if (expr1 == null)
            {
                return expr2;
            }
            else if (expr2 == null)
            {
                return expr1;
            }
            else
            {
                return context.MkAnd(expr1, expr2);
            }
        }

        public static Z3BoolExpr UGe(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVUGE(expr1, expr2);
        }

        public static Z3BoolExpr Ge(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkGe(expr1, expr2);
        }

        public static Z3BoolExpr UGt(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVUGT(expr1, expr2);
        }

        public static Z3BoolExpr Gt(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkGt(expr1, expr2);
        }

        public static Z3BoolExpr ULe(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVULE(expr1, expr2);
        }

        public static Z3BoolExpr Le(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkLe(expr1, expr2);
        }

        public static Z3BoolExpr ULt(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVULT(expr1, expr2);
        }

        public static Z3BoolExpr Lt(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkLt(expr1, expr2);
        }

        public static Z3ArithExpr Mod(this Z3IntExpr expr1, Z3Context context, Z3IntExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkMod(expr1, expr2);
        }

        public static Z3BVExpr Index(this Z3BVExpr expr1, Z3Context context, uint index)
        {
            Contract.Requires(expr1 != null && context != null);
            return context.MkExtract(index, index, expr1);
        }

        /// <summary>
        /// Fits a bit vector expression into a bit vector with size, 
        /// either by truncating or zero padding.
        /// </summary>
        public static Z3BVExpr FitBV(this Z3BVExpr expr1, Z3Context context, uint size)
        {
            Contract.Requires(expr1 != null && context != null && size > 0);
            if (expr1.SortSize == size)
            {
                return expr1;
            }
            else if (expr1.SortSize < size)
            {
                return context.MkZeroExt(size - expr1.SortSize, expr1);
            }
            else
            {
                return context.MkExtract(size - 1, 0, expr1);
            }
        }

        public static Z3IntExpr BV2Int(this Z3BVExpr expr1, Z3Context context)
        {
            var size = expr1.SortSize;
            var parts = new Z3IntExpr[size];
            var zero = context.MkInt(0);
            var bvzero = context.MkBV(0, 1);
            for (uint i = 0; i < size; ++i)
            {
                parts[i] = (Z3IntExpr)expr1.Index(context, i).Eq(context, bvzero).Ite(
                    context,
                    zero,
                    context.MkInt(BigInteger.Pow(2, (int)i).ToString()));
            }

            return (Z3IntExpr)context.MkAdd(parts);
        }

        public static Z3BVExpr Int2BV(this Z3IntExpr expr1, Z3Context context, uint size)
        {
            Contract.Requires(expr1 != null && context != null && size > 0);

            Z3IntExpr xj, pow;
            var xi = new Z3IntExpr[size];
            var yi = new Z3BVExpr[size];
            var zero = context.MkInt(0);
            var one = context.MkInt(1);
            var bvzero = context.MkBV(0, size);
            var bvone = context.MkBV(1, size);

            xi[size - 1] = expr1;
            for (int i = ((int)size) - 2; i >= 0; --i)
            {
                pow = context.MkInt(BigInteger.Pow(2, i + 1).ToString());
                xj = xi[i + 1];
                xi[i] = (Z3IntExpr)xj.Ge(context, pow).Ite(context, xj.Sub(context, pow), xj);
            }

            Z3BVExpr coercion = bvzero;
            for (int i = ((int)size) - 1; i >= 0; --i)
            {
                pow = context.MkInt(BigInteger.Pow(2, i).ToString());
                coercion = (Z3BVExpr)xi[i].Ge(context, pow).Ite(
                    context,
                    context.MkBVAdd(context.MkBVSHL(coercion, bvone), bvone),
                    context.MkBVSHL(coercion, bvone));
            }

            Contract.Assert(coercion != null);
            return coercion;
        }

        public static Z3ArithExpr Add(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkAdd(expr1, expr2);
        }

        public static Z3BVExpr BVAdd(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVAdd(expr1, expr2);
        }

        public static Z3BVExpr BVSub(this Z3BVExpr expr1, Z3Context context, Z3BVExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkBVSub(expr1, expr2);
        }

        public static Z3ArithExpr Sub(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkSub(expr1, expr2);
        }

        public static Z3ArithExpr Mul(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkMul(expr1, expr2);
        }

        public static Z3ArithExpr Div(this Z3ArithExpr expr1, Z3Context context, Z3ArithExpr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkDiv(expr1, expr2);
        }

        public static Z3BoolExpr Eq(this Z3Expr expr1, Z3Context context, Z3Expr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkEq(expr1, expr2);
        }

        public static Z3BoolExpr NEq(this Z3Expr expr1, Z3Context context, Z3Expr expr2)
        {
            Contract.Requires(expr1 != null && expr2 != null);
            return context.MkNot(context.MkEq(expr1, expr2));
        }

        /// <summary>
        /// Performs a computation over a Z3 expressions by unfold and fold operations.
        /// If a token is provided and then Failed() during computation, then computation
        /// is immediately canceled and default(S) is returned.
        /// 
        /// Compute(t, unfold, fold) = 
        /// fold(t, Compute(t_1, unfold, fold), ... , Compute(t_1, unfold, fold)) 
        /// 
        /// where:
        /// t_1 ... t_n are returned by unfold(t)
        /// </summary>
        public static S Compute<S>(
            this Z3Expr expr,
            Func<Z3Expr, SuccessToken, IEnumerable<Z3Expr>> unfold,
            Func<Z3Expr, IEnumerable<S>, SuccessToken, S> fold,
            SuccessToken token = null)
        {
            if (expr == null)
            {
                return default(S);
            }

            Z3Expr t;
            Compute1State<S> top;
            var stack = new Stack<Compute1State<S>>();
            stack.Push(new Compute1State<S>(null, expr, unfold(expr, token)));
            if (token != null && !token.Result)
            {
                return default(S);
            }

            while (stack.Count > 0)
            {
                top = stack.Peek();
                if (top.GetNext(out t))
                {
                    stack.Push(new Compute1State<S>(top, t, unfold(t, token)));
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
                else
                {
                    if (top.Parent == null)
                    {
                        Contract.Assert(stack.Count == 1);
                        return fold(top.T, top.ChildrenValues, token);
                    }

                    top.Parent.ChildrenValues.AddLast(fold(top.T, top.ChildrenValues, token));
                    stack.Pop();
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
            }

            throw new Impossible();
        }

        private class Compute1State<S>
        {
            private IEnumerator<Z3Expr> it;

            public Z3Expr T
            {
                get;
                private set;
            }

            public Compute1State<S> Parent
            {
                get;
                private set;
            }

            public LinkedList<S> ChildrenValues
            {
                get;
                private set;
            }

            public Compute1State(Compute1State<S> parent, Z3Expr t, IEnumerable<Z3Expr> unfolding)
            {
                T = t;
                Parent = parent;
                it = unfolding == null ? null : unfolding.GetEnumerator();
                ChildrenValues = new LinkedList<S>();
            }

            public bool GetNext(out Z3Expr t)
            {
                if (it != null)
                {
                    if (it.MoveNext())
                    {
                        t = it.Current;
                        return true;
                    }
                    else
                    {
                        t = null;
                        it = null;
                        return false;
                    }
                }

                t = null;
                return false;
            }
        }
    }
}
