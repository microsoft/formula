namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;

    /// <summary>
    /// Implements a set based on a red-black tree.
    /// Elements are enumerated in sorted order.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set</typeparam>
    public class Set<T> : IEnumerable<T>
    {
        private Map<T, T> setMap;

        public Set(Comparison<T> comparer)
        {
            setMap = new Map<T, T>(comparer);
        }

        public Set(Comparison<T> comparer, IEnumerable<T> initial)
        {
            setMap = new Map<T, T>(comparer);

            foreach (var value in initial)
            {
                Add(value);
            }
        }

        public Comparison<T> Comparer
        {
            get
            {
                return setMap.Comparer;
            }
        }

        public int Count
        {
            get
            {
                return setMap.Count;
            }
        }

        public IEnumerable<T> Reverse
        {
            get
            {
                foreach (var kv in this.setMap.Reverse)
                {
                    yield return kv.Key;
                }
            }
        }

        public static Set<T> Join(Set<T> set1, Set<T> set2)
        {
            var joinedSet = new Set<T>(set1.Comparer);

            foreach (var item in set1)
            {
                joinedSet.Add(item);
            }

            foreach (var item in set2)
            {
                joinedSet.Add(item);
            }

            return joinedSet;
        }

        #region IEnumerable<T> Members

        public IEnumerator<T> GetEnumerator()
        {
            var it = setMap.GetEnumerator();
            while (it.MoveNext())
            {
                yield return it.Current.Key;
            }
        }

        IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion

        public T[] ToArray()
        {
            T[] elements = new T[this.Count];
            int index = 0;
            foreach (var e in this)
            {
                elements[index++] = e;
            }

            return elements;
        }

        /// <summary>
        /// Keeps only those elements where filter evaluates to true.
        /// </summary>
        /// <param name="filter">A filter predicate</param>
        /// <returns>An array</returns>
        public T[] ToArray(Predicate<T> filter)
        {
            int count = 0;
            foreach (var e in this)
            {
                if (filter(e))
                {
                    ++count;
                }
            }

            T[] elements = new T[count];
            int index = 0;
            foreach (var e in this)
            {
                if (filter(e))
                {
                    elements[index++] = e;
                }
            }

            return elements;
        }

        /// <summary>
        /// If set contains the elements S, then any pair (e1,e2) \in S^2
        /// is visited at most once. For every subset of pairs { (e1,e2), (e2,e1) } 
        /// exactly one of these pairs is visited.
        /// </summary>
        /// <param name="visitor">The visitor</param>
        public void VisitPairs(Action<T, T> visitor)
        {
            setMap.VisitKeyPairs(visitor);
        }

        public T GetSomeElement()
        {
            return setMap.GetSomeKey();
        }

        [Pure]
        public bool Contains(T t)
        {
            return setMap.ContainsKey(t);
        }

        public bool Contains(T t, out T tp)
        {
            return setMap.TryFindValue(t, out tp);
        }

        public bool GetOrdinal(T t, out int ordinal)
        {
            return setMap.GetKeyOrdinal(t, out ordinal);
        }

        public T GetSmallestElement()
        {
            Contract.Requires(Count > 0);

            using (var en = GetEnumerator())
            {
                en.MoveNext();
                return en.Current;
            }
        }

        public T GetLargestElement()
        {
            Contract.Requires(Count > 0);

            using (var en = Reverse.GetEnumerator())
            {
                en.MoveNext();
                return en.Current;
            }
        }

        public void Add(T t)
        {
            setMap[t] = t;
        }

        public bool Remove(T t)
        {
            return setMap.Remove(t);
        }

        public void Clear()
        {
            setMap.Clear();
        }

        public Set<T> UnionWith(Set<T> set)
        {
            if (set == this)
            {
                return this;
            }

            foreach (var t in set)
            {
                Add(t);
            }

            return this;
        }

        public Set<T> IntersectWith(Set<T> set)
        {
            var stack = new Stack<T>();
            foreach (var e in this)
            {
                if (!set.Contains(e))
                {
                    stack.Push(e);
                }
            }

            while (stack.Count > 0)
            {
                Remove(stack.Pop());
            }

            return this;
        }

        /// <summary>
        /// Returns true if set contains all the elements of this.
        /// Both sets must use the same comparer.
        /// </summary>
        /// <param name="set"></param>
        /// <returns></returns>
        public bool IsSubsetOf(Set<T> set)
        {
            Contract.Requires(set != null);
            if (set == this)
            {
                return true;
            }
            else if (set.Count < Count)
            {
                return false;
            }

            foreach (var e in this)
            {
                if (!set.Contains(e))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Both sets must use the same comparer.
        /// </summary>
        public bool IsSameSet(Set<T> set)
        {
            Contract.Requires(set != null);
            return IsSameSet(set, (x, y) => Comparer(x, y) == 0);
        }

        /// <summary>
        /// Both sets must use the same comparer.
        /// </summary>
        public bool IsSameSet(Set<T> set, Func<T, T, bool> equalityTester)
        {
            Contract.Requires(set != null);

            if (Count != set.Count)
            {
                return false;
            }

            using (var en1 = GetEnumerator())
            {
                using (var en2 = set.GetEnumerator())             
                {
                    while (en1.MoveNext() && en2.MoveNext())
                    {
                        if (!equalityTester(en1.Current, en2.Current))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public bool HasIntersection(Set<T> set)
        {
            Contract.Requires(set != null);

            if (set.Count < this.Count)
            {
                foreach (var e in set)
                {
                    if (this.Contains(e))
                    {
                        return true;
                    }
                }

                return false;
            }
            else
            {
                foreach (var e in this)
                {
                    if (set.Contains(e))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Remove every element where predicate evaluates to true.
        /// </summary>
        /// <param name="predicate">The predicate to decide which elements to drop</param>
        public void Remove(Predicate<T> predicate)
        {
            List<T> dropList = new List<T>();
            foreach (var e in this)
            {
                if (predicate(e))
                {
                    dropList.Add(e);
                }
            }

            foreach (var e in dropList)
            {
                setMap.Remove(e);
            }
        }
    }
}
