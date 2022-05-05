namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Rules;
    using Common.Terms;

    //// Aliases for Z3 types to avoid clashes
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3Context = Microsoft.Z3.Context;

    /// <summary>
    /// A symbolic element is a term, possibly with symbolic constants, that exists in the LFP of the program
    /// only for those valuations where the side constraint is satisfied. For backtracking purposes, side constraints
    /// map from a state index to a side constraint. The overall side constraint is a disjunction of the map's image.
    /// </summary>
    internal class SymElement
    {
        public Term Term
        {
            get;
            private set;
        }

        public Z3Expr Encoding
        {
            get;
            private set;
        }

        public Map<int, Z3BoolExpr> SideConstraints
        {
            get;
            private set;
        }

        public Z3BoolExpr GetAllSideConstraints(Z3Context context)
        {
            Z3BoolExpr curr;

            curr = SideConstraints[0];
            for (int i = 1; i < SideConstraints.Count; i++)
            {
                curr = context.MkAnd(curr, SideConstraints[i]);
            }

            return curr;
        }

        public bool HasSideConstraints()
        {
            return !SideConstraints.IsEmpty();
        }

        /// <summary>
        ///  The earliest index at which the side constraint is known to be a tautology.
        /// </summary>
        public int TautologyNumber
        {
            get;
            private set;
        }

        public SymElement(Term term, Z3Expr encoding, Z3Context context)
        {
            Contract.Requires(term != null && encoding != null && context != null);
            Term = term;
            Encoding = encoding;
            SideConstraints = new Map<int, Z3BoolExpr>(Compare);
        }

        /// <summary>
        /// Conjoins the current side constraint with constr
        /// </summary>
        /// <param name="constr"></param>
        /// <param name="context"></param>
        public void ExtendSideConstraint(int index, Z3BoolExpr constr, Z3Context context)
        {
            Z3BoolExpr crntConstr;
            if (SideConstraints.TryFindValue(index, out crntConstr))
            {
                SideConstraints[index] = context.MkAnd(crntConstr, constr);
            }
            else
            {
                SideConstraints.Add(index, constr);
            }
        }

        public void DisjoinSideConstraints(int index, Z3BoolExpr[] constr, Z3Context context)
        {
            Z3BoolExpr prevConstraint;
            Z3BoolExpr currConstraint;

            if (constr.Length == 1)
            {
                currConstraint = constr[0];
            }
            else
            {
                currConstraint = context.MkAnd(constr);
            }

            if (SideConstraints.TryFindValue(index, out prevConstraint))
            {
                SideConstraints[index] = context.MkOr(prevConstraint, currConstraint);
            }
            else
            {
                SideConstraints.Add(index, currConstraint);
            }
        }

        /// <summary>
        /// Removes all constraints introduced at or after index.
        /// </summary>
        /// <param name="index"></param>
        public void ContractSideConstraint(int index)
        {
            var deleteList = new LinkedList<int>();
            foreach (var kv in SideConstraints.GetEnumerable(index))
            {
                deleteList.AddLast(kv.Key);
            }

            foreach (var k in deleteList)
            {
                SideConstraints.Remove(k);
            }
        }

        /// <summary>
        /// "null" is the smallest symbolic element.
        /// </summary>
        public static int Compare(SymElement e1, SymElement e2)
        {
            if (e1 == null && e2 == null)
            {
                return 0;
            }
            else if (e1 == null && e2 != null)
            {
                return -1;
            }
            else if (e1 != null && e2 == null)
            {
                return 1;
            }
            else
            {
                return Term.Compare(e1.Term, e2.Term);
            }
        }

        public void Debug_Print()
        {
            Console.WriteLine(Term.Debug_GetSmallTermString());
        }

        private static int Compare(int x, int y)
        {
            if (x < y)
            {
                return -1;
            }
            else if (x > y)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }
    }
}
