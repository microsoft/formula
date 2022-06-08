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

        public bool HasConstraints()
        {
            return !constraintData.IsEmpty();
        }

        private List<Tuple<HashSet<Z3BoolExpr>, Set<Term>, Set<Term>>> constraintData
            = new List<Tuple<HashSet<Z3BoolExpr>, Set<Term>, Set<Term>>>();

        public void AddConstraintData(HashSet<Z3BoolExpr> exprs, Set<Term> posTerms, Set<Term> negTerms)
        {
            bool needToAdd = true;

            foreach (var item in constraintData)
            {
                if (item.Item2.IsSameSet(posTerms) &&
                    item.Item3.IsSameSet(negTerms) &&
                    item.Item1.SetEquals(exprs))
                {
                    needToAdd = false;
                    break;
                }
            }

            if (needToAdd)
            {
                var data = new Tuple<HashSet<Z3BoolExpr>, Set<Term>, Set<Term>>(exprs, posTerms, negTerms);
                constraintData.Add(data);
            }
        }

        private IEnumerable<Z3BoolExpr> GetSideConstraints(SymExecuter executer, Set<Term> processed)
        {
            List<Z3BoolExpr> constraints = new List<Z3BoolExpr>();
            Term t = this.Term;
            processed.Add(t);
            SymElement next;

            foreach (var constraint in constraintData)
            {
                foreach (var posTerm in constraint.Item2)
                {
                    if (!processed.Contains(posTerm) &&
                        executer.GetSymbolicTerm(posTerm, out next))
                    {
                        constraints.AddRange(next.GetSideConstraints(executer, processed));
                    }
                }

                foreach (var negTerm in constraint.Item3)
                {
                    if (!processed.Contains(negTerm) &&
                        executer.GetSymbolicTerm(negTerm, out next))
                    {
                        foreach (var negConstraint in next.GetSideConstraints(executer, processed))
                        {
                            constraints.Add(executer.Solver.Context.MkNot(negConstraint));
                        }
                    }
                }

                constraints.AddRange(constraint.Item1);
            }


            return constraints;
        }

        public Z3BoolExpr GetSideConstraints(SymExecuter executer)
        {
            List<Z3BoolExpr> constraints = new List<Z3BoolExpr>();
            Set<Term> processed = new Set<Term>(Term.Compare);
            Term t = this.Term;
            SymElement next;
            Z3BoolExpr topLevelConstraint = null;
            Z3Context context = executer.Solver.Context;

            foreach (var constraint in constraintData)
            {
                foreach (var posTerm in constraint.Item2)
                {
                    processed.Clear();
                    processed.Add(t);
                    if (executer.GetSymbolicTerm(posTerm, out next))
                    {
                        constraints.AddRange(next.GetSideConstraints(executer, processed));
                    }
                }

                foreach (var negTerm in constraint.Item3)
                {
                    processed.Clear();
                    processed.Add(t);
                    if (executer.GetSymbolicTerm(negTerm, out next))
                    {
                        foreach (var negConstraint in next.GetSideConstraints(executer, processed))
                        {
                            constraints.Add(context.MkNot(negConstraint));
                        }
                    }
                }

                constraints.AddRange(constraint.Item1);

                var currConstraint = context.MkAnd(constraints);
                if (topLevelConstraint == null)
                {
                    topLevelConstraint = currConstraint;
                }
                else
                {
                    topLevelConstraint = context.MkOr(topLevelConstraint, currConstraint);
                }

                constraints.Clear();
            }


            return topLevelConstraint;
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
