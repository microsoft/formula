namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Reflection;

    public class ImmutableCollection<T> : IEnumerable<T>
    {
        private enum EnumKind { LinkedList, List, Set, Unknown };

        private EnumKind kind;

        private IEnumerable<T> elements;
                     
        private MethodInfo countGetter;

        public int Count
        {
            get { return GetCount(); }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<T> GetEnumerator()
        {
            if (elements == null)
            {
                yield break;
            }

            foreach (var e in elements)
            {
                yield return e;
            }
        }

        internal ImmutableCollection(IEnumerable<T> elements)
        {
            this.elements = elements;
            if (elements == null)
            {
                countGetter = null;
                return;
            }

            kind = EnumKind.Unknown;
            if (elements is LinkedList<T>)
            {
                kind = EnumKind.LinkedList;
            }
            else if (elements is List<T>)
            {
                kind = EnumKind.List;
            }
            else if (elements is Set<T>)
            {
                kind = EnumKind.Set;
            }

            if (kind != EnumKind.Unknown)
            {
                countGetter = null;
                return;
            }

            var countProps = elements.GetType().FindMembers(
                MemberTypes.Property,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                FilterCounters,
                null);

            if (countProps.Length == 1)
            {
                countGetter = ((PropertyInfo)countProps[0]).GetGetMethod();
            }
            else
            {
                countGetter = null;
            }
        }

        private static bool FilterCounters(MemberInfo m, object criteria)
        {
            if (m.Name != "Count" && m.Name != "Length")
            {
                return false;
            }

            var getter = ((PropertyInfo)m).GetGetMethod(false);
            if (getter == null)
            {
                return false;
            }
            else if (!getter.ReturnType.Equals(typeof(int)))
            {
                return false;
            }
            else if (getter.GetParameters().Length != 0)
            {
                return false;
            }

            return true;
        }

        private int GetCount()
        {
            if (elements == null)
            {
                return 0;
            }

            switch (kind)
            {
                case EnumKind.LinkedList:
                    return ((LinkedList<T>)elements).Count;
                case EnumKind.List:
                    return ((List<T>)elements).Count;
                case EnumKind.Set:
                    return ((Set<T>)elements).Count;
                case EnumKind.Unknown:
                    return countGetter == null ? elements.Count<T>() : (int)countGetter.Invoke(elements, null);
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
