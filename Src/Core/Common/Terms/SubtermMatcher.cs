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

        /// <summary>
        /// An app-free union containing the types that can match a given level.
        /// </summary>
        private AppFreeCanUnn[] matchingUnions;

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

        public int NPatterns
        {
            get { return pattern.Length; }
        }

        /// <summary>
        /// A union type representing the values of the lfp that could satisfy this pattern
        /// </summary>
        public Term Trigger
        {
            get;
            private set;
        }

        /// <summary>
        /// The matcher is satisfiable if there exists a value that satifies patterns.
        /// </summary>
        public bool IsSatisfiable
        {
            get;
            private set;
        }

        /// <summary>
        /// The matcher is triggerable if a value can appear in the LFP that can satisfy patterns.
        /// </summary>
        public bool IsTriggerable
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
            matchingUnions = new AppFreeCanUnn[pattern.Length];

            bool wasAdded;
            Term type, intr;
            Term levelTypes = null;
            IsSatisfiable = true;
            IsTriggerable = false;
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
                        if (i == 0 && (t.Symbol.Kind == SymbolKind.UserSortSymb || t.Symbol.IsDerivedConstant))
                        {
                            IsTriggerable = true;
                        }
                    }
                }

                if (!IsSatisfiable)
                {
                    return;
                }
                else
                {
                    matchingUnions[i] = new AppFreeCanUnn(levelTypes);
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
                                if (i == 0 && (type.Symbol.Kind == SymbolKind.UserSortSymb || type.Symbol.IsDerivedConstant))
                                {
                                    IsTriggerable = true;
                                }
                            }
                        }
                    }
                }
            }

            if (IsTriggerable)
            {
                var level0 = matcher[0];
                levelTypes = null;
                foreach (var kv in level0)
                {
                    if (kv.Key.Symbol.Kind == SymbolKind.UserSortSymb || kv.Key.Symbol.IsDerivedConstant)
                    {
                        levelTypes = levelTypes == null ? kv.Key : index.MkApply(index.TypeUnionSymbol, new Term[] { kv.Key, levelTypes }, out wasAdded);
                    }
                }

                Trigger = levelTypes;
            }
        }

        /// <summary>
        /// Private constructor used for cloning
        /// </summary>
        private SubtermMatcher()
        {
        }

        public IEnumerable<Term[]> EnumerateMatches(Term t)
        {
            Contract.Requires(t != null && t.Groundness == Groundness.Ground);
            int crntLevel = 0;
            Term[] crntMatch = new Term[pattern.Length];
            IEnumerator<Term>[] visitors = new IEnumerator<Term>[pattern.Length];
            visitors[0] = EnumerateMatches(t, 0).GetEnumerator();

            while (crntLevel >= 0)
            {
                while (visitors[crntLevel].MoveNext())
                {
                    crntMatch[crntLevel] = visitors[crntLevel].Current;
                    if (crntLevel < pattern.Length - 1)
                    {
                        visitors[crntLevel + 1] = EnumerateMatches(crntMatch[crntLevel], crntLevel + 1).GetEnumerator();
                        ++crntLevel;
                    }
                    else
                    {
                        yield return crntMatch;
                    }
                }

                --crntLevel;
            }
        }

        public SubtermMatcher Clone(TermIndex index)
        {
            var srcIndex = pattern[0].Owner;
            var clone = new SubtermMatcher();
            clone.IsMatchOnlyNewKinds = IsMatchOnlyNewKinds;
            clone.Trigger = Trigger == null ? null : index.MkClone(Trigger);
            clone.IsSatisfiable = IsSatisfiable;
            clone.IsTriggerable = IsTriggerable;

            Map<Term, Set<int>> levelMatches, clonedLevelMatches;
            clone.pattern = new Term[pattern.Length];
            clone.matchingUnions = new AppFreeCanUnn[pattern.Length];
            for (int i = 0; i < pattern.Length; ++i)
            {
                clone.pattern[i] = index.MkClone(pattern[i]);
                if (matchingUnions[i] != null)
                {
                    clone.matchingUnions[i] = new AppFreeCanUnn(index.MkClone(matchingUnions[i].MkTypeTerm(srcIndex)));
                }

                if (matcher.TryFindValue(i, out levelMatches))
                {
                    clonedLevelMatches = new Map<Term, Set<int>>(Term.Compare);
                    foreach (var kv in levelMatches)
                    {
                        clonedLevelMatches.Add(index.MkClone(kv.Key), kv.Value);                    
                    }

                    clone.matcher.Add(i, clonedLevelMatches);
                }
            }

            return clone;
        }

        /// <summary>
        /// Enumerates all subterms of t (possibly including t) that satisfy the pattern at level.
        /// </summary>
        private IEnumerable<Term> EnumerateMatches(Term t, int level)
        {
            foreach (var tp in t.Enumerate(x => EnumerateSubterms(x, level)))
            {
                if (tp.Symbol.Arity == 0)
                {
                    Contract.Assert(tp.Symbol.IsNewConstant || tp.Symbol.IsDerivedConstant);
                    if (matchingUnions[level].AcceptsConstant(tp.Symbol))
                    {
                        yield return tp;
                    }
                }
                else
                {
                    Contract.Assert(tp.Symbol.IsDataConstructor);
                    if (matchingUnions[level].Contains(tp.Symbol.Kind == SymbolKind.ConSymb ? ((ConSymb)tp.Symbol).SortSymbol : ((MapSymb)tp.Symbol).SortSymbol))
                    {
                        yield return tp;
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates the subterms of t that may lead to a match at this level.
        /// </summary>
        private IEnumerable<Term> EnumerateSubterms(Term t, int level)
        {
            if (t.Symbol.Arity == 0)
            {
                yield break;
            }

            Set<int> pos;
            bool wasAdded;
            var levelMatches = matcher[level];
            var sort = t.Symbol.Kind == SymbolKind.ConSymb ? ((ConSymb)t.Symbol).SortSymbol : ((MapSymb)t.Symbol).SortSymbol;
            var typeTerm = t.Owner.MkApply(sort, TermIndex.EmptyArgs, out wasAdded);
            if (!levelMatches.TryFindValue(typeTerm, out pos))
            {
                yield break;
            }

            foreach (var p in pos)
            {
                if (p >= 0)
                {
                    yield return t.Args[p];
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

        public static int Compare(SubtermMatcher m1, SubtermMatcher m2)
        {
            if (m1 == m2)
            {
                return 0;
            }

            if (m1.IsMatchOnlyNewKinds != m2.IsMatchOnlyNewKinds)
            {
                return !m1.IsMatchOnlyNewKinds ? -1 : 1;
            }

            if (m1.pattern.Length != m2.pattern.Length)
            {
                return m1.pattern.Length < m2.pattern.Length ? -1 : 1;
            }

            int cmp;
            for (int i = 0; i < m1.pattern.Length; ++i)
            {
                cmp = Term.Compare(m1.pattern[i], m2.pattern[i]);
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
