namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using Common.Extras;

    /// <summary>
    /// Given a term x, enumerates subterns
    /// x_0 : types[0], ..., x_{n-1} : types[n - 1] such that: 
    /// x_{n-1} [= x_{n-2} [= ... [= x_0 [= x.
    /// </summary>
    internal class SubtermMatcher
    {
        private const int CanMatch = -1;
        private Term[] pattern;

        /// A matching map M: [0, |subTermTypes| - 1] -> Symbol -> P([-1, arity(Symbol) - 1]).
        /// where M(i, s) = -1 if a subterm with outer symbol s is in types[i] and may satisfy the remainder of the pattern.
        /// If M(i, s) > 0, then the ith arg of a subterm with outer symbol s may satisfy the patterns i ... n - 1.
        private Map<int, Map<Term, Set<int>>> matcher = new Map<int, Map<Term, Set<int>>>((x, y) => x - y);

        public bool IsMatchOnlyNewKinds
        {
            get;
            private set;
        }

        public IEnumerable<Term> Pattern
        {         
            get { return pattern; }
        }

        public bool IsSatisfiable
        {
            get;
            private set;
        }

        public SubtermMatcher(TermIndex index, bool onlyNewKinds, Term[] pattern)
        {
            Contract.Requires(index != null);
            Contract.Requires(pattern != null && pattern.Length > 0);
            this.pattern = pattern;
            IsMatchOnlyNewKinds = onlyNewKinds;

            bool wasAdded;
            Term type, intr;
            Term levelTypes = null;
            IsSatisfiable = true;
            Stack<Term> pending = new Stack<Term>();
            Set<Term> pendingOrVisited = new Set<Term>(Term.Compare);
            for (int i = pattern.Length - 1; i >= 0; --i)
            {
                if (!IsSatisfiable)
                {
                    return;
                }

                if (i == pattern.Length - 1)
                {
                    intr = pattern[pattern.Length - 1];
                }
                else if (!index.MkIntersection(pattern[i], levelTypes, out intr))
                {
                    IsSatisfiable = false;
                    return;
                }

                levelTypes = null;
                IsSatisfiable = false;
                pendingOrVisited.Clear();
                foreach (var t in intr.Enumerate(x => x.Symbol == index.TypeUnionSymbol ? x.Args : null))
                {
                    if (t.Symbol == index.TypeUnionSymbol)
                    {
                        continue;
                    }
                    else if (IsPermitted(index, onlyNewKinds, t))
                    {
                        IsSatisfiable = true;
                        GetMatchingSet(i, t).Add(CanMatch);
                        pending.Push(t);
                        pendingOrVisited.Add(t);
                        levelTypes = levelTypes == null ? t : index.MkApply(index.TypeUnionSymbol, new Term[] { t, levelTypes }, out wasAdded);
                    }
                }

                if (!IsSatisfiable)
                {
                    return;
                }

                while (pending.Count > 0)
                {
                    type = pending.Pop();
                    foreach (var tup in index.GetTypeUses(type))
                    {
                        if (tup.Item1.Kind == SymbolKind.ConSymb)
                        {
                            type = index.MkApply(((ConSymb)tup.Item1).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        }
                        else if (tup.Item1.Kind == SymbolKind.MapSymb)
                        {
                            type = index.MkApply(((MapSymb)tup.Item1).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        if (IsPermitted(index, onlyNewKinds, type))
                        {
                            GetMatchingSet(i, type).Add(tup.Item2);
                            if (!pendingOrVisited.Contains(type))
                            {
                                pending.Push(type);
                                pendingOrVisited.Add(type);
                                levelTypes = levelTypes == null ? type : index.MkApply(index.TypeUnionSymbol, new Term[] { type, levelTypes }, out wasAdded);
                            }
                        }
                    }
                }
            }
        }

        private Set<int> GetMatchingSet(int level, Term type)
        {
            Map<Term, Set<int>> levelSets;
            if (!matcher.TryFindValue(level, out levelSets))
            {
                levelSets = new Map<Term, Set<int>>(Term.Compare);
                matcher.Add(level, levelSets);
            }

            Set<int> positions;
            if (!levelSets.TryFindValue(type, out positions))
            {
                positions = new Set<int>((x, y) => x - y);
                levelSets.Add(type, positions);
            }

            return positions;
        }

        public void Debug_Print()
        {
            Console.Write("Pattern: ");
            for (int i = 0; i < pattern.Length; ++i)
            {
                Console.Write("{" + pattern[i].Debug_GetSmallTermString() + "} ");
            }

            Console.WriteLine();
            Console.WriteLine("Is match only new kinds: {0}", IsMatchOnlyNewKinds);
            Console.WriteLine("Is satisfiable: {0}", IsSatisfiable);

            Map<Term, Set<int>> level;
            for (int i = 0; i < pattern.Length; ++i)
            {
                if (!matcher.TryFindValue(i, out level))
                {
                    Console.WriteLine("** Level {0} is unsatifiable", i);
                    continue;
                }

                Console.WriteLine("** Level {0} ", i);
                foreach (var kv in level)
                {
                    if (kv.Value.Contains(CanMatch))
                    {
                        Console.WriteLine("  [{0}]: {1} can match", i, kv.Key.Debug_GetSmallTermString());
                    }

                    foreach (var p in kv.Value)
                    {
                        if (p == CanMatch)
                        {
                            continue;
                        }

                        Console.WriteLine("  [{0}]: {1}.{2} can match", i, kv.Key.Debug_GetSmallTermString(), p);
                    }
                }                
            }
        }

        public static int Compare(Tuple<bool, Term[]> m1, Tuple<bool, Term[]> m2)
        {
            if (m1.Item1 != m2.Item1)
            {
                return !m1.Item1 ? -1 : 1;
            }

            if (m1.Item2.Length != m2.Item2.Length)
            {
                return m1.Item2.Length < m2.Item2.Length ? -1 : 1;
            }

            int cmp;
            for (int i = 0; i < m1.Item2.Length; ++i)
            {
                cmp = Term.Compare(m1.Item2[i], m2.Item2[i]);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }

        private static bool IsPermitted(TermIndex index, bool onlyNewKinds, Term t)
        {
            Contract.Assert(t.Groundness != Groundness.Variable);
            Contract.Assert(t.Symbol.Kind == SymbolKind.BaseCnstSymb ||
                            t.Symbol.Kind == SymbolKind.BaseOpSymb ||
                            t.Symbol.Kind == SymbolKind.BaseSortSymb ||
                            t.Symbol.Kind == SymbolKind.UserCnstSymb ||
                            t.Symbol.Kind == SymbolKind.UserSortSymb);
            Contract.Assert(t.Symbol.Kind != SymbolKind.BaseOpSymb || t.Symbol == index.RangeSymbol);

            if (onlyNewKinds)
            {
                if (t.Symbol.IsDerivedConstant)
                {
                    return false;
                }
                else if (t.Symbol.Kind == SymbolKind.UserSortSymb)
                {
                    var conSymb = ((UserSortSymb)t.Symbol).DataSymbol as ConSymb;
                    return conSymb == null || conSymb.IsNew;
                }
            }

            return true;
        }
    }
}
