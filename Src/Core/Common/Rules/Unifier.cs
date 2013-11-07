namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Compiler;
    using Extras;
    using Terms;
    
    internal static class Unifier
    {
        //// Suppose f(...,t,...) is in an equivalence class for data constructor f,
        //// and t is not of the form g(...) for data constructor g.
        //// Then t is tracked.
        ////
        //// Suppose .(t, "lbl") is in some equivalence class, then .(t, "lbl")
        //// is tracked in the class of t.
        private const int Track_FreeOccurs = 0;

        //// Suppose t = f(...,x,...) is in an equivalence class for data constructor f.
        //// Then t is tracked; only one such t is tracked per class.
        private const int Track_FreeApply = 0;

        public static bool IsUnifiable(Term tA, Term tB, bool standardize = true)
        {
            Contract.Requires(tA != null && tB != null);
            Contract.Requires(tA.Owner == tB.Owner);

            var eqs = new EquivalenceRelation<StdTerm>(StdTerm.Compare, 1, 1);
            var success = new SuccessToken();
            var relabelSymbol = tA.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel);
            var pending = new Stack<Tuple<StdTerm, StdTerm>>();
            pending.Push(new Tuple<StdTerm,StdTerm>(tA.Standardize(0), tB.Standardize(standardize ? 1 : 0)));

            int lblA, lblB;
            Term trmA, trmB;
            Tuple<StdTerm, StdTerm> top;
            Set<StdTerm> allVars = new Set<StdTerm>(StdTerm.Compare);
            while (pending.Count > 0)
            {
                top = pending.Pop();
                if (StdTerm.Compare(top.Item1, top.Item2) == 0)
                {
                    continue;
                }

                top.GetComponents(out trmA, out lblA, out trmB, out lblB);
                trmA.Compute<Unit>(
                    trmB,
                    (xA, xB, s) =>
                    {
                        if (xA.Symbol == relabelSymbol)
                        {
                            xA = EliminateRelabel(xA);
                        }

                        if (xB.Symbol == relabelSymbol)
                        {
                            xB = EliminateRelabel(xB);
                        }

                        if (xA.Groundness == Groundness.Ground && xB.Groundness == Groundness.Ground)
                        {
                            //// If x1 and x2 are ground, then just check equality.
                            if (xA != xB)
                            {
                                success.Failed();
                            }

                            return null;
                        }
                        else if ((xA.Symbol.IsDataConstructor || xA.Symbol.IsNonVarConstant) && 
                                 (xB.Symbol.IsDataConstructor || xB.Symbol.IsNonVarConstant))
                        {
                            //// If x1 and x2 are both data constructors, 
                            //// then check symbol equality and unroll.
                            if (xA.Symbol != xB.Symbol)
                            {
                                success.Failed();
                                return null;
                            }

                            return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(xA.Args, xB.Args);
                        }
                        else if (xA.Symbol.IsVariable || xA.Symbol == xA.Owner.SelectorSymbol)
                        {
                            Bind(allVars, eqs, pending, xA.Standardize(lblA), xB.Standardize(lblB));
                            return null;
                        }
                        else
                        {
                            Bind(allVars, eqs, pending, xB.Standardize(lblB), xA.Standardize(lblA));
                            return null;
                        }
                    },
                    (xA, xB, ch, s) =>
                    {
                        return default(Unit);
                    },
                    success);
            }

            if (!success.Result)
            {
                return false;
            }

            return OccursCheck(allVars, eqs);
        }

        /// <summary>
        /// If tA and tB are unifiable, then returns an mgu with normalized variable names.
        /// varCreator gives a variable for the nth distinct variable (from left-to-right) in the mgu, beginning with index 0.
        /// </summary>
        public static bool IsUnifiable(Term tA, Term tB, Func<int, Term> varCreator, out Term mgu, bool standardize = true)
        {
            Contract.Requires(tA != null && tB != null && varCreator != null);
            Contract.Requires(tA.Owner == tB.Owner);

            mgu = null;
            var eqs = new EquivalenceRelation<StdTerm>(StdTerm.Compare, 1, 1);
            var success = new SuccessToken();
            var relabelSymbol = tA.Owner.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel);
            var pending = new Stack<Tuple<StdTerm, StdTerm>>();
            pending.Push(new Tuple<StdTerm, StdTerm>(tA.Standardize(0), tB.Standardize(standardize ? 1 : 0)));

            int lblA, lblB;
            Term trmA, trmB;
            Tuple<StdTerm, StdTerm> top;
            Set<StdTerm> allVars = new Set<StdTerm>(StdTerm.Compare);
            while (pending.Count > 0)
            {
                top = pending.Pop();
                if (StdTerm.Compare(top.Item1, top.Item2) == 0)
                {
                    continue;
                }

                top.GetComponents(out trmA, out lblA, out trmB, out lblB);
                trmA.Compute<Unit>(
                    trmB,
                    (xA, xB, s) =>
                    {
                        if (xA.Symbol == relabelSymbol)
                        {
                            xA = EliminateRelabel(xA);
                        }

                        if (xB.Symbol == relabelSymbol)
                        {
                            xB = EliminateRelabel(xB);
                        }

                        if (xA.Groundness == Groundness.Ground && xB.Groundness == Groundness.Ground)
                        {
                            //// If x1 and x2 are ground, then just check equality.
                            if (xA != xB)
                            {
                                success.Failed();
                            }

                            return null;
                        }
                        else if ((xA.Symbol.IsDataConstructor || xA.Symbol.IsNonVarConstant) &&
                                 (xB.Symbol.IsDataConstructor || xB.Symbol.IsNonVarConstant))
                        {
                            //// If x1 and x2 are both data constructors, 
                            //// then check symbol equality and unroll.
                            if (xA.Symbol != xB.Symbol)
                            {
                                success.Failed();
                                return null;
                            }

                            return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(xA.Args, xB.Args);
                        }
                        else if (xA.Symbol.IsVariable || xA.Symbol == xA.Owner.SelectorSymbol)
                        {
                            Bind(allVars, eqs, pending, xA.Standardize(lblA), xB.Standardize(lblB));
                            return null;
                        }
                        else
                        {
                            Bind(allVars, eqs, pending, xB.Standardize(lblB), xA.Standardize(lblA));
                            return null;
                        }
                    },
                    (xA, xB, ch, s) =>
                    {
                        return default(Unit);
                    },
                    success);
            }

            if (!success.Result || !OccursCheck(allVars, eqs))
            {
                return false;
            }

            mgu = MkMGU(tA.Standardize(0), eqs, varCreator);
            return true;
        }

        private static Term MkMGU(StdTerm t, EquivalenceRelation<StdTerm> eqs, Func<int, Term> varCreator)
        {
            Term normVar;
            StdTerm stdRep, stdX;
            int i, label;
            bool wasAdded;
            var varMap = new Map<StdTerm, Term>(StdTerm.Compare);
            var labelStack = new Stack<int>();
            labelStack.Push(t.label);            
            var result = t.term.Compute<Term>(
                (x, s) =>
                {
                    if (x.Groundness == Groundness.Ground)
                    {
                        labelStack.Push(labelStack.Peek());
                        return null;
                    }
                    else if (!x.Symbol.IsVariable)
                    {
                        labelStack.Push(labelStack.Peek());
                        return x.Args;
                    }

                    stdX = x.Standardize(labelStack.Peek());
                    if (eqs.GetTracker(stdX, Track_FreeApply, out stdRep))
                    {
                        labelStack.Push(stdRep.label);
                        return EnumerableMethods.GetEnumerable<Term>(stdRep.term);
                    }
                    else if (StdTerm.Compare(stdRep = eqs.GetRepresentative(stdX), stdX) == 0)
                    {
                        labelStack.Push(labelStack.Peek());
                        return null;
                    }
                    else
                    {
                        labelStack.Push(stdRep.label);
                        return EnumerableMethods.GetEnumerable<Term>(stdRep.term);
                    }                   
                },
                (x, c, s) =>
                {
                    label = labelStack.Pop();
                    if (x.Groundness == Groundness.Ground)
                    {
                        return x;
                    }
                    else if (!x.Symbol.IsVariable)
                    {
                        i = 0;
                        var args = new Term[x.Symbol.Arity];
                        foreach (var tp in c)
                        {
                            args[i++] = tp;
                        }

                        return x.Owner.MkApply(x.Symbol, args, out wasAdded);
                    }

                    if (c.IsEmpty<Term>())
                    {
                        if (!varMap.TryFindValue(x.Standardize(label), out normVar))
                        {
                            normVar = varCreator(varMap.Count);
                            varMap.Add(x.Standardize(label), normVar);
                        }

                        return normVar;
                    }
                    else
                    {
                        return c.First<Term>();
                    }
                });

            Contract.Assert(labelStack.Count == 1);
            return result;
        }

        private static void Bind(
            Set<StdTerm> allVars,
            EquivalenceRelation<StdTerm> eqs,
            Stack<Tuple<StdTerm, StdTerm>> pending,
            StdTerm bindee,
            StdTerm binding)
        {
            if (eqs.Equals(bindee, binding))
            {
                return;
            }

            var index = bindee.term.Owner;
            Contract.Assert(bindee.term.Symbol == index.SelectorSymbol || bindee.term.Symbol.IsVariable);
            Contract.Assert(binding.term.Symbol == index.SelectorSymbol || binding.term.Symbol.IsVariable || binding.term.Symbol.IsDataConstructor || binding.term.Symbol.IsNonVarConstant);

            RegisterSelectors(allVars, eqs, bindee);
            if (binding.term.Symbol.IsDataConstructor || binding.term.Symbol.IsNonVarConstant)
            {
                var crntBinding = eqs.SetTracker(bindee, Track_FreeApply, binding);
                if (StdTerm.Compare(crntBinding, binding) != 0)
                {
                    pending.Push(new Tuple<StdTerm, StdTerm>(crntBinding, binding));
                }

                binding.term.Visit(
                    x => x.Groundness == Groundness.Variable && x.Symbol.IsDataConstructor ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol.IsVariable || x.Symbol == index.SelectorSymbol)
                        {
                            RegisterSelectors(allVars, eqs, x.Standardize(binding.label));
                            eqs.AddToTrackSet(bindee, Track_FreeOccurs, x.Standardize(binding.label));
                        }
                    });
            }
            else
            {
                RegisterSelectors(allVars, eqs, binding);
            }

            eqs.Equate(bindee, binding);
        }

        private static void RegisterSelectors(Set<StdTerm> allVars, EquivalenceRelation<StdTerm> eqs, StdTerm stdterm)
        {
            var index = stdterm.term.Owner;
            var t = stdterm.term;
            eqs.Add(stdterm);
            while (t.Symbol == index.SelectorSymbol)
            {
                eqs.AddToTrackSet(
                    t.Args[0].Standardize(stdterm.label), 
                    Track_FreeOccurs, 
                    t.Standardize(stdterm.label));
                t = t.Args[0];
            }

            Contract.Assert(t.Symbol.IsVariable);
            allVars.Add(t.Standardize(stdterm.label));
        }

        private static StdTerm Standardize(this Term t, int label)
        {
            return new StdTerm(t, label);
        }

        private static void GetComponents(this Tuple<StdTerm, StdTerm> pair, out Term trmA, out int lblA, out Term trmB, out int lblB)
        {
            trmA = pair.Item1.term;
            lblA = pair.Item1.label;

            trmB = pair.Item2.term;
            lblB = pair.Item2.label;
        }

        private static Term EliminateRelabel(Term t)
        {
            var index = t.Owner;
            var table = index.SymbolTable;
            var relabelSymbol = table.GetOpSymbol(ReservedOpKind.Relabel);
            var relabelStack = new Stack<Term>();
            Namespace relabeled;
            UserSymbol otherSymbol;
            int i = 0;
            bool wasAdded;

            return t.Compute<Term>(
                (x, s) =>
                {
                    if (x.Symbol == index.SelectorSymbol)
                    {
                        return null;
                    }

                    if (x.Symbol == relabelSymbol)
                    {
                        relabelStack.Push(x);
                    }

                    return x.Args;
                },
                (x, ch, s) =>
                {
                    if (x.Symbol == relabelSymbol)                    
                    {
                        relabelStack.Pop();
                        using (var it = ch.GetEnumerator())
                        {
                            it.MoveNext();
                            it.MoveNext();
                            it.MoveNext();
                            return it.Current;
                        }
                    }
                    
                    if (x.Symbol.IsVariable || x.Symbol.IsNewConstant || x.Symbol == index.SelectorSymbol)
                    {
                        return x;
                    }

                    Contract.Assert(x.Symbol.IsDataConstructor || x.Symbol.IsDerivedConstant);
                    relabeled = ((UserSymbol)x.Symbol).Namespace;
                    foreach (var r in relabelStack)
                    {
                        relabeled = table.Relabel(
                            (string)((BaseCnstSymb)r.Args[0].Symbol).Raw,
                            (string)((BaseCnstSymb)r.Args[1].Symbol).Raw,
                            relabeled);
                    }

                    if (!relabeled.TryGetSymbol(((UserSymbol)x.Symbol).Name, out otherSymbol))
                    {
                        throw new Impossible();
                    }

                    if (otherSymbol.Arity == 0)
                    {
                        return index.MkApply(otherSymbol, TermIndex.EmptyArgs, out wasAdded);
                    }

                    i = 0;
                    var args = new Term[otherSymbol.Arity];
                    foreach (var tp in ch)
                    {
                        args[i++] = tp;
                    }

                    return index.MkApply(otherSymbol, args, out wasAdded);
                });
        }

        private static bool OccursCheck(Set<StdTerm> vars, EquivalenceRelation<StdTerm> eqs)
        {
            //// History[v] is false if the occurs check is visiting v.
            //// History[v] is true if the occurs check visited v and did not find violations.
            var history = new Map<StdTerm, bool>(StdTerm.Compare);
            var stack = new Stack<OccursState>();

            StdTerm u, rep;
            OccursState top;
            bool isVisited, violated;
            foreach (var v in vars)
            {
                rep = eqs.GetRepresentative(v);
                if (history.TryFindValue(rep, out isVisited))
                {
                    Contract.Assert(isVisited);
                    continue;
                }

                stack.Push(new OccursState(rep, history, eqs));
                while (stack.Count > 0)
                {
                    top = stack.Peek();
                    if (top.MoveNext(eqs, history, out u, out violated))
                    {
                        stack.Push(new OccursState(u, history, eqs));
                    }
                    else if (violated)
                    {
                        return false;
                    }
                    else
                    {
                        stack.Pop().Finished(history);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Represents a term where the variables are effectively
        /// standardized apart by an extra label. Relabeling does not effect ground terms.
        /// </summary>
        private struct StdTerm
        {
            public Term term;
            public int label;

            public StdTerm(Term term, int label)
            {
                this.term = term;
                this.label = label;
            }

            public static int Compare(StdTerm t1, StdTerm t2)
            {
                var cmp = Term.Compare(t1.term, t2.term);
                if (cmp != 0)
                {
                    return cmp;
                }
                else if (t1.term.Groundness == Groundness.Ground)
                {
                    return 0;
                }
                else
                {
                    return t1.label - t2.label;
                }
            }
        }

        private class OccursState
        {
            private StdTerm rep;
            private IEnumerator<StdTerm> freeOccIt;

            public OccursState(StdTerm rep, Map<StdTerm, bool> history, EquivalenceRelation<StdTerm> eqs)
            {
                this.rep = rep;
                this.freeOccIt = eqs.GetTrackSet(rep, Track_FreeOccurs).GetEnumerator();
                history.Add(rep, false);
            }

            public bool MoveNext(
                EquivalenceRelation<StdTerm> eqs,
                Map<StdTerm, bool> history,
                out StdTerm u, 
                out bool violated)
            {
                bool isVisited;
                while (freeOccIt.MoveNext())
                {
                    u = eqs.GetRepresentative(freeOccIt.Current);
                    if (history.TryFindValue(u, out isVisited))
                    {
                        if (!isVisited)
                        {
                            violated = true;
                            return false;
                        }

                        continue;
                    }

                    violated = false;
                    return true;
                }

                u = default(StdTerm);
                violated = false;
                return false;
            }

            public void Finished(Map<StdTerm, bool> history)
            {
                history[rep] = true;
            }
        }
    }
}
