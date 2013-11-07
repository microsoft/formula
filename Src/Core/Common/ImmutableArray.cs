namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class ImmutableArray<T> : IEnumerable<T>
    {
        private T[] array;

        public int Length
        {
            get { return array == null ? 0 : array.Length; }
        }

        public T this[int index]
        {
            get
            {
                if (array == null || index < 0 || index >= array.Length)
                {
                    throw new IndexOutOfRangeException();
                }

                return array[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (array == null)
            {
                yield break;
            }

            for (int i = 0; i < array.Length; ++i)
            {
                yield return array[i];
            }
        }

        internal ImmutableArray(T[] array)
        {
            this.array = array;    
        }
    }
}
