namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Threading;

    /// <summary>
    /// Implements a map whose domain is fixed upfront. 
    /// Fully thread-safe if constructed with threadSafe flag.
    /// </summary>
    internal class FixedDomMap<S, T> : IEnumerable<KeyValuePair<S, T>>
    {
        private delegate void Writer();
        private KeyValue[] keyValues;
        private Comparison<S> comparer;
        private KeyValueComparer kvComparer;
        private SpinLock rwLock = new SpinLock();

        public bool IsThreadSafe
        {
            get;
            private set;
        }

        public int Count
        {
            get { return keyValues.Length; }
        }

        public IEnumerable<S> Keys
        {
            get
            {
                for (int i = 0; i < keyValues.Length; ++i)
                {
                    yield return keyValues[i].Key;
                }
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                if (IsThreadSafe)
                {
                    for (int i = 0; i < keyValues.Length; ++i)
                    {
                        yield return Read<T>(() => keyValues[i].Value);
                    }
                }
                else
                {
                    for (int i = 0; i < keyValues.Length; ++i)
                    {
                        yield return keyValues[i].Value;
                    }
                }
            }
        }

        public IEnumerator<KeyValuePair<S, T>> Reverse
        {
            get
            {
                if (IsThreadSafe)
                {
                    for (int i = keyValues.Length - 1; i >= 0; --i)
                    {
                        yield return new KeyValuePair<S, T>(keyValues[i].Key, Read<T>(() => keyValues[i].Value));
                    }
                }
                else
                {
                    for (int i = keyValues.Length - 1; i >= 0; --i)
                    {
                        yield return new KeyValuePair<S, T>(keyValues[i].Key, keyValues[i].Value);
                    }
                }
            }
        }

        public Comparison<S> Comparer
        {
            get
            {
                return comparer;
            }
        }

        public T this[S key]
        {
            get
            {
                var index = Array.BinarySearch(keyValues, new KeyValue(key, default(T)), kvComparer);
                if (index < 0)
                {
                    throw new KeyNotFoundException(string.Format("Could not find {0}", key));
                }

                if (IsThreadSafe)
                {
                    return Read<T>(() => keyValues[index].Value);
                }
                else
                {
                    return keyValues[index].Value;
                }
            }

            set
            {
                var index = Array.BinarySearch(keyValues, new KeyValue(key, default(T)), kvComparer);
                if (index < 0)
                {
                    throw new KeyNotFoundException(string.Format("Could not find {0}", key));
                }

                if (IsThreadSafe)
                {
                    Write(() => keyValues[index].Value = value);
                }
                else
                {
                    keyValues[index].Value = value;
                }
            }
        }

        /// <summary>
        /// If initializer is null, then every key initially maps to default(T).
        /// Otherwise every key k initially maps to initializer(k).
        /// 
        /// If isThreadSafe = true, then reads / writes are protected by locks.
        /// Otherwise, reads are thread-safe as long as they are no intervening writes.
        /// </summary>
        public FixedDomMap(Set<S> keys, Func<S, T> initializer, bool isThreadSafe = false)
        {
            Contract.Requires(keys != null);
            keyValues = new KeyValue[keys.Count];
            comparer = keys.Comparer;
            kvComparer = new KeyValueComparer(comparer);
            IsThreadSafe = isThreadSafe;

            int i = 0;
            if (initializer == null)
            {
                foreach (var k in keys)
                {
                    keyValues[i++] = new KeyValue(k, default(T));
                }
            }
            else
            {
                foreach (var k in keys)
                {
                    keyValues[i++] = new KeyValue(k, initializer(k));
                }
            }
        }

        IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public IEnumerator<KeyValuePair<S, T>> GetEnumerator()
        {
            if (IsThreadSafe)
            {
                for (int i = 0; i < keyValues.Length; ++i)
                {
                    yield return new KeyValuePair<S, T>(keyValues[i].Key, Read<T>(() => keyValues[i].Value));
                }
            }
            else
            {
                for (int i = 0; i < keyValues.Length; ++i)
                {
                    yield return new KeyValuePair<S, T>(keyValues[i].Key, keyValues[i].Value);
                }
            }
        }

        public bool ContainsKey(S key)
        {
            return Array.BinarySearch(keyValues, new KeyValue(key, default(T)), kvComparer) >= 0;
        }

        public bool TryFindValue(S key, out T value)
        {
            var index = Array.BinarySearch(keyValues, new KeyValue(key, default(T)), kvComparer);
            if (index < 0)
            {
                value = default(T);
                return false;
            }
            else 
            {
                if (IsThreadSafe)
                {
                    value = Read<T>(() => keyValues[index].Value);
                }
                else
                {
                    value = keyValues[index].Value;
                }

                return true;
            }
        }

        /// <summary>
        /// If there are one or more keys in the map, then this returns
        /// one of them. Otherwise, default(S) is returned. The key returned
        /// is arbitrary but not random.
        /// </summary>
        /// <returns>Some key or default(S)</returns>
        public S GetSomeKey()
        {
            return keyValues.Length == 0 ? default(S) : keyValues[0].Key;
        }

        /// <summary>
        /// Start sorted-order enumeration at the element k.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public IEnumerable<KeyValuePair<S, T>> GetEnumerable(S k)
        {
            var start = Array.BinarySearch(keyValues, new KeyValue(k, default(T)), kvComparer);
            if (start < 0)
            {
                throw new KeyNotFoundException(string.Format("Could not find {0}", k));
            }

            if (IsThreadSafe)
            {
                for (int i = start; i < keyValues.Length; ++i)
                {
                    yield return new KeyValuePair<S, T>(keyValues[i].Key, Read(() => keyValues[i].Value));
                }
            }
            else
            {
                for (int i = start; i < keyValues.Length; ++i)
                {
                    yield return new KeyValuePair<S, T>(keyValues[i].Key, keyValues[i].Value);
                }
            }
        }

        private U Read<U>(Func<U> reader)
        {
            bool gotLock = false;
            try
            {
                rwLock.Enter(ref gotLock);
                return reader();
            }
            finally
            {
                if (gotLock)
                {
                    rwLock.Exit();
                }
            }
        }

        private void Write(Writer writer)
        {
            bool gotLock = false;
            try
            {
                rwLock.Enter(ref gotLock);
                writer();
            }
            finally
            {
                if (gotLock)
                {
                    rwLock.Exit();
                }
            }
        }

        private class KeyValue
        {
            public S Key
            {
                get;
                private set;
            }

            public T Value
            {
                get;
                set;
            }

            public KeyValue(S key, T initValue)
            {
                Key = key;
                Value = initValue;
            }
        }

        private class KeyValueComparer : IComparer<KeyValue>
        {
            private Comparison<S> keyComparer;
            public KeyValueComparer(Comparison<S> keyComparer)
            {
                this.keyComparer = keyComparer;
            }

            public int Compare(KeyValue k1, KeyValue k2)
            {
                return keyComparer(k1.Key, k2.Key);
            }
        }
    }
}
