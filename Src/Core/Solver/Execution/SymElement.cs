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

        public bool IsDirectlyProvable
        {
            get;
            private set;
        }

        public void SetDirectlyProvable()
        {
            IsDirectlyProvable = true;
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

        private Z3BoolExpr GetSideConstraints(SymExecuter executer, Set<Term> processed)
        {
            List<Z3BoolExpr> constraints = new List<Z3BoolExpr>();
            Term t = this.Term;
            processed.Add(t);
            SymElement next;
            Z3BoolExpr topConstraint = null;
            Z3BoolExpr currConstraint = null;
            Set<Term> localProcessed = new Set<Term>(Term.Compare);

            if (IsDirectlyProvable)
            {
                return executer.Solver.Context.MkTrue();
            }

            foreach (var constraint in constraintData)
            {
                currConstraint = null;
                localProcessed.Clear();
                foreach (var term in processed)
                {
                    localProcessed.Add(term);
                }

                foreach (var posTerm in constraint.Item2)
                {
                    if (!localProcessed.Contains(posTerm) &&
                        executer.GetSymbolicTerm(posTerm, out next))
                    {
                        var nextConstraint = next.GetSideConstraints(executer, localProcessed);

                        if (currConstraint == null)
                        {
                            currConstraint = nextConstraint;
                        }
                        else
                        {
                            currConstraint = executer.Solver.Context.MkAnd(currConstraint, nextConstraint);
                        }
                    }
                }

                localProcessed.Clear();
                foreach (var term in processed)
                {
                    localProcessed.Add(term);
                }

                foreach (var negTerm in constraint.Item3)
                {
                    if (!processed.Contains(negTerm) &&
                        executer.GetSymbolicTerm(negTerm, out next))
                    {
                        var nextConstraint = next.GetSideConstraints(executer, localProcessed);
                        nextConstraint = executer.Solver.Context.MkNot(nextConstraint);

                        if (currConstraint == null)
                        {
                            currConstraint = nextConstraint;
                        }
                        else
                        {
                            currConstraint = executer.Solver.Context.MkAnd(currConstraint, nextConstraint);
                        }
                    }
                }

                foreach (var nextConstraint in constraint.Item1)
                {
                    if (currConstraint == null)
                    {
                        currConstraint = nextConstraint;
                    }
                    else
                    {
                        currConstraint = executer.Solver.Context.MkAnd(currConstraint, nextConstraint);
                    }
                }

                if (topConstraint == null)
                {
                    topConstraint = currConstraint;
                }
                else
                {
                    topConstraint = executer.Solver.Context.MkOr(topConstraint, currConstraint);
                }
            }

            return topConstraint;
        }

        public Z3BoolExpr GetSideConstraints(SymExecuter executer)
        {
            List<Z3BoolExpr> constraints = new List<Z3BoolExpr>();
            Set<Term> processed = new Set<Term>(Term.Compare);
            Term t = this.Term;
            SymElement next;
            Z3BoolExpr topConstraint = null;
            Z3BoolExpr currConstraint = null;
            Z3Context context = executer.Solver.Context;

            if (IsDirectlyProvable)
            {
                return executer.Solver.Context.MkTrue();
            }

            foreach (var constraint in constraintData)
            {
                currConstraint = null;
                foreach (var posTerm in constraint.Item2)
                {
                    processed.Clear();
                    processed.Add(t);
                    if (executer.GetSymbolicTerm(posTerm, out next))
                    {
                        var nextConstraint = next.GetSideConstraints(executer, processed);

                        if (currConstraint == null)
                        {
                            currConstraint = nextConstraint;
                        } 
                        else
                        {
                            currConstraint = context.MkAnd(currConstraint, nextConstraint);
                        }
                    }
                }

                foreach (var negTerm in constraint.Item3)
                {
                    processed.Clear();
                    processed.Add(t);
                    if (executer.GetSymbolicTerm(negTerm, out next))
                    {
                        var nextConstraint = next.GetSideConstraints(executer, processed);
                        nextConstraint = context.MkNot(nextConstraint);

                        if (currConstraint == null)
                        {
                            currConstraint = nextConstraint;
                        }
                        else
                        {
                            currConstraint = context.MkAnd(currConstraint, nextConstraint);
                        }
                    }
                }

                foreach (var nextConstraint in constraint.Item1)
                {
                    if (currConstraint == null)
                    {
                        currConstraint = nextConstraint;
                    }
                    else
                    {
                        currConstraint = executer.Solver.Context.MkAnd(currConstraint, nextConstraint);
                    }
                }

                if (topConstraint == null)
                {
                    topConstraint = currConstraint;
                }
                else
                {
                    topConstraint = executer.Solver.Context.MkOr(topConstraint, currConstraint);
                }
            }

            return topConstraint;
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
            IsDirectlyProvable = false;
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
