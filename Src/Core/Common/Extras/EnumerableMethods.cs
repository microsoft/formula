namespace Microsoft.Formula.Common.Extras
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;

    internal static class EnumerableMethods
    {
        public static IEnumerable<T> GetEnumerable<T>(T t1)
        {
            yield return t1;
        }

        public static IEnumerable<T> GetEnumerable<T>(T t1, T t2)
        {
            yield return t1;
            yield return t2;
        }

        public static IEnumerable<T> GetEnumerable<T>(T t1, T t2, T t3)
        {
            yield return t1;
            yield return t2;
            yield return t3;
        }

        public static IEnumerable<T> GetEnumerable<T>(T t1, T t2, T t3, T t4)
        {
            yield return t1;
            yield return t2;
            yield return t3;
            yield return t4;
        }

        public static IEnumerable<T> GetEnumerable<T>(this T[] array)
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

        public static T[] ToArray<T>(this IEnumerable<T> enm, int length)
        {
            Contract.Requires(length >= 0);
            if (enm == null)
            {
                Contract.Assert(length == 0);
                return new T[0];
            }

            int i = 0;
            var arr = new T[length];
            foreach (var e in enm)
            {
                arr[i++] = e; 
            }

            Contract.Assert(i == length);
            return arr;
        }

        [Pure]
        public static bool IsEmpty<T>(this IEnumerable<T> e)
        {
            if (e == null)
            {
                return true;
            }

            using (var it = e.GetEnumerator())
            {
                return !it.MoveNext();
            }
        }

        public static bool IsSeveral<T>(this IEnumerable<T> e)
        {
            if (e == null)
            {
                return true;
            }

            using (var it = e.GetEnumerator())
            {
                return it.MoveNext() && it.MoveNext();                       
            }
        }

        public static bool IsOne<T>(this IEnumerable<T> e)
        {
            if (e == null)
            {
                return true;
            }

            using (var it = e.GetEnumerator())
            {
                return it.MoveNext() && !it.MoveNext();
            }
        }

        public static IEnumerable<T> Truncate<T>(this IEnumerable<T> e, int truncAmount)
        {
            Contract.Requires(e != null);
            int stop = e.Count<T>() - truncAmount;
            if (stop > 0)
            {
                int i = 0;
                foreach (var m in e)
                {
                    yield return m;
                    if ((++i) >= stop)
                    {
                        break;
                    }
                }
            }
        }

        public static IEnumerable<T> Append<T>(this IEnumerable<T> enm, T last)
        {
            foreach (var e in enm)
            {
                yield return e;
            }

            yield return last;               
        }

        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> enm, T t)
        {
            yield return t;
            if (enm != null)
            {
                foreach (var tp in enm)
                {
                    yield return tp;
                }
            }
        }

        public static bool Or(this IEnumerable<bool> values)
        {
            if (values == null)
            {
                return false;
            }

            foreach (var v in values)
            {
                if (v)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool And(this IEnumerable<bool> values)
        {
            if (values == null)
            {
                return true;
            }

            foreach (var v in values)
            {
                if (!v)
                {
                    return false;
                }
            }

            return true;
        }

        public static void ReverseArray<T>(this T[] arr)
        {
            if (arr == null || arr.Length < 2)
            {
                return;
            }

            T tmp;
            for (int i = 0; i < arr.Length / 2; ++i)
            {
                tmp = arr[i];
                arr[i] = arr[arr.Length - 1 - i];
                arr[arr.Length - 1 - i] = tmp;                
            }
        }

        public static void ReverseArray<T>(this T[] arr, int endingIndex)
        {
            Contract.Requires(arr != null && endingIndex < arr.Length);
            if (endingIndex < 2)
            {
                return;
            }

            T tmp;
            var swapLen = (endingIndex + 1) / 2;
            for (int i = 0; i < swapLen; ++i)
            {
                tmp = arr[i];
                arr[i] = arr[endingIndex - i];
                arr[endingIndex - i] = tmp;
            }
        }

        /// <summary>
        /// Prints an IEnumerable using T.ToString() and separates elements by sep
        /// </summary>
        public static string ToString<T>(this IEnumerable<T> elements, string sep, Func<T, string> toString = null)
        {
            if (elements == null)
            {
                return string.Empty;
            }

            using (var it = elements.GetEnumerator())
            {
                string str;
                if (it.MoveNext())
                {
                    str = toString == null ? it.Current.ToString() : toString(it.Current);
                }
                else
                {
                    return string.Empty;
                }

                while (it.MoveNext())
                {
                    str += sep + (toString == null ? it.Current.ToString() : toString(it.Current));
                }

                return str;
            }
        }

        /// <summary>
        /// Defines a lexicographic order between arrays using compare
        /// </summary>
        public static int LexCompare<T>(T[] arr1, T[] arr2, Comparison<T> compare)
        {
            Contract.Requires(arr1 != null && arr2 != null && compare != null);
            int cmp;
            var n = Math.Min(arr1.Length, arr2.Length);
            for (int i = 0; i < n; ++i)
            {
                if ((cmp = compare(arr1[i], arr2[i])) != 0)
                {
                    return cmp;
                }
            }

            return arr1.Length - arr2.Length;
        }
    }
}
