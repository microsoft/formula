namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common.Extras;

    internal class ByteMap<S, T>
    {
        private const int MaxBucketSize = 8;

        private Func<S, int, byte> keySlicer;

        public int KeySize
        {
            get;
            private set;
        }

        public ByteMap(int keySize, Func<S, int, byte> keySlicer)
        {
            Contract.Requires(keySize > 0 && keySlicer != null);
            KeySize = keySize;
            this.keySlicer = keySlicer;
        }

        private static int GetNextBinSize(int currentSize)
        {
            switch (currentSize)
            {
                case 0:
                    return 7;
                case 7:
                    return 11;
                case 11:
                    return 13;
                case 13:
                    return 17;
                case 17:
                    return 19;
                case 19:
                    return 23;
                case 23:
                    return 29;
                case 29:
                    return 31;
                case 31:
                    return 37;
                case 37:
                    return 41;
                case 41:
                    return 43;
                case 43:
                    return 47;
                case 47:
                    return 53;
                case 53:
                    return 59;
                case 59:
                    return 61;
                case 61:
                    return 67;
                case 67:
                    return 71;
                case 71:
                    return 71;
                default:
                    throw new NotImplementedException();
            }
        }

        private abstract class Node
        {
            public byte Chunk
            {
                get;
                private set;
            }

            public Node(byte chunk)
            {
                Chunk = chunk;
            }
        }

        private class InternalNode : Node
        {
            private LinkedList<Node>[] buckets;
                       
            public InternalNode(byte chunk)
                : base(chunk)
            {
                buckets = new LinkedList<Node>[GetNextBinSize(0)];
            }

            public Node Find(byte chunk)
            {
                var bucket = buckets[chunk % buckets.Length];
                if (bucket == null)
                {
                    return null;
                }

                foreach (var n in bucket)
                {
                    if (n.Chunk == chunk)
                    {
                        return n;
                    }
                }

                return null;
            }

            public void Add(Node n)
            {
                var bucket = buckets[n.Chunk % buckets.Length];
                if (bucket == null)
                {
                    bucket = new LinkedList<Node>();
                    bucket.AddLast(n);
                    buckets[n.Chunk % buckets.Length] = bucket;
                    return;
                }
                else if (bucket.Count + 1 < MaxBucketSize)
                {
                    bucket.AddLast(n);
                    return;
                }

                bucket.AddLast(n);
                var oldBuckets = buckets;
                buckets = new LinkedList<Node>[GetNextBinSize(oldBuckets.Length)];
                for (int i = 0; i < oldBuckets.Length; ++i)
                {
                    if ((bucket = buckets[i]) == null)
                    {
                        continue;
                    }

                    foreach (var m in bucket)
                    {
                        Add(m);
                    }
                }
            }
        }

        private class LeafNode : Node
        {
            public T Value
            {
                get;
                private set;
            }

            public LeafNode(byte chunk, T value)
                : base(chunk)
            {
                Value = value;
            }
        }
    }
}
