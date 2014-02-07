using System;
using System.Collections.Generic;
using Microsoft.Z3;
using System.Diagnostics.Contracts;

namespace Sahvy
{
    public class FixedPointNumber
    {
        public FixedPointNumber(uint bits, uint decimals, BitVecExpr BV, BoolExpr overflow_error, string expr = "")
        {
            this.bits = bits;
            this.decimals = decimals;
            this.bv = BV;
            this.Expr = expr;
            this.overflow = overflow_error;
        }
        public uint bits { get; private set; }
        public uint decimals { get; private set; }
        public BitVecExpr bv { get; private set; }
        public BoolExpr overflow { get; private set; }
        public string Expr { get; private set; }
        public override string ToString()
        {
            return Expr;
        }
    }

    public class BoolExprWithOverflow
    {
        public BoolExprWithOverflow(BoolExpr BV, BoolExpr overflow_error, string expr = "")
        {
            this.bv = BV;
            this.Expr = expr;
            this.overflow = overflow_error;
        }
        public BoolExpr bv { get; private set; }
        public BoolExpr overflow { get; private set; }
        public string Expr { get; private set; }
        public override string ToString()
        {
            return Expr;
        }
    }

    public static class Z3Extension
    {
        static public BoolExpr MkBetween(this Context ctx, RealExpr E, RealExpr L, RealExpr U)
        {
            return ctx.MkAnd(ctx.MkGe(E, L), ctx.MkLe(E, U));
        }        
        static public FixedPointNumber MkFPfromReal(this Context ctx, double value, uint bits, uint decimals)
        {
            double val = value;
            for (int i = 0; i < decimals; ++i)
                val *= 2;
            int ival = (int)val;
            var calc = ctx.MkBV(ival, bits);
            int maxV = (1 << (int)(bits-1)) - 1;
            int minV = -(1 << (int)(bits-1));
            var overflow = (ival > maxV || ival < minV) ? ctx.MkTrue() : ctx.MkFalse();
            return new FixedPointNumber(bits, decimals, calc, overflow, ival.ToString());
        }
        static public FixedPointNumber MkFPscaled(this Context ctx, int value, uint bits, uint decimals)
        {
            var calc = ctx.MkBV(value, bits);
            int maxV = (1 << (int)(bits - 1)) - 1;
            int minV = -(1 << (int)(bits - 1));
            var overflow = (value > maxV || value < minV) ? ctx.MkTrue() : ctx.MkFalse();
            return new FixedPointNumber(bits, decimals, calc, overflow, value.ToString());
        }
        static public FixedPointNumber MkFPAbove(this Context ctx, double value, uint bits, uint decimals)
        {
            for (int i = 0; i < decimals; ++i) value *= 2;
            return MkFPscaled(ctx, (int)(value + 1), bits, decimals);
        }
        static public FixedPointNumber MkFPBelow(this Context ctx, double value, uint bits, uint decimals)
        {
            for (int i = 0; i < decimals; ++i) value *= 2;
            return MkFPscaled(ctx, (int)(value), bits, decimals);
        }
        static public FixedPointNumber MkFPConst(this Context ctx, string name, uint bits, uint decimals)
        {
            return new FixedPointNumber(bits, decimals, ctx.MkBVConst(name, bits), ctx.MkFalse(), name);
        }
        static public FixedPointNumber MkFPNeg(this Context ctx, FixedPointNumber A)
        {
            var zero = ctx.MkBV(0, A.bits);
            var calc = ctx.MkBVSub(zero, A.bv);
            var o1 = ctx.MkNot(ctx.MkBVSubNoOverflow(zero, A.bv));
            var o2 = ctx.MkNot(ctx.MkBVSubNoUnderflow(zero, A.bv, true));
            var overflow = 
                ctx.MkOr(
                    A.overflow, 
                    o1, o2);
            return new FixedPointNumber(A.bits, A.decimals, calc, overflow, String.Format("(-{0})", A.Expr));
        }
        static public FixedPointNumber MkFPAdd(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVAdd(A.bv, B.bv);
            var o1 = ctx.MkNot(ctx.MkBVAddNoOverflow(A.bv, B.bv, true));
            var o2 = ctx.MkNot(ctx.MkBVAddNoUnderflow(A.bv, B.bv));
            var overflow = 
                ctx.MkOr(
                    A.overflow, B.overflow, 
                    o1, o2);
            return new FixedPointNumber(A.bits, A.decimals, calc, overflow, String.Format("({0}+{1})", A.Expr, B.Expr));
        }
        static public FixedPointNumber MkFPSub(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVSub(A.bv, B.bv);
            var o1 = ctx.MkNot(ctx.MkBVSubNoOverflow(A.bv, B.bv));
            var o2 = ctx.MkNot(ctx.MkBVSubNoUnderflow(A.bv, B.bv, true));
            var overflow = 
                ctx.MkOr(
                    A.overflow, B.overflow, 
                    o1, o2);
            return new FixedPointNumber(A.bits, A.decimals, calc, overflow, String.Format("({0}-{1})", A.Expr, B.Expr));
        }
        static public FixedPointNumber MkFPMul(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var extA = ctx.MkSignExt(A.bits, A.bv);
            var extB = ctx.MkSignExt(A.bits, B.bv);
            var AB = ctx.MkBVMul(extA, extB);
            var calc = ctx.MkExtract(A.decimals + A.bits - 1, A.decimals, AB);
            int maxV = (1 << (int)(A.bits - 1)) - 1;
            int minV = -(1 << (int)(A.bits - 1));
            var overflow =
                ctx.MkOr(
                    A.overflow, B.overflow,
                    ctx.MkBVSGT(AB, ctx.MkBV(maxV << (int)A.decimals, A.bits * 2)),
                    ctx.MkBVSLT(AB, ctx.MkBV(minV << (int)A.decimals, A.bits * 2)));
            return new FixedPointNumber(A.bits, A.decimals, calc, overflow, String.Format("{0}*{1}/{2}", A.Expr, B.Expr, (1 << (int)A.decimals)));
        }
        static public FixedPointNumber MkFPSDiv(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var extA = ctx.MkSignExt(A.bits, A.bv);
            var extB = ctx.MkSignExt(A.bits, B.bv);
            var AB = ctx.MkBVSDiv(ctx.MkBVMul(extA, ctx.MkBV(1 << (int)A.decimals, 2*A.bits)), extB);
            var calc = ctx.MkExtract(A.bits - 1, 0, AB);
            int maxV = (1 << (int)(A.bits - 1)) - 1;
            int minV = -(1 << (int)(A.bits - 1));
            var overflow =
                ctx.MkOr(
                    A.overflow, B.overflow, 
                    ctx.MkBVSGT(AB, ctx.MkBV(maxV, A.bits * 2)),
                    ctx.MkBVSLT(AB, ctx.MkBV(minV, A.bits * 2)));
            return new FixedPointNumber(A.bits, A.decimals, calc, overflow, String.Format("{0}/{1}*{2}", A.Expr, B.Expr, (1 << (int)A.decimals)));
        }
        static public BoolExprWithOverflow MkFPEq(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkEq(A.bv, B.bv);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}={1}", A.Expr, B.Expr));
        }
        static public BoolExprWithOverflow MkFPSLE(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVSLE(A.bv, B.bv);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}<={1}", A.Expr, B.Expr));
        }
        static public BoolExprWithOverflow MkFPSGE(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVSGE(A.bv, B.bv);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}>={1}", A.Expr, B.Expr));
        }
        static public BoolExprWithOverflow MkFPSLT(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVSLT(A.bv, B.bv);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}<{1}", A.Expr, B.Expr));
        }
        static public BoolExprWithOverflow MkFPSGT(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var calc = ctx.MkBVSGT(A.bv, B.bv);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}>{1}", A.Expr, B.Expr));
        }        
        static public BoolExprWithOverflow MkFPSBetween(this Context ctx, FixedPointNumber E, FixedPointNumber L, FixedPointNumber U)
        {
            var calc = ctx.MkAnd(ctx.MkFPSGE(E, L).bv, ctx.MkFPSLE(E, U).bv);
            var overflow = ctx.MkOr(E.overflow, L.overflow, U.overflow);
            return new BoolExprWithOverflow(calc, overflow, String.Format("{0}<={1}<={2}", L.Expr, E.Expr, U.Expr));
        }
        static public FixedPointNumber MkFPMin(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new FixedPointNumber(A.bits, A.decimals, (BitVecExpr)ctx.MkITE(ctx.MkBVSLE(A.bv, B.bv), A.bv, B.bv), overflow, String.Format("min({0},{1})", A.Expr, B.Expr));
        }
        static public FixedPointNumber MkFPMax(this Context ctx, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var overflow = ctx.MkOr(A.overflow, B.overflow);
            return new FixedPointNumber(A.bits, A.decimals, (BitVecExpr)ctx.MkITE(ctx.MkBVSGE(A.bv, B.bv), A.bv, B.bv), overflow, String.Format("max({0},{1})", A.Expr, B.Expr));
        }
        static public FixedPointNumber MkFPITE(this Context ctx, BoolExprWithOverflow test, FixedPointNumber A, FixedPointNumber B)
        {
            Contract.Requires(A != null);
            Contract.Requires(B != null);
            Contract.Requires(A.bits == B.bits);
            Contract.Requires(A.decimals == B.decimals);
            var overflow = ctx.MkOr(test.overflow, A.overflow, B.overflow);
            return new FixedPointNumber(A.bits, A.decimals, (BitVecExpr)ctx.MkITE(test.bv, A.bv, B.bv), overflow, String.Format("ITE({0},{1},{2})", test.Expr, A.Expr, B.Expr));
        }
    }
}
