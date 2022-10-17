using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Microsoft.Formula.Common
{
    class GenericMap<TKey, TValue> : SortedDictionary<TKey, TValue>
    {
        public GenericMap() : base()
        {
        }

        public GenericMap(GenericMap<TKey, TValue> other) : base(other, other.Comparer)
        {
        }

        public GenericMap(IComparer<TKey> comparer) : base(comparer)
        {

        }

        public IEnumerable<KeyValuePair<TKey, TValue>> Reverse
        {
            get
            {
                foreach (var item in this.OrderByDescending(k => k.Key))
                {
                    yield return item;
                }

                yield break;
            }
        }

        public void SetExistingKey(TKey key, TValue value)
        {
            if (!this.ContainsKey(key))
            {
                throw new KeyNotFoundException();
            }
            else
            {
                this[key] = value;
            }
        }

        /// <summary>
        /// If k is a key, then returns true and the ordinal of k in the map domain.
        /// Otherwise, returns false.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public bool GetKeyOrdinal(TKey k, out int ordinal)
        {
            IComparer<TKey> comparer = this.Comparer;
            ordinal = 0;
            foreach (var kv in this)
            {
                if (comparer.Compare(kv.Key, k) == 0)
                {
                    return true;
                }

                ++ordinal;
            }

            return false;
        }

        /// <summary>
        /// Start sorted-order enumeration at the element k.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<TKey, TValue>> GetEnumerable(TKey k)
        {
            if (this.Count == 0)
            {
                yield break;
            }

            if (!this.ContainsKey(k))
            {
                throw new Exception("Key not in collection");
            }

            bool started = false;
            var comparer = this.Comparer;
            var enumerator = this.GetEnumerator();
            while (enumerator.MoveNext())
            {
                // improve the efficiency here
                if (!started && comparer.Compare(enumerator.Current.Key, k) == 0)
                {
                    started = true;
                }

                yield return enumerator.Current;
            }
        }

        /// <summary>
        /// If there are one or more keys in the map, then this returns
        /// one of them. Otherwise, default(S) is returned. The key returned
        /// is arbitrary but not random.
        /// </summary>
        /// <returns>Some key or default(S)</returns>
        public TKey GetSomeKey()
        {
            return (this.Count == 0) ? default(TKey) : this.Keys.First();
        }

        public bool TryFindValue(TKey key, out TValue value)
        {
            return base.TryGetValue(key, out value);
        }

        public void VisitKeyPairs(Action<TKey, TKey> visitor)
        {
            int pos = 0;

            SortedDictionary<TKey, TValue>.Enumerator outerEnum = this.GetEnumerator();
            SortedDictionary<TKey, TValue>.Enumerator innerEnum;

            while (outerEnum.MoveNext())
            {
                innerEnum = this.GetEnumerator();
                for (int i = 0; i < pos; i++)
                {
                    innerEnum.MoveNext();
                }

                TKey outerKey = outerEnum.Current.Key;

                while (innerEnum.MoveNext())
                {
                    visitor(outerKey, innerEnum.Current.Key);
                }
            }
        }

        /// <summary>
        /// Gets the largest key/value pair that is less than or equal to k and in the domain
        /// of the map. Returns false if there is no key in the map less than or equal to k.
        /// </summary>
        public bool GetGLB(TKey k, out TKey kGLB, out TValue vGLB)
        {
            bool success = false;
            kGLB = default(TKey);
            vGLB = default(TValue);
            var comparer = this.Comparer;
            Nullable<KeyValuePair<TKey, TValue>> max = null;

            var enumerator = this.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var curr = enumerator.Current;
                if (comparer.Compare(curr.Key, k) <= 0)
                {
                    max = curr;
                    success = true;
                }
                else
                {
                    break;
                }
            }

            if (success)
            {
                kGLB = max.Value.Key;
                vGLB = max.Value.Value;
            }

            return success;
        }

        /// <summary>
        /// Gets the smallest key/value pair that is greater than or equal to k and in the domain
        /// of the map. Returns false if there is no key in the map greater than or equal to k.
        /// </summary>
        public bool GetLUB(TKey k, out TKey kLUB, out TValue vLUB)
        {
            bool success = false;
            kLUB = default(TKey);
            vLUB = default(TValue);
            var comparer = this.Comparer;
            Nullable<KeyValuePair<TKey, TValue>> min = null;

            var enumerator = this.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var curr = enumerator.Current;
                if (comparer.Compare(curr.Key, k) >= 0)
                {
                    min = curr;
                    success = true;
                    break;
                }
            }

            if (success)
            {
                kLUB = min.Value.Key;
                vLUB = min.Value.Value;
            }

            return success;
        }

    }
}