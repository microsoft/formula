namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Common;
    using Common.Extras;
    using Common.Rules;
    using Compiler;
    using Solver;

    using Z3Expr = Microsoft.Z3.Expr;
    using Z3ArithExpr = Microsoft.Z3.ArithExpr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;

    internal static class OpLibrary
    {
        /// <summary>
        /// Types that distinguish at least this many numbers are widened into an 
        /// infinite set of integers.
        /// </summary>
        private const int NumWideningWidth = 101;

        private static readonly LiftedBool[] UnCompr = new LiftedBool[] { LiftedBool.True };
        private static readonly LiftedBool[] UnNoCompr = new LiftedBool[] { LiftedBool.False };
        private static readonly LiftedBool[] BinNoCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.False };
        private static readonly LiftedBool[] BinSecCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.True };
        private static readonly LiftedBool[] TerNoCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.False, LiftedBool.False };
        private static readonly LiftedBool[] TerTerCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.False, LiftedBool.True };
        private static readonly LiftedBool[] FourNoCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.False, LiftedBool.False, LiftedBool.False };
        private static readonly LiftedBool[] FiveNoCompr = new LiftedBool[] { LiftedBool.False, LiftedBool.False, LiftedBool.False, LiftedBool.False, LiftedBool.False };

        /// <summary>
        /// Validates that the syntactic application of an operator satisfies some basic rules.
        /// </summary>
        #region Syntax validation
        internal static bool ValidateUse_Not(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Not);
            return ValidateArity(ft, "not", UnNoCompr, flags);
        }

        internal static bool ValidateUse_Neg(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Neg);
            return ValidateArity(ft, "unary -", UnNoCompr, flags);
        }

        internal static bool ValidateUse_Sign(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Sign);
            return ValidateArity(ft, "sign", UnNoCompr, flags);
        }

        internal static bool ValidateUse_And(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.And);
            return ValidateArity(ft, "and", BinNoCompr, flags);
        }

        internal static bool ValidateUse_AndAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.AndAll);
            return ValidateArity(ft, "andAll", BinSecCompr, flags);
        }

        internal static bool ValidateUse_Or(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Or);
            return ValidateArity(ft, "or", BinNoCompr, flags);
        }

        internal static bool ValidateUse_OrAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.OrAll);
            return ValidateArity(ft, "orAll", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Impl(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Impl);
            return ValidateArity(ft, "impl", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Add(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Add);
            return ValidateArity(ft, "+", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Sub(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Sub);
            return ValidateArity(ft, "-", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Mul(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Mul);
            return ValidateArity(ft, "*", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Div(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Div);
            return ValidateArity(ft, "/", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Mod(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Mod);
            return ValidateArity(ft, "%", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Count(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Count);
            return ValidateArity(ft, "count", UnCompr, flags);
        }

        internal static bool ValidateUse_SymAnd(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.SymAnd);
            return ValidateArity(ft, "symand", UnCompr, flags);
        }

        internal static bool ValidateUse_SymAndAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.SymAndAll);
            return ValidateArity(ft, "symandall", UnCompr, flags);
        }

        internal static bool ValidateUse_SymCount(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.SymCount);
            return ValidateArity(ft, "symcount", UnCompr, flags);
        }

        internal static bool ValidateUse_GCD(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.GCD);
            return ValidateArity(ft, "gcd", BinNoCompr, flags);
        }

        internal static bool ValidateUse_GCDAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.GCDAll);
            return ValidateArity(ft, "gcdAll", BinSecCompr, flags);
        }

        internal static bool ValidateUse_LCM(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LCM);
            return ValidateArity(ft, "lcm", BinNoCompr, flags);
        }

        internal static bool ValidateUse_LCMAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LCMAll);
            return ValidateArity(ft, "lcmAll", BinSecCompr, flags);
        }

        internal static bool ValidateUse_Prod(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Prod);
            return ValidateArity(ft, "prod", BinSecCompr, flags);
        }

        internal static bool ValidateUse_Qtnt(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Qtnt);
            return ValidateArity(ft, "qtnt", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Sum(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Sum);
            return ValidateArity(ft, "sum", BinSecCompr, flags);
        }

        internal static bool ValidateUse_IsSubstring(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.IsSubstring);
            return ValidateArity(ft, "isSubstring", BinNoCompr, flags);
        }

        internal static bool ValidateUse_StrAfter(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrAfter);
            return ValidateArity(ft, "strAfter", BinNoCompr, flags);
        }

        internal static bool ValidateUse_StrBefore(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrBefore);
            return ValidateArity(ft, "strBefore", BinNoCompr, flags);
        }

        internal static bool ValidateUse_StrFind(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrFind);
            return ValidateArity(ft, "strFind", TerNoCompr, flags);
        }

        internal static bool ValidateUse_StrGetAt(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrGetAt);
            return ValidateArity(ft, "strGetAt", BinNoCompr, flags);
        }

        internal static bool ValidateUse_StrJoin(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrJoin);
            return ValidateArity(ft, "strJoin", BinNoCompr, flags);
        }

        internal static bool ValidateUse_StrReplace(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrReplace);
            return ValidateArity(ft, "strReplace", TerNoCompr, flags);
        }

        internal static bool ValidateUse_StrLength(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrLength);
            return ValidateArity(ft, "strLength", UnNoCompr, flags);
        }

        internal static bool ValidateUse_StrLower(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrLower);
            return ValidateArity(ft, "strLower", UnNoCompr, flags);
        }

        internal static bool ValidateUse_StrReverse(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrReverse);
            return ValidateArity(ft, "strReverse", UnNoCompr, flags);
        }

        internal static bool ValidateUse_StrUpper(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.StrUpper);
            return ValidateArity(ft, "strUpper", UnNoCompr, flags);
        }

        internal static bool ValidateUse_Max(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Max);
            return ValidateArity(ft, "max", BinNoCompr, flags);
        }

        internal static bool ValidateUse_MaxAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.MaxAll);
            return ValidateArity(ft, "maxAll", BinSecCompr, flags);
        }

        internal static bool ValidateUse_Min(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.Min);
            return ValidateArity(ft, "min", BinNoCompr, flags);
        }

        internal static bool ValidateUse_MinAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.MinAll);
            return ValidateArity(ft, "minAll", BinSecCompr, flags);
        }

        internal static bool ValidateUse_LstLength(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstLength);
            return ValidateArity(ft, "lstLength", BinNoCompr, flags);
        }

        internal static bool ValidateUse_LstReverse(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstReverse);
            return ValidateArity(ft, "lstReverse", BinNoCompr, flags);
        }

        internal static bool ValidateUse_LstFind(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstFind);
            return ValidateArity(ft, "lstFind", FourNoCompr, flags);
        }

        internal static bool ValidateUse_LstFindAll(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstFindAll);
            return ValidateArity(ft, "lstFindAll", TerNoCompr, flags);
        }

        internal static bool ValidateUse_LstFindAllNot(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstFindAllNot);
            return ValidateArity(ft, "lstFindAllNot", TerNoCompr, flags);
        }

        internal static bool ValidateUse_LstGetAt(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.LstGetAt);
            return ValidateArity(ft, "lstGetAt", TerNoCompr, flags);
        }

        internal static bool ValidateUse_RflIsMember(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.RflIsMember);
            return ValidateArity(ft, "rflIsMember", BinNoCompr, flags);
        }

        internal static bool ValidateUse_RflIsSubtype(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.RflIsSubtype);
            return ValidateArity(ft, "rflIsSubtype", BinNoCompr, flags);
        }

        internal static bool ValidateUse_RflGetArgType(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.RflGetArgType);
            return ValidateArity(ft, "rflGetArgType", BinNoCompr, flags);
        }

        internal static bool ValidateUse_RflGetArity(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.RflGetArity);
            return ValidateArity(ft, "rflGetArity", UnNoCompr, flags);
        }

        internal static bool ValidateUse_ToList(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.ToList);
            return ValidateArity(ft, "toList", TerTerCompr, flags);
        }

        internal static bool ValidateUse_ToOrdinal(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.ToOrdinal);
            return ValidateArity(ft, "toOrdinal", TerTerCompr, flags);
        }

        internal static bool ValidateUse_ToNatural(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.ToNatural);
            return ValidateArity(ft, "toNatural", UnNoCompr, flags);
        }

        internal static bool ValidateUse_ToString(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.ToString);
            return ValidateArity(ft, "toString", UnNoCompr, flags);
        }

        internal static bool ValidateUse_ToSymbol(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.FuncTerm);
            var ft = (FuncTerm)n;
            Contract.Assert(ft.Function is OpKind && ((OpKind)ft.Function) == OpKind.ToSymbol);
            return ValidateArity(ft, "toSymbol", UnNoCompr, flags);
        }

        internal static bool ValidateUse_Eq(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Eq);
            return ValidateArity(rc, "=", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Neq(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Neq);
            return ValidateArity(rc, "!=", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Le(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Le);
            return ValidateArity(rc, "<=", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Lt(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Lt);
            return ValidateArity(rc, "<", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Ge(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Ge);
            return ValidateArity(rc, ">=", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Gt(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Gt);
            return ValidateArity(rc, ">=", BinNoCompr, flags);
        }

        internal static bool ValidateUse_Typ(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.Typ);
            if (!ValidateArity(rc, ":", BinNoCompr, flags))
            {
                return false;
            }

            foreach (var a in EnumerableMethods.GetEnumerable<Node>(rc.Arg1, rc.Arg2))
            {
                if (a.NodeKind == NodeKind.Id)
                {
                    continue;
                }

                var flag = new Flag(
                    SeverityKind.Error,
                    a,
                    Constants.BadSyntax.ToString(": expects a variable and a type id"),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        internal static bool ValidateUse_No(Node n, List<Flag> flags)
        {
            Contract.Requires(n.NodeKind == NodeKind.RelConstr);
            var rc = (RelConstr)n;
            Contract.Assert(rc.Op == RelKind.No);
            return ValidateArity(rc, "no", UnCompr, flags);
        }

        internal static bool ValidateUse_Reserved(Node n, List<Flag> flags)
        {
            throw new InvalidOperationException();
        }

        internal static Term[] TypeApprox_Reserved(TermIndex index, Term[] args)
        {
            throw new InvalidOperationException();
        }

        internal static Term Evaluator_NotImplemented(Executer facts, Bindable[] values)
        {
            throw new NotImplementedException();
        }

        internal static Term Evaluator_Reserved(Executer facts, Bindable[] values)
        {
            throw new InvalidOperationException();
        }

        internal static Term Evaluator_Neg(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            Rational r1;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, out r1))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(new Rational(BigInteger.Negate(r1.Numerator), r1.Denominator), out wasAdded);
        }

        internal static Term Evaluator_RflIsMember(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term type;
            if (!ToType(values[1].Binding, out type))
            {
                return null;
            }

            return facts.TermIndex.IsGroundMember(type, values[0].Binding) ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_RflIsSubtype(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Term typeLft, typeRt;
            if (!ToType(values[0].Binding, out typeLft) ||
                !ToType(values[1].Binding, out typeRt))
            {
                return null;
            }

            return facts.TermIndex.IsSubtypeWidened(typeLft, typeRt) ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_LstLength(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            BigInteger len = 0;
            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            while (list.Symbol == listSymbol)
            {
                ++len;
                list = list.Args[1];
            }

            bool wasAdded;
            return facts.TermIndex.MkCnst(new Rational(len, BigInteger.One), out wasAdded);
        }

        internal static Term Evaluator_LstReverse(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            var revQueue = new Queue<Term>();
            while (list.Symbol == listSymbol)
            {
                revQueue.Enqueue(list.Args[0]);
                list = list.Args[1];
            }

            bool wasAdded;
            //// Now reverse the list keeping the same list terminator (at the top of the stack).
            var revList = list;
            while (revQueue.Count > 0)
            {
                revList = facts.TermIndex.MkApply(listSymbol, new Term[] { revQueue.Dequeue(), revList }, out wasAdded); 
            }

            return revList;
        }

        internal static Term Evaluator_LstFind(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 4);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            var searchValue = values[2].Binding;
            while (list.Symbol == listSymbol)
            {
                if (Unifier.IsUnifiable(list.Args[0], searchValue))
                {
                    return list.Args[0];
                }
                list = list.Args[1];
            }

            return values[3].Binding;
        }

        internal static Term Evaluator_LstFindAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            var searchValue = values[2].Binding;
            var listStack = new Stack<Term>();
            int pos = 0;
            bool wasAdded;
            while (list.Symbol == listSymbol)
            {
                if (Unifier.IsUnifiable(list.Args[0], searchValue))
                {
                    var newTerm = facts.TermIndex.MkCnst(new Rational(pos, BigInteger.One), out wasAdded);
                    listStack.Push(newTerm);
                }

                pos++;
                list = list.Args[1];
            }

            var newList = list;
            while (!listStack.IsEmpty())
            {
                newList = facts.TermIndex.MkApply(listSymbol, new Term[] { listStack.Pop(), newList }, out wasAdded); 
            }

            return newList;
        }

        internal static Term Evaluator_LstFindAllNot(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            var searchValue = values[2].Binding;
            var listStack = new Stack<Term>();
            while (list.Symbol == listSymbol)
            {
                if (!Unifier.IsUnifiable(list.Args[0], searchValue))
                {
                    listStack.Push(list.Args[0]);
                }
                list = list.Args[1];
            }

            bool wasAdded;
            var newList = list;
            while (!listStack.IsEmpty())
            {
                newList = facts.TermIndex.MkApply(listSymbol, new Term[] { listStack.Pop(), newList }, out wasAdded); 
            }

            return newList;
        }

        internal static Term Evaluator_LstGetAt(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            Rational ind;
            
            if (!ToNumerics(values[2].Binding, out ind) ||
                !ind.IsInteger ||
                ind.Sign < 0)
            {
                return null;
            }
            
            var listSymbol = ((UserSortSymb)listConSort.Symbol).DataSymbol;
            var list = values[1].Binding;
            var targetIndex = (int) ind.Numerator;
            var listStack = new Stack<Term>();
            while (list.Symbol == listSymbol)
            {
                if (targetIndex <= 0)
                {
                    return list.Args[0];
                }
                list = list.Args[1];
                targetIndex -= 1;
            }

            return null;
        }

        internal static Term Evaluator_RflGetArgType(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational ind;
            Term type;
            UserSymbol dataSymb;
            if (!ToType(values[0].Binding, out type, true) ||
                !ToNumerics(values[1].Binding, out ind) ||
                !ind.IsInteger ||
                ind.Sign < 0 ||
                ind.Numerator >= (dataSymb = ((UserSortSymb)type.Symbol).DataSymbol).Arity)
            {
                return null;
            }

            bool wasAdded;
            UserSymbol us;
            var result = dataSymb.Namespace.TryGetSymbol(string.Format("#{0}[{1}]", dataSymb.Name, (int)ind.Numerator), out us);
            Contract.Assert(result);
            return facts.TermIndex.MkApply(us, TermIndex.EmptyArgs, out wasAdded);
        }

        internal static Term Evaluator_RflGetArity(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            if (values[0].Binding.Symbol.Kind != SymbolKind.UserCnstSymb)
            {
                return null;
            }

            var uc = (UserCnstSymb)values[0].Binding.Symbol;
            if (!uc.IsTypeConstant)
            {
                return null;
            }

            bool wasAdded;
            UserSymbol us;
            if (uc.Name.Contains("[") || !uc.Namespace.TryGetSymbol(uc.Name.Substring(1), out us))
            {
                return facts.TermIndex.MkCnst(Rational.Zero, out wasAdded);
            }
            else
            {
                return facts.TermIndex.MkCnst(new Rational(us.Arity), out wasAdded);
            }
        }

        internal static Term Evaluator_IsSubstring(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            string str1, str2;
            if (!ToStrings(values[0].Binding, out str1) ||
                !ToStrings(values[1].Binding, out str2))
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str1))
            {
                return string.IsNullOrEmpty(str2) ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
            }
            else
            {
                return str2.Contains(str1) ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
            }
        }

        internal static Term Evaluator_StrAfter(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            string str;
            Rational ind;
            if (!ToStrings(values[0].Binding, out str) ||
                !ToNumerics(values[1].Binding, out ind) || 
                !ind.IsInteger || 
                ind.Sign < 0)
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str) || ind.Numerator >= str.Length)
            {
                return facts.TermIndex.EmptyStringValue;
            }
            else
            {
                bool wasAdded;
                return facts.TermIndex.MkCnst(str.Substring((int)ind.Numerator), out wasAdded);
            }
        }

        internal static Term Evaluator_StrBefore(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            string str;
            Rational ind;
            if (!ToStrings(values[0].Binding, out str) ||
                !ToNumerics(values[1].Binding, out ind) ||
                !ind.IsInteger ||
                ind.Sign < 0)
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str) || ind.Numerator.IsZero)
            {
                return facts.TermIndex.EmptyStringValue;
            }
            else if (ind.Numerator > str.Length)
            {
                return values[0].Binding;
            }
            else
            {
                bool wasAdded;
                return facts.TermIndex.MkCnst(str.Substring(0, (int)ind.Numerator), out wasAdded);
            }
        }

        internal static Term Evaluator_StrFind(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            string str1, str2;
            if (!ToStrings(values[0].Binding, out str1) ||
                !ToStrings(values[1].Binding, out str2))
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str1))
            {
                return string.IsNullOrEmpty(str2) ? facts.TermIndex.ZeroValue : values[2].Binding;
            }
            else
            {
                bool wasAdded;
                var first = str2.IndexOf(str1);
                return first >= 0 ? facts.TermIndex.MkCnst(new Rational(first), out wasAdded) : values[2].Binding;
            }
        }

        internal static Term Evaluator_StrGetAt(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            string str;
            Rational ind;
            if (!ToStrings(values[0].Binding, out str) ||
                !ToNumerics(values[1].Binding, out ind) ||
                !ind.IsInteger ||
                ind.Sign < 0)
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str) || ind.Numerator >= str.Length)
            {
                return facts.TermIndex.EmptyStringValue;
            }
            else
            {
                bool wasAdded;
                return facts.TermIndex.MkCnst(str.Substring((int)ind.Numerator, 1), out wasAdded);
            }
        }

        internal static Term Evaluator_StrJoin(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            string str1, str2;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str1) || 
                !ToStrings(values[1].Binding, out str2))
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str1))
            {
                return facts.TermIndex.MkCnst(str2, out wasAdded);
            }
            else if (string.IsNullOrEmpty(str2))
            {
                return facts.TermIndex.MkCnst(str1, out wasAdded);
            }
            else
            {
                return facts.TermIndex.MkCnst(string.Concat(str1, str2), out wasAdded);
            }
        }

        internal static Term Evaluator_StrReplace(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            string str1, str2, str3;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str1) ||
                !ToStrings(values[1].Binding, out str2) ||
                !ToStrings(values[2].Binding, out str3))
            {
                return null;
            }
            else if (string.IsNullOrEmpty(str1))
            {
                return facts.TermIndex.MkCnst(str1, out wasAdded);
            }
            else if (string.IsNullOrEmpty(str2))
            {
                return facts.TermIndex.MkCnst(str1, out wasAdded);
            }
            else
            {
                return facts.TermIndex.MkCnst(str1.Replace(str2, str3), out wasAdded);
            }
        }

        internal static Term Evaluator_StrLength(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            string str;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(new Rational(str.Length), out wasAdded);
        }

        internal static Term Evaluator_StrLower(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            string str;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(str.ToLowerInvariant(), out wasAdded);
        }

        internal static Term Evaluator_StrReverse(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            string str;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str))
            {
                return null;
            }

            var rev = new StringBuilder(str.Length);
            for (int i = str.Length - 1; i >= 0; --i)
            {
                rev.Append(str[i]);
            }

            return facts.TermIndex.MkCnst(rev.ToString(), out wasAdded);
        }

        internal static Term Evaluator_StrUpper(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            string str;
            bool wasAdded;
            if (!ToStrings(values[0].Binding, out str))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(str.ToUpperInvariant(), out wasAdded);
        }

        internal static Term Evaluator_ToList(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            Term listConSort;
            if (!ToType(values[0].Binding, out listConSort))
            {
                return null;
            }

            var sort = (UserSortSymb)listConSort.Symbol;
            var data = sort.DataSymbol;
            if (data.Arity != 2 || !data.CanonicalForm[1].Contains(sort))
            {
                return null;
            }
            else if (!facts.TermIndex.IsGroundMember(facts.TermIndex.GetCanonicalTerm(data, 1), values[1].Binding))
            {
                return null;
            }

            int nResults;
            Term item;
            var result = values[1].Binding;
            Set<Term> sortedItems = new Set<Term>(facts.TermIndex.LexicographicCompare);
            using (var it = facts.Query(values[2].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return result;
                }

                while (it.MoveNext())
                {
                    item = it.Current.Args[it.Current.Symbol.Arity - 1];
                    if (facts.TermIndex.IsGroundMember(facts.TermIndex.GetCanonicalTerm(data, 0), item))
                    {
                        sortedItems.Add(item);
                    }
                }
            }

            bool wasAdded;
            foreach (var t in sortedItems.Reverse)
            {
                result = facts.TermIndex.MkApply(data, new Term[] { t, result }, out wasAdded);
            }

            return result;
        }

        internal static Term Evaluator_ToNatural(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            return facts.TermIndex.MkNatural(values[0].Binding);
        }

        internal static Term Evaluator_ToString(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            return facts.TermIndex.MkString(values[0].Binding);
        }

        internal static Term Evaluator_ToSymbol(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);

            bool wasAdded;
            UserSymbol us, usp;
            var s = values[0].Binding.Symbol;
            switch (s.Kind)
            {
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    us = (UserSymbol)s;
                    us.Namespace.TryGetSymbol("#" + us.Name, out usp);
                    return facts.TermIndex.MkApply(usp, TermIndex.EmptyArgs, out wasAdded);
                default:
                    return values[0].Binding;
            }
        }

        internal static Term Evaluator_Sign(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            Rational r1;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, out r1))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(new Rational(r1.Sign), out wasAdded);
        }

        internal static Term SymEvaluator_Add(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.Add);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                // If we're a UserCnstSymb variable, then we lookup our Type in the SymExecuter
                Term normalized;
                if (t1.Symbol.Kind == SymbolKind.UserCnstSymb && t1.Symbol.IsVariable)
                {
                    var tTerm = facts.varToTypeMap[t1];
                    Contract.Assert(tTerm != null);
                    facts.Encoder.GetVarEnc(t1, tTerm);
                }
                else
                {
                    facts.Encoder.GetTerm(t1, out normalized);
                }

                if (t2.Symbol.Kind == SymbolKind.UserCnstSymb && t2.Symbol.IsVariable)
                {
                    var tTerm = facts.varToTypeMap[t2];
                    Contract.Assert(tTerm != null);
                    facts.Encoder.GetVarEnc(t2, tTerm);
                }
                else
                {
                    facts.Encoder.GetTerm(t2, out normalized);
                }

                // Encode the Term with Z3
                facts.Encoder.GetTerm(res, out normalized);
                return res;
            }
            else
            {
                Rational r1, r2;
                bool added;
                if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
                {
                    return null;
                }

                return facts.Index.MkCnst(r1 + r2, out added);
            }
        }

        internal static Term Evaluator_Add(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(r1 + r2, out wasAdded);
        }

        internal static Term Evaluator_Sub(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(r1 - r2, out wasAdded);
        }

        internal static Term SymEvaluator_Sub(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.Sub);
                return facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);
            }
            else
            {
                Rational r1, r2;
                bool added;
                if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
                {
                    return null;
                }

                return facts.Index.MkCnst(r1 - r2, out added);
            }
        }

        internal static Term Evaluator_Mul(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
            {
                return null;
            }

            return facts.TermIndex.MkCnst(r1 * r2, out wasAdded);
        }

        internal static Term SymEvaluator_Mul(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.Mul);
                return facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);
            }
            else
            {
                Rational r1, r2;
                bool wasAdded;
                if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2))
                {
                    return null;
                }

                return facts.Index.MkCnst(r1 * r2, out wasAdded);
            }
        }

        internal static Term Evaluator_Div(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || r2.IsZero)
            {
                return null;
            }

            return facts.TermIndex.MkCnst(r1 / r2, out wasAdded);
        }

        internal static Term SymEvaluator_Div(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.Div);
                return facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);
            }
            else
            {
                Rational r1, r2;
                bool wasAdded;
                if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || r2.IsZero)
                {
                    return null;
                }

                return facts.Index.MkCnst(r1 / r2, out wasAdded);
            }
        }

        internal static Term Evaluator_Mod(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || r2.IsZero)
            {
                return null;
            }

            return facts.TermIndex.MkCnst(Rational.Remainder(r1, r2), out wasAdded);
        }

        internal static Term Evaluator_Qtnt(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || r2.IsZero)
            {
                return null;
            }

            return facts.TermIndex.MkCnst(Rational.Quotient(r1, r2), out wasAdded);
        }

        internal static Term Evaluator_GCD(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || !r1.IsInteger || !r2.IsInteger)
            {
                return null;
            }

            return facts.TermIndex.MkCnst(new Rational(BigInteger.GreatestCommonDivisor(r1.Numerator, r2.Numerator), BigInteger.One), out wasAdded);
        }

        internal static Term Evaluator_GCDAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = BigInteger.Zero;
            bool hasInteger = false;
            Symbol symb;
            BaseCnstSymb bsymb;
            Rational r;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    symb = it.Current.Args[it.Current.Symbol.Arity - 1].Symbol;
                    if (symb.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bsymb = (BaseCnstSymb)symb;
                    if (bsymb.CnstKind != CnstKind.Numeric)
                    {
                        continue;
                    }

                    r = (Rational)bsymb.Raw;
                    if (r.IsInteger)
                    {
                        hasInteger = true;
                        acc = BigInteger.GreatestCommonDivisor(acc, r.Numerator);
                    }
                }
            }

            bool wasAdded;
            return hasInteger ? facts.TermIndex.MkCnst(new Rational(acc, BigInteger.One), out wasAdded) : values[0].Binding;
        }

        internal static Term Evaluator_LCM(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Rational r1, r2;
            bool wasAdded;
            if (!ToNumerics(values[0].Binding, values[1].Binding, out r1, out r2) || !r1.IsInteger || !r2.IsInteger)
            {
                return null;
            }

            var val1 = BigInteger.Abs(r1.Numerator);
            var val2 = BigInteger.Abs(r2.Numerator);
            if (val1.IsZero || val2.IsZero)
            {
                return facts.TermIndex.MkCnst(Rational.Zero, out wasAdded);
            }
            else
            {
                var lcm = (val1 / BigInteger.GreatestCommonDivisor(val1, val2)) * val2;
                return facts.TermIndex.MkCnst(new Rational(lcm, BigInteger.One), out wasAdded);
            }
        }

        internal static Term Evaluator_LCMAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = BigInteger.One;
            bool hasInteger = false;
            Symbol symb;
            BaseCnstSymb bsymb;
            Rational r;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    symb = it.Current.Args[it.Current.Symbol.Arity - 1].Symbol;
                    if (symb.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bsymb = (BaseCnstSymb)symb;
                    if (bsymb.CnstKind != CnstKind.Numeric)
                    {
                        continue;
                    }

                    r = (Rational)bsymb.Raw;
                    if (r.IsInteger)
                    {
                        hasInteger = true;
                        var val = BigInteger.Abs(r.Numerator);
                        if (acc.IsZero || val.IsZero)
                        {
                            acc = BigInteger.Zero;
                        }
                        else
                        {
                            acc = (acc / BigInteger.GreatestCommonDivisor(acc, val)) * val;
                        }
                    }
                }
            }

            bool wasAdded;
            return hasInteger ? facts.TermIndex.MkCnst(new Rational(acc, BigInteger.One), out wasAdded) : values[0].Binding;
        }

        internal static Term Evaluator_Le(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp <= 0 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term SymEvaluator_Le(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(RelKind.Le);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                Term normalized;
                facts.PendConstraint((Z3BoolExpr)facts.Encoder.GetTerm(res, out normalized, facts));
                return res;
            }
            else
            {
                var cmp = facts.Index.LexicographicCompare(values[0].Binding, values[1].Binding);
                return cmp <= 0 ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term SymEvaluator_Lt(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(RelKind.Lt);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                Term normalized;
                facts.PendConstraint((Z3BoolExpr)facts.Encoder.GetTerm(res, out normalized, facts));
                return res;
            }
            else
            {
                var cmp = facts.Index.LexicographicCompare(values[0].Binding, values[1].Binding);
                return cmp < 0 ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term Evaluator_Lt(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp < 0 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_Ge(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp >= 0 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term SymEvaluator_Count(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            int nResults;
            bool wasAdded;
            var res = facts.Query(values[0].Binding, out nResults);

            int baseCount = 0; // always 0 for now
            List<Term> realTerms = new List<Term>();
            List<Term> fakeTerms = new List<Term>();

            foreach (var term in res)
            {
                realTerms.Add(term.Args[0]); // it's a comprehension, so extract Args[0]
                fakeTerms.Add(term);
            }

            BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.SymCount);
            Term baseTerm = facts.Index.MkCnst(new Rational(baseCount), out wasAdded);

            int symCount = facts.GetSymbolicCountIndex(realTerms[0]);
            Term symCountTerm = facts.Index.MkCnst(new Rational(symCount), out wasAdded);

            Term[] allRealTerms = new Term[realTerms.Count + 2];
            Term[] allFakeTerms = new Term[fakeTerms.Count + 2];

            allRealTerms[0] = baseTerm; // use the same base count for both
            allFakeTerms[0] = baseTerm;

            allRealTerms[1] = symCountTerm; // use the same symbolic count for both
            allFakeTerms[1] = symCountTerm;

            for (int i = 0; i < realTerms.Count; i++)
            {
                allRealTerms[i + 2] = realTerms.ElementAt(i); // first two indices are reserved
                allFakeTerms[i + 2] = fakeTerms.ElementAt(i);
            }

            Term realSymCount = facts.Index.MkApply(bos, allRealTerms, out wasAdded);
            Term fakeSymCount = facts.Index.MkApply(bos, allFakeTerms, out wasAdded);
            facts.AddSymbolicCountTerm(realTerms[0], fakeSymCount);
            return realSymCount;
        }

        internal static Term SymEvaluator_Ge(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(RelKind.Ge);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                Term normalized;
                facts.PendConstraint((Z3BoolExpr)facts.Encoder.GetTerm(res, out normalized, facts));
                return res;
            }
            else
            {
                var cmp = facts.Index.LexicographicCompare(values[0].Binding, values[1].Binding);
                return cmp >= 0 ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term SymEvaluator_Gt(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(RelKind.Gt);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                Term normalized;
                facts.PendConstraint((Z3BoolExpr)facts.Encoder.GetTerm(res, out normalized, facts));
                return res;
            }
            else
            {
                var cmp = facts.Index.LexicographicCompare(values[0].Binding, values[1].Binding);
                return cmp > 0 ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term Evaluator_Gt(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp > 0 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_Neq(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            return values[0].Binding != values[1].Binding ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term SymEvaluator_Neq(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);

            Term t1 = values[0].Binding;
            Term t2 = values[1].Binding;

            if (Term.IsSymbolicTerm(t1, t2))
            {
                // Create the Term that we will return
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(RelKind.Neq);
                Term res = facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);

                Term normalized;
                facts.PendConstraint((Z3BoolExpr)facts.Encoder.GetTerm(res, out normalized, facts));
                return res;
            }
            else
            {
                return values[0].Binding != values[1].Binding ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term Evaluator_Min(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp <= 0 ? values[0].Binding : values[1].Binding;
        }

        internal static Term Evaluator_Max(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var cmp = facts.TermIndex.LexicographicCompare(values[0].Binding, values[1].Binding);
            return cmp >= 0 ? values[0].Binding : values[1].Binding;
        }

        internal static Term SymEvaluator_Max(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            Term x = values[0].Binding;
            Term y = values[1].Binding;

            if (Term.IsSymbolicTerm(x, y))
            {
                bool wasAdded;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.SymMax);
                return facts.Index.MkApply(bos, new Term[] { x, y }, out wasAdded);
            }
            else
            {
                var cmp = facts.Index.LexicographicCompare(x, y);
                return cmp >= 0 ? x : y;
            }
        }

        internal static Term Evaluator_And(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            bool b1, b2;
            if (!ToBooleans(values[0].Binding, values[1].Binding, out b1, out b2))
            {
                return null;
            }

            return b1 && b2 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term SymEvaluator_And(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            if (Term.IsSymbolicTerm(values[0].Binding, values[1].Binding))
            {
                bool wasAdded;
                Term t1 = values[0].Binding;
                Term t2 = values[1].Binding;
                BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.SymAnd);
                return facts.Index.MkApply(bos, new Term[] { t1, t2 }, out wasAdded);
            }
            else
            {
                bool b1, b2;
                if (!ToBooleans(values[0].Binding, values[1].Binding, out b1, out b2))
                {
                    return null;
                }

                return b1 && b2 ? facts.Index.TrueValue : facts.Index.FalseValue;
            }
        }

        internal static Term Evaluator_AndAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = BigInteger.Zero;
            bool hasBool = false;
            Term t;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    t = it.Current.Args[it.Current.Symbol.Arity - 1];
                    if (t == facts.TermIndex.FalseValue)
                    {
                        return facts.TermIndex.FalseValue;
                    }
                    else if (t == facts.TermIndex.TrueValue)
                    {
                        hasBool = true;
                    }
                }
            }

            return hasBool ? facts.TermIndex.TrueValue : values[0].Binding;
        }

        internal static Term SymEvaluator_AndAll(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = BigInteger.Zero;
            bool hasBool = false;
            Term t;
            bool hasSymbolics = false;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    t = it.Current.Args[it.Current.Symbol.Arity - 1];
                    if (Term.IsSymbolicTerm(t))
                    {
                        hasSymbolics = true;
                        break;
                    }
                    if (t == facts.Index.FalseValue)
                    {
                        return facts.Index.FalseValue;
                    }
                    else if (t == facts.Index.TrueValue)
                    {
                        hasBool = true;
                    }
                }
            }

            if (!hasSymbolics)
            {
                return hasBool ? facts.Index.TrueValue : values[0].Binding;
            }

            BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.SymAndAll);
            Term[] terms = facts.Query(values[1].Binding, out nResults).Select(t => t.Args[t.Symbol.Arity - 1]).ToArray();
            bool wasAdded;
            return facts.Index.MkApply(bos, terms, out wasAdded);
        }

        internal static Term Evaluator_Or(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            bool b1, b2;
            if (!ToBooleans(values[0].Binding, values[1].Binding, out b1, out b2))
            {
                return null;
            }

            return b1 || b2 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_OrAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = BigInteger.Zero;
            bool hasBool = false;
            Term t;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    t = it.Current.Args[it.Current.Symbol.Arity - 1];
                    if (t == facts.TermIndex.TrueValue)
                    {
                        return facts.TermIndex.TrueValue;
                    }
                    else if (t == facts.TermIndex.FalseValue)
                    {
                        hasBool = true;
                    }
                }
            }

            return hasBool ? facts.TermIndex.FalseValue : values[0].Binding;
        }

        internal static Term Evaluator_Impl(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            bool b1, b2;
            if (!ToBooleans(values[0].Binding, values[1].Binding, out b1, out b2))
            {
                return null;
            }

            return (!b1) || b2 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term Evaluator_Not(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            bool b1;
            if (!ToBooleans(values[0].Binding, out b1))
            {
                return null;
            }

            return b1 ? facts.TermIndex.FalseValue : facts.TermIndex.TrueValue;
        }

        internal static Term Evaluator_No(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            int nResults;
            facts.Query(values[0].Binding, out nResults);
            return nResults == 0 ? facts.TermIndex.TrueValue : facts.TermIndex.FalseValue;
        }

        internal static Term SymEvaluator_No(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            int nResults;
            var res = facts.Query(values[0].Binding, out nResults);
            if (nResults == 0)
            {
                return facts.Index.TrueValue;
            }

            bool hasConstraints = false;
            
            foreach (var item in res)
            {
                hasConstraints = (facts.AddNegativeConstraint(item) || hasConstraints);
            }

            if (nResults == 0 || hasConstraints)
            {
                return facts.Index.TrueValue;
            }
            else
            {
                return facts.Index.FalseValue;
            }
        }

        internal static Term Evaluator_Count(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 1);
            int nResults;
            bool wasAdded;
            facts.Query(values[0].Binding, out nResults);
            return facts.TermIndex.MkCnst(new Rational(nResults), out wasAdded);
        }

        internal static Term Evaluator_ToOrdinal(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            int ordinal;
            if (!facts.GetOrdinal(values[2].Binding, values[0].Binding, out ordinal))
            {
                return values[1].Binding;
            }

            bool wasAdded;
            return facts.TermIndex.MkCnst(new Rational(ordinal), out wasAdded);
        }

        internal static Term Evaluator_MaxAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                it.MoveNext();
                var max = it.Current.Args[it.Current.Symbol.Arity - 1];
                while (it.MoveNext())
                {
                    if (facts.TermIndex.LexicographicCompare(it.Current.Args[it.Current.Symbol.Arity - 1], max) > 0)
                    {
                        max = it.Current.Args[it.Current.Symbol.Arity - 1];
                    }
                }

                return max;
            }
        }

        internal static Term Evaluator_MinAll(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                it.MoveNext();
                var min = it.Current.Args[it.Current.Symbol.Arity - 1];
                while (it.MoveNext())
                {
                    if (facts.TermIndex.LexicographicCompare(it.Current.Args[it.Current.Symbol.Arity - 1], min) < 0)
                    {
                        min = it.Current.Args[it.Current.Symbol.Arity - 1];
                    }
                }

                return min;
            }
        }

        internal static Term SymEvaluator_Select(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var target = values[0].Binding;
            if (!target.Symbol.IsDataConstructor)
            {
                return null;
            }

            //// This cast should succeed, because second argument to selector should
            //// always be a string.
            var label = (string)((BaseCnstSymb)values[1].Binding.Symbol).Raw;
            int index;
            bool labelExists;
            switch (target.Symbol.Kind)
            {
                case SymbolKind.ConSymb:
                    labelExists = ((ConSymb)target.Symbol).GetLabelIndex(label, out index);
                    break;
                case SymbolKind.MapSymb:
                    labelExists = ((MapSymb)target.Symbol).GetLabelIndex(label, out index);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return labelExists ? target.Args[index] : null;
        }

        internal static Term Evaluator_Select(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            var target = values[0].Binding;
            if (!target.Symbol.IsDataConstructor)
            {
                return null;
            }

            //// This cast should succeed, because second argument to selector should
            //// always be a string.
            var label = (string)((BaseCnstSymb)values[1].Binding.Symbol).Raw;
            int index;
            bool labelExists;
            switch (target.Symbol.Kind)
            {
                case SymbolKind.ConSymb:
                    labelExists = ((ConSymb)target.Symbol).GetLabelIndex(label, out index);
                    break;
                case SymbolKind.MapSymb:
                    labelExists = ((MapSymb)target.Symbol).GetLabelIndex(label, out index);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return labelExists ? target.Args[index] : null;
        }

        internal static Term SymEvaluator_Sum(SymExecuter facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = Rational.Zero;
            bool hasNumeric = false;
            Symbol symb;
            BaseCnstSymb bsymb;

            IEnumerable<Term> terms = facts.Query(values[1].Binding, out nResults);
            if (nResults == 0)
            {
                return values[0].Binding;
            }


            bool hasVariables = false;
            foreach (var term in terms)
            {
                if (Term.IsSymbolicTerm(term))
                {
                    hasVariables = true;
                    break;
                }
            }

            if (hasVariables)
            {
                Term currExpr = null;
                Term normalized;

                foreach (var term in terms)
                {
                    Term currTerm = term.Args[term.Symbol.Arity - 1];
                    if (currTerm.Symbol.Kind == SymbolKind.UserCnstSymb && currTerm.Symbol.IsVariable)
                    {
                        var typeTerm = facts.varToTypeMap[currTerm];
                        Contract.Assert(typeTerm != null);
                        facts.Encoder.GetVarEnc(currTerm, typeTerm);
                    }
                    else
                    {
                        facts.Encoder.GetTerm(currTerm, out normalized);
                    }

                    if (currExpr == null)
                    {
                        currExpr = currTerm;
                    }
                    else
                    {
                        bool wasAdded;
                        BaseOpSymb bos = facts.Index.SymbolTable.GetOpSymbol(OpKind.Add);
                        currExpr = facts.Index.MkApply(bos, new Term[] { currExpr, currTerm }, out wasAdded);
                    }
                }

                facts.Encoder.GetTerm(currExpr, out normalized);
                return currExpr;
            }
            else
            {
                foreach (var term in terms)
                {
                    symb = term.Args[term.Symbol.Arity - 1].Symbol;
                    if (symb.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bsymb = (BaseCnstSymb)symb;
                    if (bsymb.CnstKind == CnstKind.Numeric)
                    {
                        hasNumeric = true;
                        acc += ((Rational)bsymb.Raw);
                    }
                }

                bool wasAdded;
                return hasNumeric ? facts.Index.MkCnst(acc, out wasAdded) : values[0].Binding;
            }
        }

        internal static Term Evaluator_Sum(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = Rational.Zero;
            bool hasNumeric = false;
            Symbol symb;
            BaseCnstSymb bsymb;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    symb = it.Current.Args[it.Current.Symbol.Arity - 1].Symbol;
                    if (symb.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bsymb = (BaseCnstSymb)symb;
                    if (bsymb.CnstKind == CnstKind.Numeric)
                    {
                        hasNumeric = true;
                        acc += ((Rational)bsymb.Raw);
                    }                    
                }
            }

            bool wasAdded;
            return hasNumeric ? facts.TermIndex.MkCnst(acc, out wasAdded) : values[0].Binding;
        }

        internal static Term Evaluator_Prod(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 2);
            int nResults;
            var acc = Rational.One;
            bool hasNumeric = false;
            Symbol symb;
            BaseCnstSymb bsymb;
            using (var it = facts.Query(values[1].Binding, out nResults).GetEnumerator())
            {
                if (nResults == 0)
                {
                    return values[0].Binding;
                }

                while (it.MoveNext())
                {
                    symb = it.Current.Args[it.Current.Symbol.Arity - 1].Symbol;
                    if (symb.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bsymb = (BaseCnstSymb)symb;
                    if (bsymb.CnstKind == CnstKind.Numeric)
                    {
                        hasNumeric = true;
                        acc *= ((Rational)bsymb.Raw);
                    }
                }
            }

            bool wasAdded;
            return hasNumeric ? facts.TermIndex.MkCnst(acc, out wasAdded) : values[0].Binding;
        }

        internal static Term Evaluator_Relabel(Executer facts, Bindable[] values)
        {
            Contract.Requires(values.Length == 3);
            var from = (string)((BaseCnstSymb)values[0].Binding.Symbol).Raw;
            var to = (string)((BaseCnstSymb)values[1].Binding.Symbol).Raw;
            var index = facts.TermIndex;
            var table = facts.TermIndex.SymbolTable;

            int i;
            UserSymbol us;
            UserCnstSymb ucs;
            bool result, wasAdded;
            return values[2].Binding.Compute<Term>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            return x;
                        case SymbolKind.UserCnstSymb:
                            ucs = (UserCnstSymb)x.Symbol;
                            if (ucs.IsDerivedConstant || ucs.IsTypeConstant)
                            {
                                result = table.Relabel(from, to, ucs.Namespace).TryGetSymbol(ucs.Name, out us);
                                Contract.Assert(result);
                                return index.MkApply(us, TermIndex.EmptyArgs, out wasAdded);
                            }
                            else
                            {
                                return x;
                            }
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            us = (UserSymbol)x.Symbol;
                            result = table.Relabel(from, to, us.Namespace).TryGetSymbol(us.Name, out us);
                            Contract.Assert(result);
                            i = 0;
                            var args = new Term[us.Arity];
                            foreach (var c in ch)
                            {
                                args[i++] = c;
                            }

                            return index.MkApply(us, args, out wasAdded);
                        default:
                            throw new NotImplementedException();
                    }
                });
        }

        internal static IEnumerable<Tuple<RelKind, Term, Term>> AppConstrainer_RflGetArgType(TermIndex index, Term[] args)
        {
            Contract.Requires(index != null);
            Contract.Requires(args != null && args.Length == 2);
            bool wasAdded;
           
            yield return new Tuple<RelKind, Term, Term>(
                RelKind.Lt,
                args[1],
                index.MkApply(index.SymbolTable.GetOpSymbol(OpKind.RflGetArity), new Term[] { args[0] }, out wasAdded));
        }

        internal static IEnumerable<Tuple<RelKind, Term, Term>> AppConstrainer_ToList(TermIndex index, Term[] args)
        {
            Contract.Requires(index != null);
            Contract.Requires(args != null && args.Length == 3);
            bool wasAdded;

            yield return new Tuple<RelKind, Term, Term>(
                RelKind.Eq,
                index.MkApply(
                    index.SymbolTable.GetOpSymbol(OpKind.RflIsMember),
                    new Term[] 
                    {
                        args[1],
                        index.MkApply(
                            index.SymbolTable.GetOpSymbol(OpKind.RflGetArgType),
                            new Term[] { args[0], index.OneValue },
                            out wasAdded)
                    },
                    out wasAdded),
                index.TrueValue);
        }

        internal static IEnumerable<Tuple<RelKind, Term, Term>> AppConstrainer_BinArg2NonZero(TermIndex index, Term[] args)
        {
            Contract.Requires(index != null);
            Contract.Requires(args != null && args.Length == 2);
            bool wasAdded;
            yield return new Tuple<RelKind, Term, Term>(
                RelKind.Neq, 
                args[1], 
                index.MkCnst(Rational.Zero, out wasAdded));
        }

        /// <summary>
        /// If t1 and t2 stand for numerics, then returns the values.
        /// </summary>
        private static bool ToNumerics(Term t1, Term t2, out Rational r1, out Rational r2)
        {
            BaseCnstSymb s1, s2;
            r1 = Rational.Zero;
            r2 = Rational.Zero;

            if (t1.Symbol.Kind != SymbolKind.BaseCnstSymb)
            {
                return false;
            }

            s1 = (BaseCnstSymb)(t1.Symbol);
            if (s1.CnstKind != CnstKind.Numeric)
            {
                return false;
            }

            if (t2.Symbol.Kind != SymbolKind.BaseCnstSymb)
            {
                return false;
            }

            s2 = (BaseCnstSymb)(t2.Symbol);
            if (s2.CnstKind != CnstKind.Numeric)
            {
                return false;
            }

            r1 = (Rational)s1.Raw;
            r2 = (Rational)s2.Raw;
            return true;
        }

        /// <summary>
        /// If t1 stands for a numeric, then returns the value.
        /// </summary>
        private static bool ToNumerics(Term t1, out Rational r1)
        {
            BaseCnstSymb s1;
            r1 = Rational.Zero;

            if (t1.Symbol.Kind != SymbolKind.BaseCnstSymb)
            {
                return false;
            }

            s1 = (BaseCnstSymb)(t1.Symbol);
            if (s1.CnstKind != CnstKind.Numeric)
            {
                return false;
            }

            r1 = (Rational)s1.Raw;
            return true;
        }

        /// <summary>
        /// If t1 stands for a string, then returns the value.
        /// </summary>
        private static bool ToStrings(Term t1, out string str1)
        {
            BaseCnstSymb s1;
            str1 = null;
            if (t1.Symbol.Kind != SymbolKind.BaseCnstSymb)
            {
                return false;
            }

            s1 = (BaseCnstSymb)(t1.Symbol);
            if (s1.CnstKind != CnstKind.String)
            {
                return false;
            }

            str1 = (string)s1.Raw;
            return true;
        }

        /// <summary>
        /// If t1 and t2 stand for booleans, then returns the values.
        /// </summary>
        private static bool ToBooleans(Term t1, Term t2, out bool b1, out bool b2)
        {
            b1 = b2 = false;
            if (t1 == t1.Owner.TrueValue)
            {
                b1 = true;
            }
            else if (t1 != t1.Owner.FalseValue)
            {
                return false;
            }

            if (t2 == t2.Owner.TrueValue)
            {
                b2 = true;
            }
            else if (t2 != t2.Owner.FalseValue)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Converts a type constant to a type term.
        /// </summary>
        private static bool ToType(Term t, out Term type, bool onlyConstructors = false)
        {
            if (t.Symbol.Kind != SymbolKind.UserCnstSymb)
            {
                type = null;
                return false;
            }

            var uc = (UserCnstSymb)t.Symbol;
            if (!uc.IsTypeConstant)
            {
                type = null;
                return false;
            }

            UserSymbol us;
            var ndx = uc.Name.IndexOf('[');
            if (ndx >= 0 && uc.Namespace.TryGetSymbol(uc.Name.Substring(1, ndx - 1), out us))
            {
                if (onlyConstructors)
                {
                    type = null;
                    return false;
                }

                type = t.Owner.GetCanonicalTerm(us, int.Parse(uc.Name.Substring(ndx + 1, uc.Name.Length - ndx - 2)));
            }
            else if (uc.Namespace.TryGetSymbol(uc.Name.Substring(1), out us))
            {
                bool wasAdded;
                switch (us.Kind)
                {
                    case SymbolKind.ConSymb:
                        type = t.Owner.MkApply(((ConSymb)us).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    case SymbolKind.MapSymb:
                        type = t.Owner.MkApply(((MapSymb)us).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    case SymbolKind.UnnSymb:
                        if (onlyConstructors)
                        {
                            type = null;
                            return false;
                        }

                        type = t.Owner.GetCanonicalTerm(us, 0);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                throw new Impossible();
            }

            return true;
        }

        /// <summary>
        /// If t1 stands for a boolean, then returns the value.
        /// </summary>
        private static bool ToBooleans(Term t1, out bool b1)
        {
            b1 = false;
            if (t1 == t1.Owner.TrueValue)
            {
                b1 = true;
            }
            else if (t1 != t1.Owner.FalseValue)
            {
                return false;
            }

            return true;
        }

        private static bool ValidateArity(RelConstr rc, string prettyName, LiftedBool[] comprPattern, List<Flag> flags)
        {
            int nArgs = rc.Arg2 == null ? 1 : 2;
            if (nArgs != comprPattern.Length)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    rc,
                    Constants.BadSyntax.ToString(string.Format("{0} got {1} arguments but needs {2}", prettyName, nArgs, comprPattern.Length)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return false;
            }

            var args = rc.Arg2 == null ? EnumerableMethods.GetEnumerable<Node>(rc.Arg1)
                                       : EnumerableMethods.GetEnumerable<Node>(rc.Arg1, rc.Arg2);
            int i = 0;
            foreach (var a in args)
            {
                if (a.NodeKind == NodeKind.Compr && comprPattern[i] == LiftedBool.False)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        rc,
                        Constants.BadSyntax.ToString(string.Format("comprehension not allowed in argument {1} of {0}", prettyName, i + 1)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    return false;
                }
                else if (a.NodeKind != NodeKind.Compr && comprPattern[i] == LiftedBool.True)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        rc,
                        Constants.BadSyntax.ToString(string.Format("comprehension required in argument {1} of {0}", prettyName, i + 1)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    return false;
                }

                ++i;
            }

            return true;
        }

        private static bool ValidateArity(FuncTerm ft, string prettyName, LiftedBool[] comprPattern, List<Flag> flags)
        {
            if (ft.Args.Count != comprPattern.Length)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.BadSyntax.ToString(string.Format("{0} got {1} arguments but needs {2}", prettyName, ft.Args.Count, comprPattern.Length)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return false;
            }

            int i = 0;
            foreach (var a in ft.Args)
            {
                if (a.NodeKind == NodeKind.Compr && comprPattern[i] == LiftedBool.False)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        ft,
                        Constants.BadSyntax.ToString(string.Format("comprehension not allowed in argument {1} of {0}", prettyName, i + 1)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    return false;
                }
                else if (a.NodeKind != NodeKind.Compr && comprPattern[i] == LiftedBool.True)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        ft,
                        Constants.BadSyntax.ToString(string.Format("comprehension required in argument {1} of {0}", prettyName, i + 1)),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    return false;
                }

                ++i;
            }

            return true;
        }

        #endregion

        #region Galois Approximations
        public static Func<TermIndex, Term[], Term[]> TypeApprox_And_Up
        {
            get { return AndUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_And_Down
        {
            get { return AndDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Or_Up
        {
            get { return OrUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Or_Down
        {
            get { return OrDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Impl_Up
        {
            get { return ImplUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Impl_Down
        {
            get { return ImplDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Not_Up
        {
            get { return NotApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Not_Down
        {
            get { return NotApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Neg_Up
        {
            get { return NegApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Neg_Down
        {
            get { return NegApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Eq_Up
        {
            get { return EqUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Eq_Down
        {
            get { return EqDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_NEq_Up
        {
            get { return NEqUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_NEq_Down
        {
            get { return NEqDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Add_Up
        {
            get { return AddUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Add_Down
        {
            get { return AddDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sign_Up
        {
            get { return SignUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sign_Down
        {
            get { return SignDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sub_Up
        {
            get { return SubUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sub_Down
        {
            get { return SubDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sum_Up
        {
            get { return SumUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sum_Down
        {
            get { return SumDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_IsSubstring_Up
        {
            get { return IsSubstringUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_IsSubstring_Down
        {
            get { return IsSubstringDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrAfter_Up
        {
            get { return StrAfterUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrAfter_Down
        {
            get { return StrAfterDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrBefore_Up
        {
            get { return StrBeforeUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrBefore_Down
        {
            get { return StrBeforeDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrFind_Up
        {
            get { return StrFindUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrFind_Down
        {
            get { return StrFindDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrGetAt_Up
        {
            get { return StrGetAtUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrGetAt_Down
        {
            get { return StrGetAtDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrJoin_Up
        {
            get { return StrJoinUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrJoin_Down
        {
            get { return StrJoinDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrReplace_Up
        {
            get { return StrReplaceUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrReplace_Down
        {
            get { return StrReplaceDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrLength_Up
        {
            get { return StrLengthUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrLength_Down
        {
            get { return StrLengthDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrLower_Up
        {
            get { return StrLowerUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrLower_Down
        {
            get { return StrLowerDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrUpper_Up
        {
            get { return StrUpperUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToList_Down
        {
            get { return ToListDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToList_Up
        {
            get { return ToListUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToString_Down
        {
            get { return ToStringDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToString_Up
        {
            get { return ToStringUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToNatural_Down
        {
            get { return ToNaturalDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToNatural_Up
        {
            get { return ToNaturalUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToSymbol_Down
        {
            get { return ToSymbolDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToSymbol_Up
        {
            get { return ToSymbolUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrUpper_Down
        {
            get { return StrUpperDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrReverse_Up
        {
            get { return StrReverseUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_StrReverse_Down
        {
            get { return StrReverseDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_AndAll_Up
        {
            get { return AndAllUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_AndAll_Down
        {
            get { return AndAllDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_OrAll_Up
        {
            get { return OrAllUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_OrAll_Down
        {
            get { return OrAllDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Prod_Up
        {
            get { return ProdUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Prod_Down
        {
            get { return ProdDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToOrdinal_Up
        {
            get { return ToOrdinalUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_ToOrdinal_Down
        {
            get { return ToOrdinalDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Mul_Up
        {
            get { return MulUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Mul_Down
        {
            get { return MulDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Div_Up
        {
            get { return DivUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Div_Down
        {
            get { return DivDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Mod_Up
        {
            get { return ModUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Mod_Down
        {
            get { return ModDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Qtnt_Up
        {
            get { return QtntUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Qtnt_Down
        {
            get { return QtntDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_GCD_Up
        {
            get { return GCDUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_GCD_Down
        {
            get { return GCDDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_GCDAll_Up
        {
            get { return GCDAllUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_GCDAll_Down
        {
            get { return GCDAllDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LCM_Up
        {
            get { return LCMUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LCM_Down
        {
            get { return LCMDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LCMAll_Up
        {
            get { return LCMAllUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LCMAll_Down
        {
            get { return LCMAllDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Lt_Up
        {
            get { return LtUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Lt_Down
        {
            get { return LtDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Le_Up
        {
            get { return LeUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Le_Down
        {
            get { return LeDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Gt_Up
        {
            get { return GtUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Gt_Down
        {
            get { return GtDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Ge_Up
        {
            get { return GeUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Ge_Down
        {
            get { return GeDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstLength_Up
        {
            get { return LstLengthUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstLength_Down
        {
            get { return LstLengthDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstReverse_Up
        {
            get { return LstReverseUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstReverse_Down
        {
            get { return LstReverseDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFind_Up
        {
            get { return LstFindUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFind_Down
        {
            get { return LstFindDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFindAll_Up
        {
            get { return LstFindAllUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFindAll_Down
        {
            get { return LstFindAllDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFindAllNot_Up
        {
            get { return LstFindAllNotUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstFindAllNot_Down
        {
            get { return LstFindAllNotDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstGetAt_Up
        {
            get { return LstGetAtUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_LstGetAt_Down
        {
            get { return LstGetAtDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflIsMember_Up
        {
            get { return RflIsMemberUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflIsMember_Down
        {
            get { return RflIsMemberDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflIsSubtype_Up
        {
            get { return RflIsSubtypeUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflIsSubtype_Down
        {
            get { return RflIsSubtypeDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflGetArgType_Up
        {
            get { return RflGetArgTypeUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflGetArgType_Down
        {
            get { return RflGetArgTypeDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflGetArity_Up
        {
            get { return RflGetArityUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_RflGetArity_Down
        {
            get { return RflGetArityDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sel_Up
        {
            get { return SelUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Sel_Down
        {
            get { return SelDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Count_Up
        {
            get { return CountUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Count_Down
        {
            get { return CountDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_No_Up
        {
            get { return NoUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_No_Down
        {
            get { return NoDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Min_Up
        {
            get { return MinUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Min_Down
        {
            get { return MinDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Max_Up
        {
            get { return MaxUpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Max_Down
        {
            get { return MaxDownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Unconstrained2_Down
        {
            get { return Unconstrained2DownwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_Unconstrained2_Up
        {
            get { return Unconstrained2UpwardApprox.Instance.Approximate; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_NotImplemented_Down
        {
            get { return NotImplementedApprox; }
        }

        public static Func<TermIndex, Term[], Term[]> TypeApprox_NotImplemented_Up
        {
            get { return NotImplementedApprox; }
        }

        private static Term[] NotImplementedApprox(TermIndex index, Term[] args)
        {
            throw new NotImplementedException();
        }

        private class GaloisApproxTable
        {
            private Func<TermIndex, Term[], Term[]>[] approximations;
            public GaloisApproxTable(params Func<TermIndex, Term[], Term[]>[] approximations)
            {
                Contract.Requires(approximations != null && approximations.Length > 0);
                this.approximations = approximations;
            }

            public Term[] Approximate(TermIndex index, Term[] args)
            {
                Term[] result;
                foreach (var approx in approximations)
                {
                    if ((result = approx(index, args)) != null)
                    {
                        return result;
                    }
                }

                return null;
            }

            protected static Term MkBaseSort(TermIndex index, BaseSortKind sort)
            {
                bool wasAdded;
                return index.MkApply(index.SymbolTable.GetSortSymbol(sort), TermIndex.EmptyArgs, out wasAdded);
            }

            protected static void GetBoolTerms(TermIndex index, out Term _bool, out Term _true, out Term _false)
            {
                bool wasAdded;
                UserSymbol other;
                _true = index.MkApply(
                    index.SymbolTable.Resolve(ASTSchema.Instance.ConstNameTrue, out other),
                    TermIndex.EmptyArgs,
                    out wasAdded);

                _false = index.MkApply(
                    index.SymbolTable.Resolve(ASTSchema.Instance.ConstNameFalse, out other),
                    TermIndex.EmptyArgs,
                    out wasAdded);

                _bool = index.MkApply(
                    index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                    new Term[] { _true, _false },
                    out wasAdded);
            }

            /// <summary>
            /// For every type constant #T, adds the type T to the set
            /// </summary>
            protected static bool GetTypeConstantTypes(
                                                Term type,
                                                TermIndex index,
                                                out Set<Term> elements,
                                                bool onlyConstructors = false)
            {
                Contract.Requires(type != null && index != null);
                int ndx;
                bool wasAdded;
                UserCnstSymb uc;
                UserSymbol us;
                var lclElements = new Set<Term>(Term.Compare);              
                type.Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol.Kind != SymbolKind.UserCnstSymb)
                        {
                            return;
                        }

                        uc = (UserCnstSymb)x.Symbol;
                        if (!uc.IsTypeConstant)
                        {
                            return;
                        }

                        ndx = uc.Name.IndexOf('[');
                        if (ndx >= 0 && uc.Namespace.TryGetSymbol(uc.Name.Substring(1, ndx - 1), out us))
                        {
                            if (!onlyConstructors)
                            {
                                lclElements.Add(index.GetCanonicalTerm(us, int.Parse(uc.Name.Substring(ndx + 1, uc.Name.Length - ndx - 2))));
                            }
                        }
                        else if (uc.Namespace.TryGetSymbol(uc.Name.Substring(1), out us))
                        {
                            switch (us.Kind)
                            {
                                case SymbolKind.ConSymb:
                                    lclElements.Add(index.MkApply(((ConSymb)us).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                                    break;
                                case SymbolKind.MapSymb:
                                    lclElements.Add(index.MkApply(((MapSymb)us).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                                    break;
                                case SymbolKind.UnnSymb:
                                    if (!onlyConstructors)
                                    {
                                        lclElements.Add(index.GetCanonicalTerm(us, 0));
                                    }
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                        else
                        {
                            throw new Impossible();
                        }                                                                      
                    });

                elements = lclElements;
                return elements.Count > 0;
            }

            /// <summary>
            /// Returns a possibly widened set of string elements
            /// Widened if more than RangeWideningWidth distinguished numbers.
            /// </summary>
            protected static bool GetStringElements(
                                                Term type,
                                                TermIndex index,
                                                out Set<Term> elements)
            {
                Contract.Requires(type != null && index != null);

                BaseCnstSymb bc;
                BaseSortSymb bs;
                bool hasAllStrings = false;
                var lclElements = new Set<Term>(Term.Compare);
                type.Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (!hasAllStrings && x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            bc = (BaseCnstSymb)x.Symbol;
                            if (bc.CnstKind == CnstKind.String)
                            {
                                lclElements.Add(x);
                            }
                        }
                        else if (!hasAllStrings && x.Symbol.Kind == SymbolKind.BaseSortSymb)
                        {
                            bs = (BaseSortSymb)x.Symbol;
                            if (bs.SortKind == BaseSortKind.String)
                            {
                                hasAllStrings = true;
                                lclElements.Clear();
                                lclElements.Add(x);
                            }
                        }
                    });

                elements = lclElements;
                return elements.Count > 0;
            }

            /// <summary>
            /// Returns a possibly widened set of numerical elements
            /// contained by type. If widened, the set of elements will always have less than
            /// RangeWideningWidth distinguished numbers.
            /// </summary>
            protected static bool GetNumericalElements(
                                                Term type,
                                                TermIndex index,
                                                out Set<Term> elements,
                                                out Term _zero,
                                                out Term _one)
            {
                Contract.Requires(type != null && index != null);

                bool wasAdded;
                elements = new Set<Term>(Term.Compare);
                _zero = index.MkCnst(Rational.Zero, out wasAdded);
                _one = index.MkCnst(Rational.One, out wasAdded);

                //// Step 1. Add to elements distinguished constants.
                //// Also find the largest and smallest distinguished constants.
                //// Never add more than RangeWideningWidth - 1 constants. 
                var appUnnWide = new AppFreeCanUnn(type);
                bool isIntegral = true;
                Rational upper, lower;
                BigInteger nDistConsts = BigInteger.Zero, size, i;
                Rational min = Rational.Zero, max = Rational.Zero;
               
                foreach (var e in appUnnWide.RangeMembers)
                {
                    lower = new Rational(e.Key, BigInteger.One);
                    upper = new Rational(e.Value, BigInteger.One);
                    size = e.Value - e.Key + BigInteger.One;
                    if (nDistConsts.IsZero)
                    {
                        min = lower;
                        max = upper;
                    }
                    else
                    {
                        min = lower < min ? lower : min;
                        max = upper > max ? upper : max;
                    }

                    nDistConsts += size;
                    if (nDistConsts >= NumWideningWidth)
                    {
                        continue;
                    }

                    i = e.Key;
                    while (i <= e.Value)
                    {
                        elements.Add(index.MkCnst(new Rational(i, BigInteger.One), out wasAdded));
                        ++i;
                    }
                }

                BaseCnstSymb bs;
                foreach (var e in appUnnWide.NonRangeMembers)
                {
                    if (e.Kind != SymbolKind.BaseCnstSymb)
                    {
                        continue;
                    }

                    bs = (BaseCnstSymb)e;
                    if (bs.CnstKind != CnstKind.Numeric)
                    {
                        continue;
                    }

                    lower = (Rational)bs.Raw;
                    Contract.Assert(!lower.IsInteger);

                    isIntegral = false;
                    if (nDistConsts.IsZero)
                    {
                        min = lower;
                        max = lower;
                    }
                    else
                    {
                        min = lower < min ? lower : min;
                        max = lower > max ? lower : max;
                    }

                    nDistConsts++;
                    if (nDistConsts >= NumWideningWidth)
                    {
                        continue;
                    }

                    elements.Add(index.MkCnst(lower, out wasAdded));
                }

                //// Step 2. Perform widening if needed.
                if (nDistConsts >= NumWideningWidth)
                {
                    elements.Clear();
                    if (isIntegral)
                    {
                        if (min.Sign < 0 && max.Sign < 0)
                        {
                            elements.Add(MkBaseSort(index, BaseSortKind.NegInteger));
                        }
                        else if (min.Sign < 0 && max.Sign == 0)
                        {
                            elements.Add(MkBaseSort(index, BaseSortKind.NegInteger));
                            elements.Add(_zero);
                        }
                        else if (min.Sign < 0 && max.Sign > 0)
                        {
                            elements.Add(MkBaseSort(index, BaseSortKind.Integer));
                        }
                        else if (min.Sign == 0 && max.Sign > 0)
                        {
                            elements.Add(MkBaseSort(index, BaseSortKind.Natural));
                        }
                        else if (min.Sign > 0 && max.Sign > 0)
                        {
                            elements.Add(MkBaseSort(index, BaseSortKind.PosInteger));
                        }
                        else
                        {
                            throw new Impossible();
                        }
                    }
                    else
                    {
                        elements.Add(MkBaseSort(index, BaseSortKind.Real));
                    }
                }

                //// Step 3. Handle base sorts
                if (appUnnWide.Contains(index.SymbolTable.GetSortSymbol(BaseSortKind.Real)))
                {
                    elements.Add(MkBaseSort(index, BaseSortKind.Real));
                }
                else if (appUnnWide.Contains(index.SymbolTable.GetSortSymbol(BaseSortKind.Integer)))
                {
                    elements.Add(MkBaseSort(index, BaseSortKind.Integer));
                }
                else 
                {
                    if (appUnnWide.Contains(index.SymbolTable.GetSortSymbol(BaseSortKind.Natural)))
                    {
                        elements.Add(MkBaseSort(index, BaseSortKind.Natural));
                    }
                    else if (appUnnWide.Contains(index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger)))
                    {
                        elements.Add(MkBaseSort(index, BaseSortKind.PosInteger));
                    }

                    if (appUnnWide.Contains(index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger)))
                    {
                        elements.Add(MkBaseSort(index, BaseSortKind.NegInteger));
                    }
                }

                return elements.Count > 0;
            }
        }

        private class AndUpwardApprox : GaloisApproxTable
        {
            private static readonly AndUpwardApprox theInstance = new AndUpwardApprox();
            public static AndUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private AndUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0, tintr1;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                result = index.MkIntersection(_bool, args[1], out tintr1);
                Contract.Assert(result);

                if (tintr0.Groundness == Groundness.Type &&
                    tintr1.Groundness == Groundness.Type)
                {
                    return new Term[] { _bool };
                }
                else if (tintr0.Groundness == Groundness.Type)
                {
                    return tintr1 == _false ? new Term[] { _false } : new Term[] { _bool };
                }
                else if (tintr1.Groundness == Groundness.Type)
                {
                    return tintr0 == _false ? new Term[] { _false } : new Term[] { _bool };
                }
                else
                {
                    return tintr0 == _false || tintr1 == _false ? new Term[] { _false } : new Term[] { _true }; 
                }
            }
        }

        private class AndDownwardApprox : GaloisApproxTable
        {
            private static readonly AndDownwardApprox theInstance = new AndDownwardApprox();
            public static AndDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private AndDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                return tintr0 == _true ? new Term[] { _true, _true } : new Term[] { _bool, _bool };
            }
        }

        private class OrUpwardApprox : GaloisApproxTable
        {
            private static readonly OrUpwardApprox theInstance = new OrUpwardApprox();
            public static OrUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private OrUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0, tintr1;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                result = index.MkIntersection(_bool, args[1], out tintr1);
                Contract.Assert(result);

                if (tintr0.Groundness == Groundness.Type &&
                    tintr1.Groundness == Groundness.Type)
                {
                    return new Term[] { _bool };
                }
                else if (tintr0.Groundness == Groundness.Type)
                {
                    return tintr1 == _true ? new Term[] { _true } : new Term[] { _bool };
                }
                else if (tintr1.Groundness == Groundness.Type)
                {
                    return tintr0 == _true ? new Term[] { _true } : new Term[] { _bool };
                }
                else
                {
                    return tintr0 == _true || tintr1 == _true ? new Term[] { _true } : new Term[] { _false };
                }
            }
        }

        private class OrDownwardApprox : GaloisApproxTable
        {
            private static readonly OrDownwardApprox theInstance = new OrDownwardApprox();
            public static OrDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private OrDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                return tintr0 == _false ? new Term[] { _false, _false } : new Term[] { _bool, _bool };
            }
        }

        private class ImplUpwardApprox : GaloisApproxTable
        {
            private static readonly ImplUpwardApprox theInstance = new ImplUpwardApprox();
            public static ImplUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ImplUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0, tintr1;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                result = index.MkIntersection(_bool, args[1], out tintr1);
                Contract.Assert(result);

                if (tintr0.Groundness == Groundness.Type &&
                    tintr1.Groundness == Groundness.Type)
                {
                    return new Term[] { _bool };
                }
                else if (tintr0.Groundness == Groundness.Type)
                {
                    return tintr1 == _true ? new Term[] { _true } : new Term[] { _bool };
                }
                else if (tintr1.Groundness == Groundness.Type)
                {
                    return tintr0 == _false ? new Term[] { _true } : new Term[] { _bool };
                }
                else
                {
                    return tintr0 == _true && tintr1 == _false ? new Term[] { _false } : new Term[] { _true };
                }
            }
        }

        private class ImplDownwardApprox : GaloisApproxTable
        {
            private static readonly ImplDownwardApprox theInstance = new ImplDownwardApprox();
            public static ImplDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ImplDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);
                return tintr0 == _false ? new Term[] { _true, _false } : new Term[] { _bool, _bool };
            }
        }

        private class NotApprox : GaloisApproxTable
        {
            private static readonly NotApprox theInstance = new NotApprox();
            public static NotApprox Instance
            {
                get { return theInstance; }
            }

            private NotApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);

                Term tintr0;
                var result = index.MkIntersection(_bool, args[0], out tintr0);
                Contract.Assert(result);

                if (tintr0 == _true)
                {
                    return new Term[] { _false };
                }
                else if (tintr0 == _false)
                {
                    return new Term[] { _true };
                }
                else
                {
                    return new Term[] { _bool };
                }
            }
        }

        private class EqUpwardApprox : GaloisApproxTable
        {
            private static readonly EqUpwardApprox theInstance = new EqUpwardApprox();
            public static EqUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private EqUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);
                if (args[0].Groundness == Groundness.Ground &&
                    args[1].Groundness == Groundness.Ground)
                {
                    return new Term[] { args[0] == args[1] ? _true : _false };
                }

                Term tintr;
                if (!index.MkIntersection(args[0], args[1], out tintr))
                {
                    return new Term[] { _false };
                }
                else 
                {
                    return new Term[] { _bool };
                }
            }
        }

        private class EqDownwardApprox : GaloisApproxTable
        {
            private static readonly EqDownwardApprox theInstance = new EqDownwardApprox();
            public static EqDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private EqDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class NEqUpwardApprox : GaloisApproxTable
        {
            private static readonly NEqUpwardApprox theInstance = new NEqUpwardApprox();
            public static NEqUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private NEqUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _bool, _true, _false;
                GetBoolTerms(index, out _bool, out _true, out _false);
                if (args[0].Groundness == Groundness.Ground &&
                    args[1].Groundness == Groundness.Ground)
                {
                    return new Term[] { args[0] != args[1] ? _true : _false };
                }

                Term tintr;
                if (!index.MkIntersection(args[0], args[1], out tintr))
                {
                    return new Term[] { _true };
                }
                else
                {
                    return new Term[] { _bool };
                }
            }
        }

        private class NEqDownwardApprox : GaloisApproxTable
        {
            private static readonly NEqDownwardApprox theInstance = new NEqDownwardApprox();
            public static NEqDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private NEqDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        /// <summary>
        /// Approximates a binary arithmetic operator by widening the arguments
        /// into two unions { e1, ..., en } and { f1, ..., fm }. Then the unions are
        /// expanded into point-wise approximations Approx(e_i, f_j) and union
        /// into an overall approx.
        /// </summary>
        private abstract class BinArithUpApprox : GaloisApproxTable
        {
            protected BinArithUpApprox(Func<TermIndex, Term[], Term[]> expandApprox)
                : base(expandApprox)
            {
            }

            protected static Term[] ExpandApprox(
                TermIndex index, 
                Term[] args,
                Func<TermIndex, Term, Term, Term, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Term _zero, _one;
                Set<Term> cmps1, cmps2;
                if (!GetNumericalElements(args[0], index, out cmps1, out _zero, out _one))
                {
                    return null;
                }

                if (!GetNumericalElements(args[1], index, out cmps2, out _zero, out _one))
                {
                    return null;
                }

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e1 in cmps1)
                {
                    foreach (var e2 in cmps2)
                    {
                        if (approx == null)
                        {
                            approx = pointApprox(index, e1, e2, _zero, _one);
                        }
                        else
                        {
                            approx = index.MkApply(
                                index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                new Term[] { pointApprox(index, e1, e2, _zero, _one), approx },
                                out wasAdded);
                        }
                    }
                }

                return new Term[] { approx };
            }
        }

        private class AddDownwardApprox : GaloisApproxTable
        {
            private static readonly AddDownwardApprox theInstance = new AddDownwardApprox();
            public static AddDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private AddDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class AddUpwardApprox : BinArithUpApprox
        {
            private static readonly AddUpwardApprox theInstance = new AddUpwardApprox();
            public static AddUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private AddUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(
                                    TermIndex index, 
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                BaseSortKind b1 = BaseSortKind.Real;
                BaseSortKind b2 = BaseSortKind.Real;
                if (c1.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b1 = ((BaseSortSymb)c1.Symbol).SortKind;
                }

                if (c2.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b2 = ((BaseSortSymb)c2.Symbol).SortKind;
                }

                //// Step 1. If c1 is ground, then handle this special case.
                if (c1.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    if (c2.Groundness == Groundness.Ground)
                    {
                        return index.MkCnst(val1 + (Rational)((BaseCnstSymb)c2.Symbol).Raw, out wasAdded);
                    }

                    switch (b2)
                    {
                        case BaseSortKind.Real:
                            return c2; //// Real
                        case BaseSortKind.Integer:
                            if (val1.IsInteger)
                            {
                                return c2; //// Integer
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.Natural:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign == 0)
                                {
                                    return c2; //// Natural
                                }
                                else if (val1.Sign > 0)
                                {
                                    return MkBaseSort(index, BaseSortKind.PosInteger);
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.PosInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign >= 0)
                                {
                                    return c2; //// PosInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.NegInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign <= 0)
                                {
                                    return c2; //// NegInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }
                else if (c2.Groundness == Groundness.Ground)
                {
                    return PointApprox(index, c2, c1, _zero, _one);
                }

                //// Step 2. Handle the remaining 25 cases
                if (b1 == BaseSortKind.Real || b2 == BaseSortKind.Real)
                {
                    return MkBaseSort(index, BaseSortKind.Real);
                }
                else if (b1 == BaseSortKind.Integer || b2 == BaseSortKind.Integer)
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
                else if (b1 == b2)
                {
                    return c1;
                }
                else if ((b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.Natural) ||
                    (b1 == BaseSortKind.Natural && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.PosInteger);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class SubDownwardApprox : GaloisApproxTable
        {
            private static readonly SubDownwardApprox theInstance = new SubDownwardApprox();
            public static SubDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private SubDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class SubUpwardApprox : BinArithUpApprox
        {
            private static readonly SubUpwardApprox theInstance = new SubUpwardApprox();
            public static SubUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private SubUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one,
                                    bool wasSwapped)
            {
                bool wasAdded;
                BaseSortKind b1 = BaseSortKind.Real;
                BaseSortKind b2 = BaseSortKind.Real;
                if (c1.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b1 = ((BaseSortSymb)c1.Symbol).SortKind;
                }

                if (c2.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b2 = ((BaseSortSymb)c2.Symbol).SortKind;
                }

                //// Step 1. If c1 is ground, then handle this special case.
                if (c1.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    if (c2.Groundness == Groundness.Ground)
                    {
                        Contract.Assert(!wasSwapped);
                        return index.MkCnst(val1 - (Rational)((BaseCnstSymb)c2.Symbol).Raw, out wasAdded);
                    }

                    switch (b2)
                    {
                        case BaseSortKind.Real:
                            return c2; //// Real
                        case BaseSortKind.Integer:
                            if (val1.IsInteger)
                            {
                                return c2; //// Integer
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.Natural:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign == 0)
                                {
                                    if (wasSwapped)
                                    {
                                        return c2; //// Natural
                                    }
                                    else
                                    {
                                        return index.MkApply(
                                                index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                                new Term[] { MkBaseSort(index, BaseSortKind.NegInteger), _zero }, 
                                                out wasAdded);
                                    }
                                }
                                else if (val1.Sign < 0)
                                {
                                    if (wasSwapped)
                                    {
                                        return MkBaseSort(index, BaseSortKind.PosInteger);
                                    }
                                    else
                                    {
                                        return MkBaseSort(index, BaseSortKind.NegInteger);
                                    }
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.PosInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign <= 0)
                                {
                                    if (wasSwapped)
                                    {
                                        return c2; //// PosInteger
                                    }
                                    else
                                    {
                                        return MkBaseSort(index, BaseSortKind.NegInteger);
                                    }
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.NegInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign == 0)
                                {
                                    if (wasSwapped)
                                    {
                                        return MkBaseSort(index, BaseSortKind.PosInteger);
                                    }
                                    else
                                    {
                                        return c2; //// NegInteger
                                    }
                                }
                                else if (val1.Sign > 0)
                                {
                                    if (wasSwapped)
                                    {
                                        return c2; //// NegInteger
                                    }
                                    else
                                    {
                                        return MkBaseSort(index, BaseSortKind.Integer);
                                    }
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.Integer);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }
                else if (c2.Groundness == Groundness.Ground)
                {
                    return PointApprox(index, c2, c1, _zero, _one, true);
                }

                //// Step 2. Handle the remaining 25 cases
                Contract.Assert(!wasSwapped);
                if (b1 == BaseSortKind.Real || b2 == BaseSortKind.Real)
                {
                    return MkBaseSort(index, BaseSortKind.Real);
                }
                else if (b1 == BaseSortKind.Integer || b2 == BaseSortKind.Integer)
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
                else if (b1 == b2)
                {
                    return c1;
                }
                else if ((b1 == BaseSortKind.Natural && b2 == BaseSortKind.NegInteger) ||
                         (b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.NegInteger))
                {
                    return MkBaseSort(index, BaseSortKind.PosInteger);
                }
                else if ((b1 == BaseSortKind.NegInteger && b2 == BaseSortKind.Natural) ||
                         (b1 == BaseSortKind.NegInteger && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.NegInteger);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, (ti, c1, c2, z, o) => PointApprox(ti, c1, c2, z, o, false));
            }
        }

        private class MulDownwardApprox : GaloisApproxTable
        {
            private static readonly MulDownwardApprox theInstance = new MulDownwardApprox();
            public static MulDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private MulDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class MulUpwardApprox : BinArithUpApprox
        {
            private static readonly MulUpwardApprox theInstance = new MulUpwardApprox();
            public static MulUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private MulUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                BaseSortKind b1 = BaseSortKind.Real;
                BaseSortKind b2 = BaseSortKind.Real;
                if (c1.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b1 = ((BaseSortSymb)c1.Symbol).SortKind;
                }

                if (c2.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b2 = ((BaseSortSymb)c2.Symbol).SortKind;
                }

                //// Step 1. If c1 is ground, then handle this special case.
                if (c1.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    if (c2.Groundness == Groundness.Ground)
                    {
                        return index.MkCnst(val1 * (Rational)((BaseCnstSymb)c2.Symbol).Raw, out wasAdded);
                    }
                    else if (val1.Sign == 0)
                    {
                        return _zero;
                    }

                    switch (b2)
                    {
                        case BaseSortKind.Real:
                            return c2; //// Real
                        case BaseSortKind.Integer:
                            if (val1.IsInteger)
                            {
                                return c2; //// Integer
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.Natural:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// Natural
                                }
                                else
                                {
                                    return index.MkApply(
                                        index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                        new Term[] { MkBaseSort(index, BaseSortKind.NegInteger), _zero },
                                        out wasAdded);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.PosInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// PosInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.NegInteger);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.NegInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// NegInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.PosInteger);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }
                else if (c2.Groundness == Groundness.Ground)
                {
                    return PointApprox(index, c2, c1, _zero, _one);
                }

                //// Step 2. Handle the remaining 25 cases
                if (b1 == BaseSortKind.Real || b2 == BaseSortKind.Real)
                {
                    return MkBaseSort(index, BaseSortKind.Real);
                }
                else if (b1 == BaseSortKind.Integer || b2 == BaseSortKind.Integer)
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
                else if (b1 == b2)
                {
                    return b1 == BaseSortKind.Natural ? MkBaseSort(index, BaseSortKind.Natural) : MkBaseSort(index, BaseSortKind.PosInteger);
                }
                else if ((b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.NegInteger) ||
                         (b1 == BaseSortKind.NegInteger && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.NegInteger);
                }
                else if ((b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.Natural) ||
                         (b1 == BaseSortKind.Natural && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
                else
                {
                    return index.MkApply(
                        index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                        new Term[] { MkBaseSort(index, BaseSortKind.NegInteger), _zero },
                        out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class DivDownwardApprox : GaloisApproxTable
        {
            private static readonly DivDownwardApprox theInstance = new DivDownwardApprox();
            public static DivDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private DivDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class DivUpwardApprox : BinArithUpApprox
        {
            private static readonly DivUpwardApprox theInstance = new DivUpwardApprox();
            public static DivUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private DivUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                BaseSortKind b1 = BaseSortKind.Real;
                BaseSortKind b2 = BaseSortKind.Real;
                if (c1.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b1 = ((BaseSortSymb)c1.Symbol).SortKind;
                }

                if (c2.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b2 = ((BaseSortSymb)c2.Symbol).SortKind;
                }

                //// Step 1. If c1 is ground, then handle this special case.
                if (c1.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    if (c2.Groundness == Groundness.Ground)
                    {
                        var val2 = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                        return val2.Sign == 0 ? _zero : index.MkCnst(val1 / val2, out wasAdded);
                    }

                    switch (b2)
                    {
                        case BaseSortKind.Real:
                            return c2; //// Real
                        case BaseSortKind.Integer:
                            if (val1.IsInteger)
                            {
                                return c2; //// Integer
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.Natural:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// Natural
                                }
                                else
                                {
                                    return index.MkApply(
                                        index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                        new Term[] { MkBaseSort(index, BaseSortKind.NegInteger), _zero },
                                        out wasAdded);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.PosInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// PosInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.NegInteger);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        case BaseSortKind.NegInteger:
                            if (val1.IsInteger)
                            {
                                if (val1.Sign > 0)
                                {
                                    return c2; //// NegInteger
                                }
                                else
                                {
                                    return MkBaseSort(index, BaseSortKind.PosInteger);
                                }
                            }
                            else
                            {
                                return MkBaseSort(index, BaseSortKind.Real);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }
                else if (c2.Groundness == Groundness.Ground)
                {
                    return PointApprox(index, c2, c1, _zero, _one);
                }

                //// Step 2. Handle the remaining 25 cases
                if (b1 == BaseSortKind.Real || b2 == BaseSortKind.Real)
                {
                    return MkBaseSort(index, BaseSortKind.Real);
                }
                else if (b1 == BaseSortKind.Integer || b2 == BaseSortKind.Integer)
                {
                    return MkBaseSort(index, BaseSortKind.Integer);
                }
                else if (b1 == b2)
                {
                    return b1 == BaseSortKind.Natural ? MkBaseSort(index, BaseSortKind.Natural) : MkBaseSort(index, BaseSortKind.PosInteger);
                }
                else if ((b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.NegInteger) ||
                         (b1 == BaseSortKind.NegInteger && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.NegInteger);
                }
                else if ((b1 == BaseSortKind.PosInteger && b2 == BaseSortKind.Natural) ||
                         (b1 == BaseSortKind.Natural && b2 == BaseSortKind.PosInteger))
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
                else
                {
                    return index.MkApply(
                        index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                        new Term[] { MkBaseSort(index, BaseSortKind.NegInteger), _zero },
                        out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class ModDownwardApprox : GaloisApproxTable
        {
            private static readonly ModDownwardApprox theInstance = new ModDownwardApprox();
            public static ModDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ModDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class ModUpwardApprox : BinArithUpApprox
        {
            private static readonly ModUpwardApprox theInstance = new ModUpwardApprox();
            public static ModUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ModUpwardApprox()
                : base(Approx)
            { }

            /// <summary>
            /// The remainder is defined so that it is always non-negative.
            /// </summary>
            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                BaseSortKind b1 = BaseSortKind.Real;
                BaseSortKind b2 = BaseSortKind.Real;
                if (c1.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b1 = ((BaseSortSymb)c1.Symbol).SortKind;
                }

                if (c2.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    b2 = ((BaseSortSymb)c2.Symbol).SortKind;
                }

                if (c1.Groundness == Groundness.Ground &&
                    c2.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    var val2 = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    return index.MkCnst(Rational.Remainder(val1, val2), out wasAdded);
                }
                else if (c1.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    if (val1.IsZero)
                    {
                        return _zero;
                    }
                    else if (val1.IsInteger && b2 != BaseSortKind.Real)
                    {
                        return MkBaseSort(index, BaseSortKind.Natural);
                    }
                    else
                    {
                        return MkBaseSort(index, BaseSortKind.Real);
                    }
                }
                else if (c2.Groundness == Groundness.Ground)
                {
                    var val2 = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    if (b1 == BaseSortKind.Real || !val2.IsInteger)
                    {
                        return MkBaseSort(index, BaseSortKind.Real);
                    }
                    else if (val2.IsOne)
                    {
                        return _zero;
                    }
                    else
                    {
                        return MkBaseSort(index, BaseSortKind.Natural);
                    }
                }
                else if (b1 == BaseSortKind.Real || b2 == BaseSortKind.Real)
                {
                    return MkBaseSort(index, BaseSortKind.Real);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class QtntDownwardApprox : GaloisApproxTable
        {
            private static readonly QtntDownwardApprox theInstance = new QtntDownwardApprox();
            public static QtntDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private QtntDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Real), 
                    MkBaseSort(index, BaseSortKind.Real) 
                };
            }
        }

        private class QtntUpwardApprox : BinArithUpApprox
        {
            private static readonly QtntUpwardApprox theInstance = new QtntUpwardApprox();
            private static readonly int[,] approxMatrix = new int[4, 4];

            static QtntUpwardApprox()
            {
                approxMatrix[0, 0] = 1;
                approxMatrix[0, 1] = 0;
                approxMatrix[0, 2] = 0;
                approxMatrix[0, 3] = 3;

                approxMatrix[1, 0] = 4;
                approxMatrix[1, 1] = 2;
                approxMatrix[1, 2] = 2;
                approxMatrix[1, 3] = 3;

                approxMatrix[2, 0] = 4;
                approxMatrix[2, 1] = 2;
                approxMatrix[2, 2] = 2;
                approxMatrix[2, 3] = 3;

                approxMatrix[3, 0] = 3;
                approxMatrix[3, 1] = 3;
                approxMatrix[3, 2] = 3;
                approxMatrix[3, 3] = 3;
            }

            public static QtntUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private QtntUpwardApprox()
                : base(Approx)
            { }

            /// <summary>
            /// The quotient is defined so that it is always an integer.
            /// </summary>
            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                if (c1.Groundness == Groundness.Ground &&
                    c2.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    var val2 = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    return index.MkCnst(Rational.Quotient(val1, val2), out wasAdded);
                }
                else if (c1 == _zero || c2 == _zero)
                {
                    return _zero;
                }

                //// 0 for Neg
                //// 1 for Pos
                //// 2 for Nat
                //// 3 for Int/Real
                //// 4 for {0} + Neg

                int kind1, kind2;
                if (c1.Groundness == Groundness.Ground)
                {
                    kind1 = ((Rational)((BaseCnstSymb)c1.Symbol).Raw).Sign == -1 ? 0 : 1;
                }
                else
                {
                    var bs = (BaseSortSymb)c1.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.NegInteger:
                            kind1 = 0;
                            break;
                        case BaseSortKind.PosInteger:
                            kind1 = 1;
                            break;
                        case BaseSortKind.Natural:
                            kind1 = 2;
                            break;
                        case BaseSortKind.Integer:
                        case BaseSortKind.Real:
                            kind1 = 3;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }

                if (c2.Groundness == Groundness.Ground)
                {
                    kind2 = ((Rational)((BaseCnstSymb)c2.Symbol).Raw).Sign == -1 ? 0 : 1;
                }
                else
                {
                    var bs = (BaseSortSymb)c2.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.NegInteger:
                            kind2 = 0;
                            break;
                        case BaseSortKind.PosInteger:
                            kind2 = 1;
                            break;
                        case BaseSortKind.Natural:
                            kind2 = 2;
                            break;
                        case BaseSortKind.Integer:
                        case BaseSortKind.Real:
                            kind2 = 3;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }

                var approx = approxMatrix[kind1, kind2];
                switch (approx)
                {
                    case 0:
                        return MkBaseSort(index, BaseSortKind.NegInteger);
                    case 1:
                        return MkBaseSort(index, BaseSortKind.PosInteger);
                    case 2:
                        return MkBaseSort(index, BaseSortKind.Natural);
                    case 3:
                        return MkBaseSort(index, BaseSortKind.Integer);
                    case 4:
                        return index.MkApply(
                                   index.TypeUnionSymbol,
                                   new Term[] { _zero, MkBaseSort(index, BaseSortKind.NegInteger) },
                                   out wasAdded);
                    default:
                        throw new NotImplementedException();
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class GCDDownwardApprox : GaloisApproxTable
        {
            private static readonly GCDDownwardApprox theInstance = new GCDDownwardApprox();
            public static GCDDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private GCDDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Integer), 
                    MkBaseSort(index, BaseSortKind.Integer) 
                };
            }
        }

        private class GCDUpwardApprox : BinArithUpApprox
        {
            private static readonly GCDUpwardApprox theInstance = new GCDUpwardApprox();

            public static GCDUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private GCDUpwardApprox()
                : base(Approx)
            { }

            /// <summary>
            /// The quotient is defined so that it is always an integer.
            /// </summary>
            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                if (c1.Groundness == Groundness.Ground &&
                    c2.Groundness == Groundness.Ground)
                {
                    var val1 = (Rational)((BaseCnstSymb)c1.Symbol).Raw;
                    var val2 = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    return index.MkCnst(new Rational(BigInteger.GreatestCommonDivisor(val1.Numerator, val2.Numerator), BigInteger.One), out wasAdded);
                }
                else if (c1 == _one || c2 == _one)
                {
                    return _one;
                }

                bool canBeZero = c1 == _zero || c2 == _zero;
                if (!canBeZero && c1.Groundness == Groundness.Type)
                {
                    var bs = (BaseSortSymb)c1.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.Real:
                        case BaseSortKind.Integer:
                        case BaseSortKind.Natural:
                            canBeZero = true;
                            break;
                        default:
                            break;

                    }
                }

                if (!canBeZero && c1.Groundness == Groundness.Type)
                {
                    var bs = (BaseSortSymb)c2.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.Real:
                        case BaseSortKind.Integer:
                        case BaseSortKind.Natural:
                            canBeZero = true;
                            break;
                        default:
                            break;

                    }
                }

                if (canBeZero)
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.PosInteger);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class LCMDownwardApprox : GaloisApproxTable
        {
            private static readonly LCMDownwardApprox theInstance = new LCMDownwardApprox();
            public static LCMDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LCMDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    MkBaseSort(index, BaseSortKind.Integer), 
                    MkBaseSort(index, BaseSortKind.Integer) 
                };
            }
        }

        private class LCMUpwardApprox : BinArithUpApprox
        {
            private static readonly LCMUpwardApprox theInstance = new LCMUpwardApprox();

            public static LCMUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LCMUpwardApprox()
                : base(Approx)
            { }

            /// <summary>
            /// The quotient is defined so that it is always an integer.
            /// </summary>
            private static Term PointApprox(
                                    TermIndex index,
                                    Term c1,
                                    Term c2,
                                    Term _zero,
                                    Term _one)
            {
                bool wasAdded;
                if (c1.Groundness == Groundness.Ground &&
                    c2.Groundness == Groundness.Ground)
                {
                    var val1 = BigInteger.Abs(((Rational)((BaseCnstSymb)c1.Symbol).Raw).Numerator);
                    var val2 = BigInteger.Abs(((Rational)((BaseCnstSymb)c2.Symbol).Raw).Numerator);
                    if (val1.IsZero || val2.IsZero)
                    {
                        return _zero;
                    }
                    else
                    {
                        var lcm = (val1 / BigInteger.GreatestCommonDivisor(val1, val2)) * val2;
                        return index.MkCnst(new Rational(lcm, BigInteger.One), out wasAdded);
                    }
                }

                bool canBeZero = c1 == _zero || c2 == _zero;
                if (!canBeZero && c1.Groundness == Groundness.Type)
                {
                    var bs = (BaseSortSymb)c1.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.Real:
                        case BaseSortKind.Integer:
                        case BaseSortKind.Natural:
                            canBeZero = true;
                            break;
                        default:
                            break;

                    }
                }

                if (!canBeZero && c1.Groundness == Groundness.Type)
                {
                    var bs = (BaseSortSymb)c2.Symbol;
                    switch (bs.SortKind)
                    {
                        case BaseSortKind.Real:
                        case BaseSortKind.Integer:
                        case BaseSortKind.Natural:
                            canBeZero = true;
                            break;
                        default:
                            break;

                    }
                }

                if (canBeZero)
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.PosInteger);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class NegApprox : GaloisApproxTable
        {
            private static readonly NegApprox theInstance = new NegApprox();
            public static NegApprox Instance
            {
                get { return theInstance; }
            }

            private NegApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term type = null;
                BaseCnstSymb bc;
                BaseSortSymb bs;
                Rational r;
                Term t, tp;
                bool wasAdded;
                args[0].Compute<Unit>(
                    (x, s) =>
                    {
                        if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            bc = (BaseCnstSymb)x.Symbol;
                            if (bc.CnstKind == CnstKind.Numeric)
                            {
                                r = (Rational)bc.Raw;
                                r = new Rational(BigInteger.Negate(r.Numerator), r.Denominator);
                                t = index.MkCnst(r, out wasAdded);
                                type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                            }
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                        {
                            bs = (BaseSortSymb)x.Symbol;
                            switch (bs.SortKind)
                            {
                                case BaseSortKind.Integer:
                                case BaseSortKind.Real:
                                    t = x;
                                    break;
                                case BaseSortKind.Natural:
                                    t = index.MkApply(
                                        index.TypeUnionSymbol,
                                        new Term[] { index.MkCnst(Rational.Zero, out wasAdded), MkBaseSort(index, BaseSortKind.NegInteger) },
                                        out wasAdded);
                                    break;
                                case BaseSortKind.PosInteger:
                                    t = MkBaseSort(index, BaseSortKind.NegInteger);
                                    break;
                                case BaseSortKind.NegInteger:
                                    t = MkBaseSort(index, BaseSortKind.PosInteger);
                                    break;
                                case BaseSortKind.String:
                                    t = null;
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            if (t != null)
                            {
                                type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                            }
                        }
                        else if (x.Symbol == index.RangeSymbol)
                        {
                            r = (Rational)((BaseCnstSymb)x.Args[1].Symbol).Raw;
                            r = new Rational(BigInteger.Negate(r.Numerator), r.Denominator);
                            t = index.MkCnst(r, out wasAdded);

                            r = (Rational)((BaseCnstSymb)x.Args[0].Symbol).Raw;
                            r = new Rational(BigInteger.Negate(r.Numerator), r.Denominator);
                            tp = index.MkCnst(r, out wasAdded);

                            t = index.MkApply(index.RangeSymbol, new Term[] { t, tp }, out wasAdded);
                            type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                        }
                        else if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return x.Args;
                        }

                        return null;
                    },
                    (x, ch, s) =>
                    {
                        return default(Unit);
                    },
                    null);

                if (type == null)
                {
                    return null;
                }
                else
                {
                    return new Term[] { (new AppFreeCanUnn(type)).MkTypeTerm(index) };
                }
            }
        }

        private class LtDownwardApprox : GaloisApproxTable
        {
            private static readonly LtDownwardApprox theInstance = new LtDownwardApprox();
            public static LtDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LtDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType
                };
            }
        }

        /// <summary>
        /// Given x : Tx &lt; y : Ty, then need to know if
        /// \exists n \in Tx, m \in Ty. x &lt; y and
        /// \exists n \in Tx, m \in Ty. x &gt;= y.
        /// This can be decided in |Tx| + |Ty| time by the following lemmas:
        /// \exists n \in Tx, m \in Ty. x &lt; y Iff min(Tx) &lt; max(Ty)
        /// \exists n \in Tx, m \in Ty. x &gt;= y Iff max(Tx) &gt;= min(Ty).
        /// </summary>
        private class LtUpwardApprox : GaloisApproxTable
        {
            private static readonly LtUpwardApprox theInstance = new LtUpwardApprox();
            public static LtUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LtUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                if ((index.LexicographicMinMaxCompare(args[0], args[1]) >= 0) == true)
                {
                    return new Term[] { index.FalseValue };
                }
                else if ((index.LexicographicMinMaxCompare(args[1], args[0]) > 0) == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
            }
        }

        private class LeDownwardApprox : GaloisApproxTable
        {
            private static readonly LeDownwardApprox theInstance = new LeDownwardApprox();
            public static LeDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LeDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType 
                };
            }
        }

        /// <summary>
        /// See the summary for LtUpwardApprox
        /// </summary>
        private class LeUpwardApprox : GaloisApproxTable
        {
            private static readonly LeUpwardApprox theInstance = new LeUpwardApprox();
            public static LeUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LeUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                if ((index.LexicographicMinMaxCompare(args[0], args[1]) > 0) == true)
                {
                    return new Term[] { index.FalseValue };
                }
                else if ((index.LexicographicMinMaxCompare(args[1], args[0]) >= 0) == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
            }
        }

        private class GtDownwardApprox : GaloisApproxTable
        {
            private static readonly GtDownwardApprox theInstance = new GtDownwardApprox();
            public static GtDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private GtDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType
                };
            }
        }

        /// <summary>
        /// See the summary for LtUpwardApprox
        /// </summary>
        private class GtUpwardApprox : GaloisApproxTable
        {
            private static readonly GtUpwardApprox theInstance = new GtUpwardApprox();
            public static GtUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private GtUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                if ((index.LexicographicMinMaxCompare(args[1], args[0]) >= 0) == true)
                {
                    return new Term[] { index.FalseValue };
                }
                else if ((index.LexicographicMinMaxCompare(args[0], args[1]) > 0) == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
            }
        }

        private class GeDownwardApprox : GaloisApproxTable
        {
            private static readonly GeDownwardApprox theInstance = new GeDownwardApprox();
            public static GeDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private GeDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType
                };
            }
        }

        /// <summary>
        /// See the summary for GeUpwardApprox
        /// </summary>
        private class GeUpwardApprox : GaloisApproxTable
        {
            private static readonly GeUpwardApprox theInstance = new GeUpwardApprox();
            public static GeUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private GeUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                if ((index.LexicographicMinMaxCompare(args[1], args[0]) > 0) == true)
                {
                    return new Term[] { index.FalseValue };
                }
                else if ((index.LexicographicMinMaxCompare(args[0], args[1]) >= 0) == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
            }
        }

        private class MaxDownwardApprox : GaloisApproxTable
        {
            private static readonly MaxDownwardApprox theInstance = new MaxDownwardApprox();
            public static MaxDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private MaxDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType
                };
            }
        }

        /// <summary>
        /// See the summary for GeUpwardApprox
        /// </summary>
        private class MaxUpwardApprox : GaloisApproxTable
        {
            private static readonly MaxUpwardApprox theInstance = new MaxUpwardApprox();
            public static MaxUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private MaxUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                var wide0 = index.MkDataWidenedType(args[0]);
                var wide1 = index.MkDataWidenedType(args[1]);

                if (wide0 == wide1)
                {
                    return new Term[] { wide0 };
                }
                else if ((index.LexicographicMinMaxCompare(wide0, wide1) >= 0) == true)
                {
                    return new Term[] { wide0 };
                }
                else if ((index.LexicographicMinMaxCompare(wide1, wide0) >= 0) == true)
                {
                    return new Term[] { wide1 };
                }
                else
                {
                    return new Term[] { new AppFreeCanUnn(new Term[] { wide0, wide1 }).MkTypeTerm(index) };
                }
            }
        }

        private class MinDownwardApprox : GaloisApproxTable
        {
            private static readonly MinDownwardApprox theInstance = new MinDownwardApprox();
            public static MinDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private MinDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] 
                { 
                    index.CanonicalAnyType,
                    index.CanonicalAnyType
                };
            }
        }

        /// <summary>
        /// See the summary for GeUpwardApprox
        /// </summary>
        private class MinUpwardApprox : GaloisApproxTable
        {
            private static readonly MinUpwardApprox theInstance = new MinUpwardApprox();
            public static MinUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private MinUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                var wide0 = index.MkDataWidenedType(args[0]);
                var wide1 = index.MkDataWidenedType(args[1]);

                if (wide0 == wide1)
                {
                    return new Term[] { wide0 };
                }
                else if ((index.LexicographicMinMaxCompare(wide0, wide1) >= 0) == true)
                {
                    return new Term[] { wide1 };
                }
                else if ((index.LexicographicMinMaxCompare(wide1, wide0) >= 0) == true)
                {
                    return new Term[] { wide0 };
                }
                else
                {
                    return new Term[] { new AppFreeCanUnn(new Term[] { wide0, wide1 }).MkTypeTerm(index) };
                }
            }
        }
        
        /// <summary>
        /// The selection operator is really a family of unary operators of the form
        /// ._{label}(x). However, it is encoded as a binary term .(x, label) where
        /// label is always a string constant. 
        /// 
        /// The upward approximation needs to know the label, which is provided as
        /// the second type argument
        /// </summary>
        private class SelUpwardApprox : GaloisApproxTable
        {
            private static readonly SelUpwardApprox theInstance = new SelUpwardApprox();
            public static SelUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private SelUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                var label = (string)((BaseCnstSymb)args[1].Symbol).Raw;
                Term acc = null;
                args[0].Visit(
                    (x) => x.Symbol.IsTypeUnn ? x.Args : null,
                    (x) => acc = ProjectOnLabel(index, x, label, acc));
                return acc == null ? null : new Term[] { acc, args[1] };                
            }

            private static Term ProjectOnLabel(TermIndex index, Term t, string label, Term acc)
            {
                int i;
                switch (t.Symbol.Kind)
                {
                    case SymbolKind.ConSymb:
                        {
                            var cs = (ConSymb)t.Symbol;
                            return cs.GetLabelIndex(label, out i) ? Accumulate(index, t.Args[i], acc) : acc;
                        }
                    case SymbolKind.MapSymb:
                        {
                            var ms = (MapSymb)t.Symbol;
                            return ms.GetLabelIndex(label, out i) ? Accumulate(index, t.Args[i], acc) : acc;
                        }
                    case SymbolKind.UserSortSymb:
                        {
                            var us = (MapSymb)t.Symbol;
                            if (us.SortSymbol.Kind == SymbolKind.ConSymb)
                            {
                                var cs = (ConSymb)t.Symbol;
                                return cs.GetLabelIndex(label, out i) ? Accumulate(index, index.GetCanonicalTerm(cs, i), acc) : acc;
                            }
                            else if (us.SortSymbol.Kind == SymbolKind.MapSymb)
                            {
                                var ms = (MapSymb)t.Symbol;
                                return ms.GetLabelIndex(label, out i) ? Accumulate(index, index.GetCanonicalTerm(ms, i), acc) : acc;
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                        }
                    default:
                        return acc;
                }                
            }

            private static Term Accumulate(TermIndex index, Term t, Term acc)
            {
                if (acc == null)
                {
                    return t;
                }

                bool wasAdded;
                return index.MkApply(index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                    new Term[] { t, acc },
                                    out wasAdded);
            }
        }

        /// <summary>
        /// The selection operator is really a family of unary operators of the form
        /// ._{label}(x). However, it is encoded as a binary term .(x, label) where
        /// label is always a string constant. 
        /// 
        /// The downward approximation needs to know the label, which is provided as
        /// the second type argument. If .(x, "label") : \tau, then the downward approx receives:
        /// [ \tau, "label" ]
        /// </summary>
        private class SelDownwardApprox : GaloisApproxTable
        {
            private static readonly SelDownwardApprox theInstance = new SelDownwardApprox();
            public static SelDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private SelDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                var label = (string)((BaseCnstSymb)args[1].Symbol).Raw;
                Set<UserSortSymb> symbols;
                index.SymbolTable.InverseLabelLookup(label, out symbols);
                Contract.Assert(symbols != null);

                int i;
                bool wasAdded;
                Term tintr, tapprox = null;
                Term[] narrowArgs;
                UserSymbol dataSymb;
                foreach (var s in symbols)
                {
                    dataSymb = s.DataSymbol;
                    if (dataSymb.Kind == SymbolKind.ConSymb)
                    {
                        ((ConSymb)dataSymb).GetLabelIndex(label, out i);
                    }
                    else if (dataSymb.Kind == SymbolKind.MapSymb)
                    {
                        ((MapSymb)dataSymb).GetLabelIndex(label, out i);
                    }
                    else 
                    {
                        throw new NotImplementedException();
                    }

                    if (index.MkIntersection(index.GetCanonicalTerm(dataSymb, i), args[0], out tintr))
                    {
                        narrowArgs = new Term[dataSymb.Arity];
                        for (int j = 0; j < dataSymb.Arity; ++j)
                        {
                            narrowArgs[j] = j != i ? index.GetCanonicalTerm(dataSymb, j) : tintr;
                        }

                        tapprox = tapprox == null
                            ? index.MkApply(dataSymb, narrowArgs, out wasAdded)
                            : index.MkApply(index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                            new Term[] { index.MkApply(dataSymb, narrowArgs, out wasAdded), tapprox },
                                            out wasAdded);
                    }
                }

                return tapprox == null ? null : new Term[] { tapprox, args[1] };
            }
        }

        private class CountDownwardApprox : GaloisApproxTable
        {
            private static readonly CountDownwardApprox theInstance = new CountDownwardApprox();
            public static CountDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private CountDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType };
            }
        }

        private class CountUpwardApprox : GaloisApproxTable
        {
            private static readonly CountUpwardApprox theInstance = new CountUpwardApprox();
            public static CountUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private CountUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.Natural) };
            }
        }

        private class NoDownwardApprox : GaloisApproxTable
        {
            private static readonly NoDownwardApprox theInstance = new NoDownwardApprox();
            public static NoDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private NoDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType };
            }
        }

        private class NoUpwardApprox : GaloisApproxTable
        {
            private static readonly NoUpwardApprox theInstance = new NoUpwardApprox();
            public static NoUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private NoUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term _true, _false, _bool;
                GetBoolTerms(index, out _bool, out _true, out _false);
                return new Term[] { _true };
            }
        }

        private class Unconstrained2DownwardApprox : GaloisApproxTable
        {
            private static readonly Unconstrained2DownwardApprox theInstance = new Unconstrained2DownwardApprox();
            public static Unconstrained2DownwardApprox Instance
            {
                get { return theInstance; }
            }

            private Unconstrained2DownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class Unconstrained2UpwardApprox : GaloisApproxTable
        {
            private static readonly Unconstrained2UpwardApprox theInstance = new Unconstrained2UpwardApprox();
            public static Unconstrained2UpwardApprox Instance
            {
                get { return theInstance; }
            }

            private Unconstrained2UpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                return new Term[] { index.CanonicalAnyType };
            }
        }

        private class SumDownwardApprox : GaloisApproxTable
        {
            private static readonly SumDownwardApprox theInstance = new SumDownwardApprox();
            public static SumDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private SumDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);                                               
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class SumUpwardApprox : GaloisApproxTable
        {
            private static readonly SumUpwardApprox theInstance = new SumUpwardApprox();
            public static SumUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private SumUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), MkBaseSort(index, BaseSortKind.Real) },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class ProdDownwardApprox : GaloisApproxTable
        {
            private static readonly ProdDownwardApprox theInstance = new ProdDownwardApprox();
            public static ProdDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ProdDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class ProdUpwardApprox : GaloisApproxTable
        {
            private static readonly ProdUpwardApprox theInstance = new ProdUpwardApprox();
            public static ProdUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ProdUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), MkBaseSort(index, BaseSortKind.Real) },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class ToOrdinalDownwardApprox : GaloisApproxTable
        {
            private static readonly ToOrdinalDownwardApprox theInstance = new ToOrdinalDownwardApprox();
            public static ToOrdinalDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToOrdinalDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class ToOrdinalUpwardApprox : GaloisApproxTable
        {
            private static readonly ToOrdinalUpwardApprox theInstance = new ToOrdinalUpwardApprox();
            public static ToOrdinalUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToOrdinalUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[1]), MkBaseSort(index, BaseSortKind.Natural) },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class GCDAllDownwardApprox : GaloisApproxTable
        {
            private static readonly GCDAllDownwardApprox theInstance = new GCDAllDownwardApprox();
            public static GCDAllDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private GCDAllDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class GCDAllUpwardApprox : GaloisApproxTable
        {
            private static readonly GCDAllUpwardApprox theInstance = new GCDAllUpwardApprox();
            public static GCDAllUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private GCDAllUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), MkBaseSort(index, BaseSortKind.Natural) },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class LCMAllDownwardApprox : GaloisApproxTable
        {
            private static readonly LCMAllDownwardApprox theInstance = new LCMAllDownwardApprox();
            public static LCMAllDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LCMAllDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class LCMAllUpwardApprox : GaloisApproxTable
        {
            private static readonly LCMAllUpwardApprox theInstance = new LCMAllUpwardApprox();
            public static LCMAllUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LCMAllUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), MkBaseSort(index, BaseSortKind.Natural) },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class AndAllDownwardApprox : GaloisApproxTable
        {
            private static readonly AndAllDownwardApprox theInstance = new AndAllDownwardApprox();
            public static AndAllDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private AndAllDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class AndAllUpwardApprox : GaloisApproxTable
        {
            private static readonly AndAllUpwardApprox theInstance = new AndAllUpwardApprox();
            public static AndAllUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private AndAllUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), index.CanonicalBooleanType },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class OrAllDownwardApprox : GaloisApproxTable
        {
            private static readonly OrAllDownwardApprox theInstance = new OrAllDownwardApprox();
            public static OrAllDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private OrAllDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class OrAllUpwardApprox : GaloisApproxTable
        {
            private static readonly OrAllUpwardApprox theInstance = new OrAllUpwardApprox();
            public static OrAllUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private OrAllUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                bool wasAdded;
                var widened = index.MkApply(
                    index.TypeUnionSymbol,
                    new Term[] { index.MkDataWidenedType(args[0]), index.CanonicalBooleanType },
                    out wasAdded);

                return new Term[] { widened };
            }
        }

        private class SignDownwardApprox : GaloisApproxTable
        {
            private static readonly SignDownwardApprox theInstance = new SignDownwardApprox();
            public static SignDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private SignDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                bool wasAdded;
                var tRng = index.MkApply(
                    index.RangeSymbol,
                    new Term[] { index.MkCnst(new Rational(-1), out wasAdded), index.MkCnst(Rational.One, out wasAdded) },
                    out wasAdded);

                Term tIntr;
                if (!index.MkIntersection(args[0], tRng, out tIntr))
                {
                    return null;
                }
                else if (tIntr == index.MkCnst(Rational.Zero, out wasAdded))
                {
                    return new Term[] { index.MkCnst(Rational.Zero, out wasAdded) };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.Real) };
                }
            }
        }

        private class SignUpwardApprox : GaloisApproxTable
        {
            private static readonly SignUpwardApprox theInstance = new SignUpwardApprox();
            public static SignUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private SignUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Term type = null;
                BaseCnstSymb bc;
                BaseSortSymb bs;
                Rational r, rp;
                Term t;
                bool wasAdded;
                args[0].Compute<Unit>(
                    (x, s) =>
                    {
                        if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            bc = (BaseCnstSymb)x.Symbol;
                            if (bc.CnstKind == CnstKind.Numeric)
                            {
                                t = index.MkCnst(new Rational(((Rational)bc.Raw).Sign), out wasAdded);
                                type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                            }
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                        {
                            bs = (BaseSortSymb)x.Symbol;
                            switch (bs.SortKind)
                            {
                                case BaseSortKind.Integer:
                                case BaseSortKind.Real:
                                    t = index.MkApply(
                                        index.RangeSymbol, 
                                        new Term[] { index.MkCnst(new Rational(-1), out wasAdded), index.MkCnst(Rational.One, out wasAdded) },
                                        out wasAdded);
                                    break;
                                case BaseSortKind.Natural:
                                    t = index.MkApply(
                                        index.RangeSymbol, 
                                        new Term[] { index.MkCnst(Rational.Zero, out wasAdded), index.MkCnst(Rational.One, out wasAdded) },
                                        out wasAdded);
                                    break;
                                case BaseSortKind.PosInteger:
                                    t = index.MkCnst(Rational.One, out wasAdded);
                                    break;
                                case BaseSortKind.NegInteger:
                                    t = index.MkCnst(new Rational(-1), out wasAdded);
                                    break;
                                case BaseSortKind.String:
                                    t = null;
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            if (t != null)
                            {
                                type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                            }
                        }
                        else if (x.Symbol == index.RangeSymbol)
                        {
                            r = (Rational)((BaseCnstSymb)x.Args[0].Symbol).Raw;
                            rp = (Rational)((BaseCnstSymb)x.Args[1].Symbol).Raw;
                            t = index.MkApply(
                                index.RangeSymbol,
                                new Term[] { index.MkCnst(new Rational(r.Sign), out wasAdded), index.MkCnst(new Rational(rp.Sign), out wasAdded) },
                                out wasAdded);
                            type = type == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, type }, out wasAdded);
                        }
                        else if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return x.Args;
                        }

                        return null;
                    },
                    (x, ch, s) =>
                    {
                        return default(Unit);
                    },
                    null);

                if (type == null)
                {
                    return null;
                }
                else
                {
                    return new Term[] { (new AppFreeCanUnn(type)).MkTypeTerm(index) };
                }
            }
        }

        private abstract class UnStringUpApprox : GaloisApproxTable
        {
            protected UnStringUpApprox(Func<TermIndex, Term[], Term[]> expandApprox)
                : base(expandApprox)
            {
            }

            protected static Term[] ExpandApprox(
                TermIndex index,
                Term[] args,
                Func<TermIndex, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                Set<Term> cmps;
                if (!GetStringElements(args[0], index, out cmps))
                {
                    return null;
                }

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e in cmps)
                {
                    if (approx == null)
                    {
                        approx = pointApprox(index, e);
                    }
                    else
                    {
                        approx = index.MkApply(
                            index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                            new Term[] { pointApprox(index, e), approx },
                            out wasAdded);
                    }
                }

                return new Term[] { approx };
            }
        }

        private class StrLengthDownwardApprox : GaloisApproxTable
        {
            private static readonly StrLengthDownwardApprox theInstance = new StrLengthDownwardApprox();
            public static StrLengthDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrLengthDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.ZeroValue)
                {
                    return new Term[] { index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String) };
                }
            }
        }

        private class StrLengthUpwardApprox : UnStringUpApprox
        {
            private static readonly StrLengthUpwardApprox theInstance = new StrLengthUpwardApprox();
            public static StrLengthUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrLengthUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c)
            {
                bool wasAdded;
                if (c.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    return MkBaseSort(index, BaseSortKind.Natural);
                }
                else
                {
                    return index.MkCnst(new Rational(((string)((BaseCnstSymb)c.Symbol).Raw).Length), out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrLowerDownwardApprox : GaloisApproxTable
        {
            private static readonly StrLowerDownwardApprox theInstance = new StrLowerDownwardApprox();
            public static StrLowerDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrLowerDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.EmptyStringValue)
                {
                    return new Term[] { index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String) };
                }
            }
        }

        private class StrLowerUpwardApprox : UnStringUpApprox
        {
            private static readonly StrLowerUpwardApprox theInstance = new StrLowerUpwardApprox();
            public static StrLowerUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrLowerUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c)
            {
                bool wasAdded;
                if (c.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
                else
                {
                    return index.MkCnst(((string)((BaseCnstSymb)c.Symbol).Raw).ToLowerInvariant(), out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrUpperDownwardApprox : GaloisApproxTable
        {
            private static readonly StrUpperDownwardApprox theInstance = new StrUpperDownwardApprox();
            public static StrUpperDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrUpperDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.EmptyStringValue)
                {
                    return new Term[] { index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String) };
                }
            }
        }

        private class StrUpperUpwardApprox : UnStringUpApprox
        {
            private static readonly StrUpperUpwardApprox theInstance = new StrUpperUpwardApprox();
            public static StrUpperUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrUpperUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c)
            {
                bool wasAdded;
                if (c.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
                else
                {
                    return index.MkCnst(((string)((BaseCnstSymb)c.Symbol).Raw).ToUpperInvariant(), out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrReverseDownwardApprox : GaloisApproxTable
        {
            private static readonly StrReverseDownwardApprox theInstance = new StrReverseDownwardApprox();
            public static StrReverseDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrReverseDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.EmptyStringValue)
                {
                    return new Term[] { index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String) };
                }
            }
        }

        private class StrReverseUpwardApprox : UnStringUpApprox
        {
            private static readonly StrReverseUpwardApprox theInstance = new StrReverseUpwardApprox();
            public static StrReverseUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrReverseUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c)
            {
                bool wasAdded;
                if (c.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
                else
                {
                    var str = (string)((BaseCnstSymb)c.Symbol).Raw;
                    var rev = new StringBuilder(str.Length);
                    for (int i = str.Length - 1; i >= 0; --i)
                    {
                        rev.Append(str[i]);
                    }

                    return index.MkCnst(rev.ToString(), out wasAdded);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private abstract class BinStringUpApprox : GaloisApproxTable
        {
            protected BinStringUpApprox(Func<TermIndex, Term[], Term[]> expandApprox)
                : base(expandApprox)
            {
            }

            protected static Term[] ExpandApprox(
                TermIndex index,
                Term[] args,
                Func<TermIndex, Term, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> cmps1, cmps2;
                if (!GetStringElements(args[0], index, out cmps1))
                {
                    return null;
                }

                if (!GetStringElements(args[1], index, out cmps2))
                {
                    return null;
                }

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e1 in cmps1)
                {
                    foreach (var e2 in cmps2)
                    {
                        if (approx == null)
                        {
                            approx = pointApprox(index, e1, e2);
                        }
                        else
                        {
                            approx = index.MkApply(
                                index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                new Term[] { pointApprox(index, e1, e2), approx },
                                out wasAdded);
                        }
                    }
                }

                return new Term[] { approx };
            }
        }

        private class StrJoinDownwardApprox : GaloisApproxTable
        {
            private static readonly StrJoinDownwardApprox theInstance = new StrJoinDownwardApprox();
            public static StrJoinDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrJoinDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.EmptyStringValue)
                {
                    return new Term[] { index.EmptyStringValue, index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.String) };
                }
            }
        }

        private class StrJoinUpwardApprox : BinStringUpApprox
        {
            private static readonly StrJoinUpwardApprox theInstance = new StrJoinUpwardApprox();
            public static StrJoinUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrJoinUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2)
            {
                if (c1 == index.EmptyStringValue)
                {
                    return c2;
                }
                else if (c2 == index.EmptyStringValue)
                {
                    return c1;
                }
                else if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                         c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    bool wasAdded;
                    return index.MkCnst(string.Concat(
                                    (string)((BaseCnstSymb)c1.Symbol).Raw, 
                                    (string)((BaseCnstSymb)c2.Symbol).Raw), 
                                    out wasAdded);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrReplaceDownwardApprox : GaloisApproxTable
        {
            private static readonly StrReplaceDownwardApprox theInstance = new StrReplaceDownwardApprox();
            public static StrReplaceDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrReplaceDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (args[0] == index.EmptyStringValue)
                {
                    return new Term[] { index.EmptyStringValue, index.EmptyStringValue, index.EmptyStringValue };
                }
                else
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String),
                                        MkBaseSort(index, BaseSortKind.String),
                                        MkBaseSort(index, BaseSortKind.String)  };
                }
            }
        }
        private abstract class TerStringUpApprox : GaloisApproxTable
        {
            protected TerStringUpApprox(Func<TermIndex, Term[], Term[]> expandApprox)
                : base(expandApprox)
            {
            }

            protected static Term[] ExpandApprox(
                TermIndex index,
                Term[] args,
                Func<TermIndex, Term, Term, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> cmps1, cmps2, cmps3;
                if (!GetStringElements(args[0], index, out cmps1))
                {
                    return null;
                }

                if (!GetStringElements(args[1], index, out cmps2))
                {
                    return null;
                }
                if (!GetStringElements(args[2], index, out cmps3))
                {
                    return null;
                }

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e1 in cmps1)
                {
                    foreach (var e2 in cmps2)
                    {
                        foreach (var e3 in cmps3)
                        {
                            if (approx == null)
                            {
                                approx = pointApprox(index, e1, e2, e3);
                            }
                            else
                            {
                                approx = index.MkApply(
                                    index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                    new Term[] { pointApprox(index, e1, e2, e3), approx },
                                    out wasAdded);
                            }
                        }
                    }
                }

                return new Term[] { approx };
            }
        }

        private class StrReplaceUpwardApprox : TerStringUpApprox
        {
            private static readonly StrReplaceUpwardApprox theInstance = new StrReplaceUpwardApprox();
            public static StrReplaceUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrReplaceUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2, Term c3)
            {
                if (c1 == index.EmptyStringValue)
                {
                    return c1;
                }
                else if (c2 == index.EmptyStringValue)
                {
                    return c1;
                }
                else if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                         c2.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                         c3.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    bool wasAdded;
                    return index.MkCnst(
                                    ((string)((BaseCnstSymb)c1.Symbol).Raw).Replace(
                                    (string)((BaseCnstSymb)c2.Symbol).Raw,
                                    (string)((BaseCnstSymb)c3.Symbol).Raw),
                                    out wasAdded);
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class IsSubstringDownwardApprox : GaloisApproxTable
        {
            private static readonly IsSubstringDownwardApprox theInstance = new IsSubstringDownwardApprox();
            public static IsSubstringDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private IsSubstringDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.String) };
            }
        }

        private class IsSubstringUpwardApprox : BinStringUpApprox
        {
            private static readonly IsSubstringUpwardApprox theInstance = new IsSubstringUpwardApprox();
            public static IsSubstringUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private IsSubstringUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2)
            {
                if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                    c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    var str1 = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var str2 = (string)((BaseCnstSymb)c2.Symbol).Raw;

                    if (string.IsNullOrEmpty(str1))
                    {
                        return string.IsNullOrEmpty(str2) ? index.TrueValue : index.FalseValue;
                    }
                    else
                    {
                        return str2.Contains(str1) ? index.TrueValue : index.FalseValue;
                    }
                }
                else
                {
                    return index.CanonicalBooleanType;
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrFindDownwardApprox : GaloisApproxTable
        {
            private static readonly StrFindDownwardApprox theInstance = new StrFindDownwardApprox();
            public static StrFindDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrFindDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.String), index.CanonicalAnyType };
            }
        }

        private class StrFindUpwardApprox : GaloisApproxTable
        {
            private static readonly StrFindUpwardApprox theInstance = new StrFindUpwardApprox();
            public static StrFindUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrFindUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2, Term owiseType)
            {
                bool wasAdded;
                if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                    c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    var str1 = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var str2 = (string)((BaseCnstSymb)c2.Symbol).Raw;
                    if (string.IsNullOrEmpty(str1))
                    {
                        return string.IsNullOrEmpty(str2) ? index.ZeroValue : owiseType;
                    }
                    else
                    {
                        var first = str2.IndexOf(str1);
                        return first >= 0 ? index.MkCnst(new Rational(first), out wasAdded) : owiseType;
                    }
                }
                else
                {
                    return index.MkApply(index.TypeUnionSymbol, new Term[] { MkBaseSort(index, BaseSortKind.Natural), owiseType }, out wasAdded);
                }
            }

            protected static Term[] ExpandApprox(
                TermIndex index,
                Term[] args,
                Func<TermIndex, Term, Term, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                Set<Term> cmps1, cmps2;                
                if (!GetStringElements(args[0], index, out cmps1))
                {
                    return null;
                }

                if (!GetStringElements(args[1], index, out cmps2))
                {
                    return null;
                }

                Term owiseType = index.MkDataWidenedType(args[2]);

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e1 in cmps1)
                {
                    foreach (var e2 in cmps2)
                    {
                        if (approx == null)
                        {
                            approx = pointApprox(index, e1, e2, owiseType);
                        }
                        else
                        {
                            approx = index.MkApply(
                                index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                new Term[] { pointApprox(index, e1, e2, owiseType), approx },
                                out wasAdded);
                        }
                    }
                }

                return new Term[] { approx };
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private abstract class BinStrNatUpApprox : GaloisApproxTable
        {
            protected BinStrNatUpApprox(Func<TermIndex, Term[], Term[]> expandApprox)
                : base(expandApprox)
            {
            }

            protected static Term[] ExpandApprox(
                TermIndex index,
                Term[] args,
                Func<TermIndex, Term, Term, Term> pointApprox)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> cmps1, cmps2;
                if (!GetStringElements(args[0], index, out cmps1))
                {
                    return null;
                }

                Term zero, one;
                if (!GetNumericalElements(args[1], index, out cmps2, out zero, out one))
                {
                    return null;
                }

                //// Union the approximations
                bool wasAdded;
                Term approx = null;
                foreach (var e1 in cmps1)
                {
                    foreach (var e2 in cmps2)
                    {
                        if (approx == null)
                        {
                            approx = pointApprox(index, e1, e2);
                        }
                        else
                        {
                            approx = index.MkApply(
                                index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn),
                                new Term[] { pointApprox(index, e1, e2), approx },
                                out wasAdded);
                        }
                    }
                }

                return new Term[] { approx };
            }
        }

        private class StrAfterDownwardApprox : GaloisApproxTable
        {
            private static readonly StrAfterDownwardApprox theInstance = new StrAfterDownwardApprox();
            public static StrAfterDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrAfterDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.Natural) };
            }
        }

        private class StrAfterUpwardApprox : BinStrNatUpApprox
        {
            private static readonly StrAfterUpwardApprox theInstance = new StrAfterUpwardApprox();
            public static StrAfterUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrAfterUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2)
            {
                bool wasAdded;
                if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                    c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    var str = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var ind = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    if (!ind.IsInteger || ind.Sign < 0)
                    {
                        return null;
                    }
                    else if (string.IsNullOrEmpty(str) || ind.Numerator >= str.Length)
                    {
                        return index.EmptyStringValue;
                    }
                    else
                    {
                        return index.MkCnst(str.Substring((int)ind.Numerator), out wasAdded);
                    }
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrBeforeDownwardApprox : GaloisApproxTable
        {
            private static readonly StrBeforeDownwardApprox theInstance = new StrBeforeDownwardApprox();
            public static StrBeforeDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrBeforeDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.Natural) };
            }
        }

        private class StrBeforeUpwardApprox : BinStrNatUpApprox
        {
            private static readonly StrBeforeUpwardApprox theInstance = new StrBeforeUpwardApprox();
            public static StrBeforeUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrBeforeUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2)
            {
                bool wasAdded;
                if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                    c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    var str = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var ind = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    if (!ind.IsInteger || ind.Sign < 0)
                    {
                        return null;
                    }
                    else if (string.IsNullOrEmpty(str) || ind.Numerator.IsZero)
                    {
                        return index.EmptyStringValue;
                    }
                    else
                    {
                        if (ind.Numerator > str.Length)
                        {
                            return c1;
                        }
                        else
                        {
                            return index.MkCnst(str.Substring(0, (int)ind.Numerator), out wasAdded);
                        }
                    }
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class StrGetAtDownwardApprox : GaloisApproxTable
        {
            private static readonly StrGetAtDownwardApprox theInstance = new StrGetAtDownwardApprox();
            public static StrGetAtDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrGetAtDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { MkBaseSort(index, BaseSortKind.String), MkBaseSort(index, BaseSortKind.Natural) };
            }
        }

        private class StrGetAtUpwardApprox : BinStrNatUpApprox
        {
            private static readonly StrGetAtUpwardApprox theInstance = new StrGetAtUpwardApprox();
            public static StrGetAtUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private StrGetAtUpwardApprox()
                : base(Approx)
            { }

            private static Term PointApprox(TermIndex index, Term c1, Term c2)
            {
                bool wasAdded;
                if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                    c2.Symbol.Kind == SymbolKind.BaseCnstSymb)
                {
                    var str = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var ind = (Rational)((BaseCnstSymb)c2.Symbol).Raw;
                    if (!ind.IsInteger || ind.Sign < 0)
                    {
                        return null;
                    }
                    else if (string.IsNullOrEmpty(str) || ind.Numerator > str.Length)
                    {
                        return index.EmptyStringValue;
                    }
                    else
                    {
                        if (ind.Numerator >= str.Length)
                        {
                            return c1;
                        }
                        else
                        {
                            return index.MkCnst(str[(int)ind.Numerator].ToString(), out wasAdded);
                        }
                    }
                }
                else if (c1.Symbol.Kind == SymbolKind.BaseCnstSymb &&
                         ((string)((BaseCnstSymb)c1.Symbol).Raw).Length < NumWideningWidth)
                {
                    var str = (string)((BaseCnstSymb)c1.Symbol).Raw;
                    var ind = (BaseSortSymb)c2.Symbol;
                    if (ind.SortKind == BaseSortKind.NegInteger)
                    {
                        return null;
                    }

                    var approx = index.EmptyStringValue;
                    for (int i = (ind.SortKind == BaseSortKind.PosInteger) ? 1 : 0; i < str.Length; ++i)
                    {
                        approx = index.MkApply(
                            index.TypeUnionSymbol,
                            new Term[] { index.MkCnst(str[i].ToString(), out wasAdded), approx },
                            out wasAdded);
                    }

                    return approx;
                }
                else
                {
                    return MkBaseSort(index, BaseSortKind.String);
                }
            }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                return ExpandApprox(index, args, PointApprox);
            }
        }

        private class RflGetArityDownwardApprox : GaloisApproxTable
        {
            private static readonly RflGetArityDownwardApprox theInstance = new RflGetArityDownwardApprox();
            public static RflGetArityDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflGetArityDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                BaseSortKind largestSort = BaseSortKind.NegInteger;
                IntIntervals natIntervals = new IntIntervals();

                Rational r;
                BaseCnstSymb bc;
                BaseSortSymb bs;
                BigInteger b1, b2;
                args[0].Compute<Unit>(
                    (x, s) =>
                    {
                        if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            bc = (BaseCnstSymb)x.Symbol;
                            if (bc.CnstKind == CnstKind.Numeric)
                            {
                                r = (Rational)bc.Raw;
                                if (r.IsInteger && r.Sign >= 0)
                                {
                                    natIntervals.Add(r.Numerator, r.Numerator);
                                }
                            }
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                        {
                            bs = (BaseSortSymb)x.Symbol;
                            switch (bs.SortKind)
                            {
                                case BaseSortKind.Integer:
                                case BaseSortKind.Real:
                                case BaseSortKind.Natural:
                                    largestSort = BaseSortKind.Natural;
                                    break;
                                case BaseSortKind.PosInteger:
                                    if (largestSort == BaseSortKind.NegInteger)
                                    {
                                        largestSort = BaseSortKind.PosInteger;
                                    }
                                    break;
                                case BaseSortKind.NegInteger:
                                case BaseSortKind.String:
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                        else if (x.Symbol == index.RangeSymbol)
                        {
                            b1 = ((Rational)((BaseCnstSymb)x.Args[0].Symbol).Raw).Numerator;
                            b2 = ((Rational)((BaseCnstSymb)x.Args[1].Symbol).Raw).Numerator;
                            if (b1.Sign >= 0 || b2.Sign >= 0)
                            {
                                natIntervals.Add(BigInteger.Max(b1, BigInteger.Zero), BigInteger.Max(b2, BigInteger.Zero));
                            }
                        }
                        else if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return x.Args;
                        }

                        return null;
                    },
                    (x, ch, s) =>
                    {
                        return default(Unit);
                    },
                    null);

                if (largestSort == BaseSortKind.Natural)
                {
                    return new Term[] { index.TypeConstantsType };
                }

                var arities = index.GetConstructorArities();
                if (largestSort == BaseSortKind.PosInteger)
                {
                    arities.Remove(BigInteger.Zero, BigInteger.Zero);
                }
                else
                {
                    arities.Add(BigInteger.Zero, BigInteger.Zero);
                    arities = IntIntervals.MkIntersection(arities, natIntervals);
                }

                if (arities.Count == 0)
                {
                    return null;
                }

                int b, e;
                Term approx = null;
                bool wasAdded;
                foreach (var kv in arities.CanonicalForm)
                {
                    b = (int)kv.Key;
                    e = (int)kv.Value;
                    for (int i = b; i <= e; ++i)
                    {
                        approx = approx == null
                            ? index.GetTypeConstType(i)
                            : index.MkApply(index.TypeUnionSymbol, new Term[] { index.GetTypeConstType(i), approx }, out wasAdded);
                    }
                }

                return new Term[] { approx };
            }
        }       
      
        private class RflGetArityUpwardApprox : GaloisApproxTable
        {
            private static readonly RflGetArityUpwardApprox theInstance = new RflGetArityUpwardApprox();
            public static RflGetArityUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflGetArityUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                var arities = new Set<int>((x, y) => x - y);
                UserCnstSymb uc;
                UserSymbol us;
                args[0].Compute<Unit>(
                    (t, s) =>
                    {
                        if (t.Symbol == index.TypeUnionSymbol)
                        {
                            return t.Args;
                        }
                        else if (t.Symbol.Kind != SymbolKind.UserCnstSymb)
                        {
                            return null;
                        }

                        uc = (UserCnstSymb)t.Symbol;
                        if (!uc.IsTypeConstant)
                        {
                            return null;
                        }

                        if (uc.Name.Contains("["))
                        {
                            arities.Add(0);
                        }
                        else if (uc.Namespace.TryGetSymbol(uc.Name.Substring(1), out us))
                        {
                            arities.Add(us.Arity);
                        }

                        return null;
                    },
                    (t, ch, s) =>
                    {
                        return default(Unit);
                    });

                bool wasAdded;
                Term approx = null;
                foreach (var a in arities)
                {
                    approx = approx == null
                        ? index.MkCnst(new Rational(a), out wasAdded)
                        : index.MkApply(index.TypeUnionSymbol, new Term[] { index.MkCnst(new Rational(a), out wasAdded), approx }, out wasAdded);
                }

                return approx == null ? null : new Term[] { approx };
            }
        }

        private class RflIsMemberDownwardApprox : GaloisApproxTable
        {
            private static readonly RflIsMemberDownwardApprox theInstance = new RflIsMemberDownwardApprox();
            public static RflIsMemberDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflIsMemberDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType, index.TypeConstantsType };
            }
        }

        private class RflIsMemberUpwardApprox : GaloisApproxTable
        {
            private static readonly RflIsMemberUpwardApprox theInstance = new RflIsMemberUpwardApprox();
            public static RflIsMemberUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflIsMemberUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[1], index, out types))
                {
                    return null;
                }

                //// Widen types through AppFreeCanUnn to avoid full canonization algorithm.
                var widened = new AppFreeCanUnn(args[0]).MkTypeTerm(index);

                LiftedBool result = LiftedBool.Unknown;
                Term intrs;
                foreach (var t in types)
                {
                    if (!index.MkIntersection(widened, t, out intrs))
                    {
                        if (result == LiftedBool.Unknown)
                        {
                            result = false;
                        }
                        else if (result == true)
                        {
                            return new Term[] { index.CanonicalBooleanType };
                        }
                    }
                    else if ((new AppFreeCanUnn(intrs)).MkTypeTerm(index) == widened)
                    {
                        if (result == LiftedBool.Unknown)
                        {
                            result = true;
                        }
                        else if (result == false)
                        {
                            return new Term[] { index.CanonicalBooleanType };
                        }
                    }
                    else
                    {
                        return new Term[] { index.CanonicalBooleanType };
                    }
                }

                if (result == LiftedBool.Unknown)
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
                else if (result == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.FalseValue };
                }
            }
        }

        private class RflIsSubtypeDownwardApprox : GaloisApproxTable
        {
            private static readonly RflIsSubtypeDownwardApprox theInstance = new RflIsSubtypeDownwardApprox();
            public static RflIsSubtypeDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflIsSubtypeDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.TypeConstantsType, index.TypeConstantsType };
            }
        }

        private class RflIsSubtypeUpwardApprox : GaloisApproxTable
        {
            private static readonly RflIsSubtypeUpwardApprox theInstance = new RflIsSubtypeUpwardApprox();
            public static RflIsSubtypeUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflIsSubtypeUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> typesLft, typesRt;
                if (!GetTypeConstantTypes(args[0], index, out typesLft) ||
                    !GetTypeConstantTypes(args[1], index, out typesRt))
                {
                    return null;
                }

                LiftedBool result = LiftedBool.Unknown;
                foreach (var tL in typesLft)
                {
                    foreach (var tR in typesRt)
                    {
                        if (index.IsSubtypeWidened(tL, tR))
                        {
                            if (result == LiftedBool.Unknown)
                            {
                                result = true;
                            }
                            else if (result == false)
                            {
                                return new Term[] { index.CanonicalBooleanType };
                            }
                        }
                        else
                        {
                            if (result == LiftedBool.Unknown)
                            {
                                result = false;
                            }
                            else if (result == true)
                            {
                                return new Term[] { index.CanonicalBooleanType };
                            }
                        }
                    }
                }

                if (result == LiftedBool.Unknown)
                {
                    return new Term[] { index.CanonicalBooleanType };
                }
                else if (result == true)
                {
                    return new Term[] { index.TrueValue };
                }
                else
                {
                    return new Term[] { index.FalseValue };
                }
            }
        }

        private class RflGetArgTypeDownwardApprox : GaloisApproxTable
        {
            private static readonly RflGetArgTypeDownwardApprox theInstance = new RflGetArgTypeDownwardApprox();
            public static RflGetArgTypeDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflGetArgTypeDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                var typeApprox = new Set<Term>(Term.Compare);
                var indicesApprox = new Set<Term>(Term.Compare);

                int ndx;
                bool result, wasAdded;
                UserSymbol us;
                UserCnstSymb uc;
                args[0].Visit(
                    t => t.Symbol == index.TypeUnionSymbol ? t.Args : null,
                    t =>
                    {
                        if (t.Symbol.Kind != SymbolKind.UserCnstSymb)
                        {
                            return;
                        }

                        uc = (UserCnstSymb)t.Symbol;
                        if (!uc.IsTypeConstant)
                        {
                            return;
                        }

                        ndx = uc.Name.IndexOf('[');
                        if (ndx < 0)
                        {
                            return;
                        }

                        result = uc.Namespace.TryGetSymbol(uc.Name.Substring(0, ndx), out us);
                        Contract.Assert(result);
                        typeApprox.Add(index.MkApply(us, TermIndex.EmptyArgs, out wasAdded));
                        indicesApprox.Add(index.MkCnst(new Rational(int.Parse(uc.Name.Substring(ndx + 1, uc.Name.Length - ndx - 2))), out wasAdded));
                    });

                if (typeApprox.Count == 0)
                {
                    return null;
                }

                Term ta = null, ia = null;
                foreach (var t in typeApprox)
                {
                    ta = ta == null
                        ? t
                        : index.MkApply(index.TypeUnionSymbol, new Term[] { t, ta }, out wasAdded);
                }

                foreach (var i in indicesApprox)
                {
                    ia = ia == null
                        ? i
                        : index.MkApply(index.TypeUnionSymbol, new Term[] { i, ia }, out wasAdded);
                }

                return new Term[] { ta, ia };
            }
        }

        private class RflGetArgTypeUpwardApprox : GaloisApproxTable
        {
            private static readonly RflGetArgTypeUpwardApprox theInstance = new RflGetArgTypeUpwardApprox();
            public static RflGetArgTypeUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private RflGetArgTypeUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> types;
                Set<Term> indices;
                Term zero, one;
                if (!GetTypeConstantTypes(args[0], index, out types, true) ||
                    !GetNumericalElements(args[1], index, out indices, out zero, out one))
                {
                    return null;
                }

                Term approx = null;
                UserSymbol us, usp;
                bool wasAdded, result;
                int hasBaseSort = -1;

                if (indices.Contains(MkBaseSort(index, BaseSortKind.Real)) ||
                    indices.Contains(MkBaseSort(index, BaseSortKind.Integer)) ||
                    indices.Contains(MkBaseSort(index, BaseSortKind.Natural)))
                {
                    hasBaseSort = 0;
                }
                else if (indices.Contains(MkBaseSort(index, BaseSortKind.PosInteger)))
                {
                    hasBaseSort = indices.Contains(index.ZeroValue) ? 0 : 1;
                }

                //// Any index can be selected.
                if (hasBaseSort >= 0)
                {
                    foreach (var t in types)
                    {
                        us = ((UserSortSymb)t.Symbol).DataSymbol;
                        for (int i = hasBaseSort; i < us.Arity; ++i)
                        {
                            result = us.Namespace.TryGetSymbol(string.Format("#{0}[{1}]", us.Name, i), out usp);
                            Contract.Assert(result);
                            approx = approx == null
                                ? index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded)
                                : index.MkApply(index.TypeUnionSymbol, new Term[] { index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded), approx }, out wasAdded);
                        }
                    }

                    return approx == null ? null : new Term[] { approx };
                }

                //// Only need to care about numeric constants in indices.
                foreach (var t in types)
                {
                    us = ((UserSortSymb)t.Symbol).DataSymbol;
                    for (int i = 0; i < us.Arity; ++i)
                    {
                        if (!indices.Contains(index.MkCnst(new Rational(i), out wasAdded)))
                        {
                            continue;
                        }

                        result = us.Namespace.TryGetSymbol(string.Format("#{0}[{1}]", us.Name, i), out usp);
                        Contract.Assert(result);
                        approx = approx == null
                            ? index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded)
                            : index.MkApply(index.TypeUnionSymbol, new Term[] { index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded), approx }, out wasAdded);
                    }
                }

                return approx == null ? null : new Term[] { approx };
            }
        }

        private class ToListDownwardApprox : GaloisApproxTable
        {
            private static readonly ToListDownwardApprox theInstance = new ToListDownwardApprox();
            public static ToListDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToListDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class LstLengthUpwardApprox : GaloisApproxTable
        {
            private static readonly LstLengthUpwardApprox theInstance = new LstLengthUpwardApprox();
            public static LstLengthUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstLengthUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                bool wasAdded;
                if (args[0].Groundness == Groundness.Ground && args[1].Groundness == Groundness.Ground)
                {
                    BigInteger len = 0;
                    var listSymb = ((UserSortSymb)types.GetSomeElement().Symbol).DataSymbol;
                    var list = args[1];
                    while (list.Symbol == listSymb)
                    {
                        ++len;
                        list = list.Args[1];
                    }

                    var lenTerm = index.MkCnst(new Rational(len, BigInteger.One), out wasAdded);
                    return new Term[] { index.MkApply(index.RangeSymbol, new Term[] { lenTerm, lenTerm }, out wasAdded) };
                }
                else if (types.Count == 1)
                {
                    if (index.IsSubtypeWidened(args[1], types.GetSomeElement()))
                    {
                        //// Must return a non-zero length
                        return new Term[] { MkBaseSort(index, BaseSortKind.PosInteger) };
                    }
                    else if (index.IsSubtypeWidened(types.GetSomeElement(), args[1]))
                    {
                        //// Arg may also contain non-list values, otherwise previous branch would be taken
                        return new Term[] { MkBaseSort(index, BaseSortKind.Natural) };
                    }
                    else
                    {
                        //// A list value is never provided
                        return new Term[] { index.MkApply(index.RangeSymbol, new Term[] { index.ZeroValue, index.ZeroValue }, out wasAdded) };                        
                    }
                }
                else
                {
                    Term intr;
                    var listTypes = (new AppFreeCanUnn(types)).MkTypeTerm(index);
                    if (index.MkIntersection(listTypes, index.MkDataWidenedType(args[1]), out intr))
                    {
                        return new Term[] { MkBaseSort(index, BaseSortKind.Natural) };
                    }
                    else
                    {
                        return new Term[] { index.MkApply(index.RangeSymbol, new Term[] { index.ZeroValue, index.ZeroValue }, out wasAdded) };                        
                    }
                }
            }
        }

        private class LstLengthDownwardApprox : GaloisApproxTable
        {
            private static readonly LstLengthDownwardApprox theInstance = new LstLengthDownwardApprox();
            public static LstLengthDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstLengthDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType };
            }
        }

        private class LstReverseUpwardApprox : GaloisApproxTable
        {
            private static readonly LstReverseUpwardApprox theInstance = new LstReverseUpwardApprox();
            public static LstReverseUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstReverseUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 2);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                bool wasAdded;
                if (types.Count == 1 && args[1].Groundness == Groundness.Ground)
                {
                    var listSymb = ((UserSortSymb)types.GetSomeElement().Symbol).DataSymbol;
                    var list = args[1];
                    var revQueue = new Queue<Term>();
                    while (list.Symbol == listSymb)
                    {
                        revQueue.Enqueue(list.Args[0]);
                        list = list.Args[1];
                    }

                    //// Now reverse the list keeping the same list terminator (at the top of the stack).
                    var revList = list;
                    while (revQueue.Count > 0)
                    {
                        revList = index.MkApply(listSymb, new Term[] { revQueue.Dequeue(), revList }, out wasAdded);
                    }

                    return new Term[] { revList };
                }
                else
                {
                    return new Term[] { index.MkDataWidenedType(args[1]) };
                }
            }
        }

        private class LstReverseDownwardApprox : GaloisApproxTable
        {
            private static readonly LstReverseDownwardApprox theInstance = new LstReverseDownwardApprox();
            public static LstReverseDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstReverseDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType };
            }
        }

        private class LstFindUpwardApprox : GaloisApproxTable
        {
            private static readonly LstFindUpwardApprox theInstance = new LstFindUpwardApprox();
            public static LstFindUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 4);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                if (types.Count == 1 && args[1].Groundness == Groundness.Ground)
                {
                    var listSymb = ((UserSortSymb)types.GetSomeElement().Symbol).DataSymbol;
                    var list = args[1];
                    while (list.Symbol == listSymb)
                    {
                        if (Unifier.IsUnifiable(list.Args[0], args[2]))
                        {
                            return new Term[] { list.Args[0] };
                        }
                        list = list.Args[1];
                    }

                    return new Term[] {args[3]};

                }
                else
                {
                    return new Term[] {index.MkDataWidenedType(args[3])};
                }
            }
        }

        private class LstFindDownwardApprox : GaloisApproxTable
        {
            private static readonly LstFindDownwardApprox theInstance = new LstFindDownwardApprox();
            public static LstFindDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType, index.CanonicalAnyType, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class LstFindAllUpwardApprox : GaloisApproxTable
        {
            private static readonly LstFindAllUpwardApprox theInstance = new LstFindAllUpwardApprox();
            public static LstFindAllUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindAllUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                if (types.Count == 1 && args[1].Groundness == Groundness.Ground)
                {
                    var listSymb = ((UserSortSymb)types.GetSomeElement().Symbol).DataSymbol;
                    var list = args[1];
                    var listStack = new Stack<Term>();
                    while (list.Symbol == listSymb)
                    {
                        if (Unifier.IsUnifiable(list.Args[0], args[2]))
                        {
                            listStack.Push(list.Args[0]);
                        }
                        list = list.Args[1];
                    }

                    bool wasAdded;
                    var newList = list;
                    while (listStack.Count > 0)
                    {
                        newList = index.MkApply(listSymb, new Term[] { listStack.Pop(), newList }, out wasAdded);
                    }

                    return new Term[] { newList };
                }
                else
                {
                    return new Term[] {index.MkDataWidenedType(args[1])};
                }
            }
        }

        private class LstFindAllDownwardApprox : GaloisApproxTable
        {
            private static readonly LstFindAllDownwardApprox theInstance = new LstFindAllDownwardApprox();
            public static LstFindAllDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindAllDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class LstFindAllNotUpwardApprox : GaloisApproxTable
        {
            private static readonly LstFindAllNotUpwardApprox theInstance = new LstFindAllNotUpwardApprox();
            public static LstFindAllNotUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindAllNotUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                if (types.Count == 1 && args[1].Groundness == Groundness.Ground)
                {
                    var listSymb = ((UserSortSymb)types.GetSomeElement().Symbol).DataSymbol;
                    var list = args[1];
                    var listStack = new Stack<Term>();
                    while (list.Symbol == listSymb)
                    {
                        if (!Unifier.IsUnifiable(list.Args[0], args[2]))
                        {
                            listStack.Push(list.Args[0]);
                        }
                        list = list.Args[1];
                    }

                    bool wasAdded;
                    var newList = list;
                    while (listStack.Count > 0)
                    {
                        newList = index.MkApply(listSymb, new Term[] { listStack.Pop(), newList }, out wasAdded);
                    }

                    return new Term[] { newList };
                }
                else
                {
                    return new Term[] {index.MkDataWidenedType(args[1])};
                }
            }
        }

        private class LstFindAllNotDownwardApprox : GaloisApproxTable
        {
            private static readonly LstFindAllNotDownwardApprox theInstance = new LstFindAllNotDownwardApprox();
            public static LstFindAllNotDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstFindAllNotDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class LstGetAtUpwardApprox : GaloisApproxTable
        {
            private static readonly LstGetAtUpwardApprox theInstance = new LstGetAtUpwardApprox();
            public static LstGetAtUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstGetAtUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                Set<Term> types;
                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                return new Term[] {index.MkDataWidenedType(args[2])};
            }
        }

        private class LstGetAtDownwardApprox : GaloisApproxTable
        {
            private static readonly LstGetAtDownwardApprox theInstance = new LstGetAtDownwardApprox();
            public static LstGetAtDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private LstGetAtDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                if (index.ListTypeConstantsType == null)
                {
                    //// Then just return some value, which will be filtered by upward approx.
                    return new Term[] { index.FalseValue, index.CanonicalAnyType, index.CanonicalAnyType };
                }

                return new Term[] { index.ListTypeConstantsType, index.CanonicalAnyType, index.CanonicalAnyType };
            }
        }

        private class ToListUpwardApprox : GaloisApproxTable
        {
            private static readonly ToListUpwardApprox theInstance = new ToListUpwardApprox();
            public static ToListUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToListUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 3);
                Set<Term> types;
                Set<Term> approxSet = new Set<Term>(Term.Compare);

                if (!GetTypeConstantTypes(args[0], index, out types, true))
                {
                    return null;
                }

                UserSortSymb sort;
                UserSymbol data;
                Term intrs;
                var widened = index.MkDataWidenedType(args[1]);
                foreach (var t in types)
                {
                    sort = (UserSortSymb)t.Symbol;
                    data = sort.DataSymbol;
                    if (data.Arity != 2 || !data.CanonicalForm[1].Contains(sort))
                    {
                        continue;
                    }

                    if (!index.MkIntersection(index.GetCanonicalTerm(data, 1), widened, out intrs))
                    {
                        continue;
                    }

                    approxSet.Add(t);
                    approxSet.Add(intrs);
                }

                if (approxSet.Count == 0)
                {
                    return null;
                }
                else
                {
                    return new Term[] { (new AppFreeCanUnn(approxSet)).MkTypeTerm(index) };
                }
            }
        }

        private class ToStringDownwardApprox : GaloisApproxTable
        {
            private static readonly ToStringDownwardApprox theInstance = new ToStringDownwardApprox();
            public static ToStringDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToStringDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType };
            }
        }

        private class ToStringUpwardApprox : GaloisApproxTable
        {
            private static readonly ToStringUpwardApprox theInstance = new ToStringUpwardApprox();
            public static ToStringUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToStringUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                bool wasAdded;
                Rational r1, r2;
                BigInteger start, end;
                Set<Term> strings = new Set<Term>(Term.Compare);
                var continueExpand = new SuccessToken();

                args[0].Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return;
                        }

                        if (x.Groundness == Groundness.Ground)
                        {
                            strings.Add(index.MkString(x));
                        }
                        else if (x.Symbol == index.RangeSymbol)
                        {
                            r1 = (Rational)((BaseCnstSymb)x.Args[0].Symbol).Raw;
                            r2 = (Rational)((BaseCnstSymb)x.Args[1].Symbol).Raw;
                            start = BigInteger.Min(r1.Numerator, r2.Numerator);
                            end = BigInteger.Max(r1.Numerator, r2.Numerator);
                            if (end - start <= NumWideningWidth)
                            {
                                var size = (int)(end - start);
                                for (var i = 0; i <= size; ++i)
                                {
                                    strings.Add(index.MkString(index.MkCnst(new Rational(start + i, BigInteger.One), out wasAdded)));
                                }
                            }
                            else
                            {
                                continueExpand.Failed();
                            }
                        }
                        else
                        {
                            continueExpand.Failed();
                        }
                    },
                    continueExpand);

                if (!continueExpand.Result)
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.String) };
                }

                Term approx = null;
                foreach (var s in strings)
                {
                    approx = approx == null
                        ? s
                        : index.MkApply(index.TypeUnionSymbol, new Term[] { s, approx }, out wasAdded);
                }

                return new Term[] { approx };
            }
        }

        private class ToNaturalDownwardApprox : GaloisApproxTable
        {
            private static readonly ToNaturalDownwardApprox theInstance = new ToNaturalDownwardApprox();
            public static ToNaturalDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToNaturalDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                return new Term[] { index.CanonicalAnyType };
            }
        }

        private class ToNaturalUpwardApprox : GaloisApproxTable
        {
            private static readonly ToNaturalUpwardApprox theInstance = new ToNaturalUpwardApprox();
            public static ToNaturalUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToNaturalUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);
                bool wasAdded;
                Rational r1, r2;
                BigInteger start, end;
                Set<Term> nats = new Set<Term>(Term.Compare);
                var continueExpand = new SuccessToken();

                args[0].Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return;
                        }

                        if (x.Groundness == Groundness.Ground)
                        {
                            nats.Add(index.MkNatural(x));
                        }
                        else if (x.Symbol == index.RangeSymbol)
                        {
                            r1 = (Rational)((BaseCnstSymb)x.Args[0].Symbol).Raw;
                            r2 = (Rational)((BaseCnstSymb)x.Args[1].Symbol).Raw;
                            start = BigInteger.Min(r1.Numerator, r2.Numerator);
                            end = BigInteger.Max(r1.Numerator, r2.Numerator);
                            if (end - start <= NumWideningWidth)
                            {
                                var size = (int)(end - start);
                                for (var i = 0; i <= size; ++i)
                                {
                                    nats.Add(index.MkNatural(index.MkCnst(new Rational(start + i, BigInteger.One), out wasAdded)));
                                }
                            }
                            else
                            {
                                continueExpand.Failed();
                            }
                        }
                        else
                        {
                            continueExpand.Failed();
                        }
                    },
                    continueExpand);

                if (!continueExpand.Result)
                {
                    return new Term[] { MkBaseSort(index, BaseSortKind.Natural) };
                }

                Term approx = null;
                foreach (var s in nats)
                {
                    approx = approx == null
                        ? s
                        : index.MkApply(index.TypeUnionSymbol, new Term[] { s, approx }, out wasAdded);
                }

                return new Term[] { approx };
            }
        }

        private class ToSymbolDownwardApprox : GaloisApproxTable
        {
            private static readonly ToSymbolDownwardApprox theInstance = new ToSymbolDownwardApprox();
            public static ToSymbolDownwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToSymbolDownwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);

                bool wasAdded;
                UserCnstSymb uc;
                UserSymbol us;
                UserSortSymb uss;
                var types = new List<Term>();
                args[0].Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return;
                        }

                        switch (x.Symbol.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                            case SymbolKind.BaseSortSymb:
                                types.Add(x);
                                break;
                            case SymbolKind.UserCnstSymb:
                                uc = (UserCnstSymb)x.Symbol;
                                types.Add(x);
                                if (uc.IsTypeConstant && !uc.Name.Contains('['))
                                {
                                    uc.Namespace.TryGetSymbol(uc.Name.Substring(1), out us);
                                    switch (us.Kind)
                                    {
                                        case SymbolKind.ConSymb:
                                            uss = ((ConSymb)us).SortSymbol;
                                            types.Add(index.MkApply(uss, TermIndex.EmptyArgs, out wasAdded));
                                            break;
                                        case SymbolKind.MapSymb:
                                            uss = ((MapSymb)us).SortSymbol;
                                            types.Add(index.MkApply(uss, TermIndex.EmptyArgs, out wasAdded));
                                            break;
                                        case SymbolKind.UnnSymb:
                                            break;
                                        default:
                                            throw new NotImplementedException();
                                    }
                                }

                                break;
                            case SymbolKind.BaseOpSymb:
                                Contract.Assert(x.Symbol == index.RangeSymbol);
                                types.Add(x);
                                break;
                            case SymbolKind.ConSymb:
                            case SymbolKind.MapSymb:
                            case SymbolKind.UserSortSymb:
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    });

                if (types.Count == 0)
                {
                    return null;
                }

                var appFree = new AppFreeCanUnn(types);
                return new Term[] { appFree.MkTypeTerm(index) };
            }
        }

        private class ToSymbolUpwardApprox : GaloisApproxTable
        {
            private static readonly ToSymbolUpwardApprox theInstance = new ToSymbolUpwardApprox();
            public static ToSymbolUpwardApprox Instance
            {
                get { return theInstance; }
            }

            private ToSymbolUpwardApprox()
                : base(Approx)
            { }

            private static Term[] Approx(TermIndex index, Term[] args)
            {
                Contract.Requires(index != null && args != null && args.Length == 1);

                bool wasAdded;
                UserSymbol us, usp;
                var types = new List<Term>();
                args[0].Visit(
                    x => x.Symbol == index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == index.TypeUnionSymbol)
                        {
                            return;
                        }

                        switch (x.Symbol.Kind)
                        {
                            case SymbolKind.BaseCnstSymb:
                            case SymbolKind.BaseSortSymb:
                            case SymbolKind.UserCnstSymb:
                                types.Add(x);
                                break;
                            case SymbolKind.BaseOpSymb:
                                Contract.Assert(x.Symbol == index.RangeSymbol);
                                types.Add(x);
                                break;
                            case SymbolKind.ConSymb:
                            case SymbolKind.MapSymb:
                                us = (UserSymbol)x.Symbol;
                                us.Namespace.TryGetSymbol("#" + us.Name, out usp);
                                types.Add(index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded));
                                break;
                            case SymbolKind.UserSortSymb:
                                us = ((UserSortSymb)x.Symbol).DataSymbol;
                                us.Namespace.TryGetSymbol("#" + us.Name, out usp);
                                types.Add(index.MkApply(usp, TermIndex.EmptyArgs, out wasAdded));
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    });

                var appFree = new AppFreeCanUnn(types);
                return new Term[] { appFree.MkTypeTerm(index) };
            }
        }
        #endregion
    }
}
