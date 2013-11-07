namespace Microsoft.Formula.Common
{
    using System;

    internal class MutableTuple<T1>
    {
        public T1 Item1
        {
            get;
            set;
        }

        public MutableTuple()
        {

        }

        public MutableTuple(T1 item1)
        {
            Item1 = item1;
        }
    }

    internal class MutableTuple<T1, T2>
    {
        public T1 Item1
        {
            get;
            set;
        }

        public T2 Item2
        {
            get;
            set;
        }

        public MutableTuple()
        {

        }

        public MutableTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    internal class MutableTuple<T1, T2, T3>
    {
        public T1 Item1
        {
            get;
            set;
        }

        public T2 Item2
        {
            get;
            set;
        }

        public T3 Item3
        {
            get;
            set;
        }

        public MutableTuple()
        {

        }

        public MutableTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }
    }
}
