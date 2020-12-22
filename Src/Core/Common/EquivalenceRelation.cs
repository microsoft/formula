namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Maintains an equivalence relation of type T. Sets of T values can be maintained with the 
    /// equivalence classes. If two classes are merged, then these sets are also merged. 
    /// </summary>
    internal class EquivalenceRelation<T>
    {
        private static readonly T[] EmptyTrackData = new T[0];

        private int nextNodeId = 0;
        private Comparison<T> comparer;
        private Map<T, Node> nodes;
        private Set<Node> classes = new Set<Node>(Node.Compare);

        private Map<Node, T>[] trackMaps;
        private Map<Node, Set<T>>[] setTrackMaps;

        public int NSetTrackers
        {
            get;
            private set;
        }

        public int NTrackers
        {
            get;
            private set;
        }

        public EquivalenceRelation(Comparison<T> comparer, int nTrackers, int nSetTrackers)
        {
            Contract.Requires(nSetTrackers >= 0);
            this.comparer = comparer;
            nodes = new Map<T, Node>(comparer);

            trackMaps = new Map<Node, T>[nTrackers];
            NTrackers = nTrackers;
            for (int i = 0; i < nTrackers; ++i)
            {
                trackMaps[i] = new Map<Node, T>(Node.Compare);
            }  

            setTrackMaps = new Map<Node, Set<T>>[nSetTrackers];
            NSetTrackers = nSetTrackers;
            for (int i = 0; i < nSetTrackers; ++i)
            {
                setTrackMaps[i] = new Map<Node, Set<T>>(Node.Compare);
            }          
        }

        /// <summary>
        /// Presents a representative from each equivalence class.
        /// </summary>
        public IEnumerable<T> Representatives
        {
            get
            {
                foreach (var n in classes)
                {
                    yield return n.Element;
                }
            }
        }

        public IEnumerable<T> Elements
        {
            get
            {
                return nodes.Keys;
            }
        }

        /// <summary>
        /// Returns true if the element is in the relation
        /// </summary>
        public bool Contains(T element)
        {
            return this.nodes.ContainsKey(element);
        }

        /// <summary>
        /// Adds an element to the relation, placing it in its
        /// own equivalence class.
        /// </summary>
        /// <param name="element"></param>
        public void Add(T element)
        {
            GetNode(element);
        }

        /// <summary>
        /// From henceforth the value y will be tracked in setId with the 
        /// equivalence class containing x.
        /// </summary>
        public void AddToTrackSet(T x, int setId, T y)
        {
            Contract.Requires(setId >= 0 && setId < NSetTrackers);
            var n = GetNode(x).Find();
            Set<T> track;
            if (!setTrackMaps[setId].TryFindValue(n, out track))
            {
                track = new Set<T>(comparer);
                setTrackMaps[setId].Add(n, track);
            }

            track.Add(y);
        }

        /// <summary>
        /// If the equivalence class containing x doesn't have any data for trackerId, 
        /// then this tracker is set to y. Otherwise the tracked value remains unchanged.
        /// The method returns the value of the tracker.
        /// </summary>
        public T SetTracker(T x, int trackerId, T y)
        {
            Contract.Requires(trackerId >= 0 && trackerId < NTrackers);
            var n = GetNode(x).Find();

            T crnt;
            if (!trackMaps[trackerId].TryFindValue(n, out crnt))
            {
                trackMaps[trackerId].Add(n, y);
                return y;
            }

            return crnt;
        }

        public IEnumerable<T> GetTrackSet(T x, int setId)
        {
            Contract.Requires(setId >= 0 && setId < NSetTrackers);
            Node n;
            if (!nodes.TryFindValue(x, out n))
            {
                return EmptyTrackData;
            }

            n = n.Find();
            Set<T> track;
            if (!setTrackMaps[setId].TryFindValue(n, out track))
            {
                return EmptyTrackData;
            }

            return track;
        }

        /// <summary>
        /// If x has data set for trackerId, then returns true and gives the value.
        /// Otherwise returns false.
        /// </summary>
        public bool GetTracker(T x, int trackerId, out T value)
        {
            Contract.Requires(trackerId >= 0 && trackerId < NSetTrackers);
            Node n;
            if (!nodes.TryFindValue(x, out n))
            {
                value = default(T);
                return false;
            }

            n = n.Find();
            return trackMaps[trackerId].TryFindValue(n, out value);
        }

        public bool IsInTrackSet(T x, int setId, T y)
        {
            Contract.Requires(setId >= 0 && setId < NSetTrackers);
            Node n;
            if (!nodes.TryFindValue(x, out n))
            {
                return false;
            }

            n = n.Find();
            Set<T> track;
            if (!setTrackMaps[setId].TryFindValue(n, out track))
            {
                return false;
            }

            return track.Contains(y);
        }

        /// <summary>
        /// Combines two equivalence classes
        /// </summary>
        public void Equate(T x, T y)
        {
            Node.Union(GetNode(x), GetNode(y), this);
        }

        /// <summary>
        /// Returns some element from the equivalence class containing x.
        /// Returns x if it has not been explicitly added to the relation, but does not add it.
        /// </summary>
        public T GetRepresentative(T x)
        {
            Node nx;
            if (!this.nodes.TryFindValue(x, out nx))
            {
                return x;
            }
            else
            {
                return nx.Find().Element;
            }
        }

        /// <summary>
        /// Returns true if x and y are equal under the relation.
        /// </summary>
        public bool Equals(T x, T y)
        {
            Node nx, ny;

            if (comparer(x, y) == 0)
            {
                return true;
            }
            else if (!this.nodes.TryFindValue(x, out nx))
            {
                return false;
            }
            else if (!this.nodes.TryFindValue(y, out ny))
            {
                return false;
            }
            else
            {
                return nx.Find() == ny.Find();
            }
        }

        public Map<T, Set<T>> GetBindings()
        {
            T rep;
            Set<T> part;
            var partitions = new Map<T, Set<T>>(comparer);
            foreach (var kv in nodes)
            {
                rep = kv.Value.Find().Element;
                if (!partitions.TryFindValue(rep, out part))
                {
                    part = new Set<T>(comparer);
                    partitions.Add(rep, part);
                }

                part.Add(kv.Key);
            }

            return partitions;
        }

        public void Debug_PrintRelation(Func<T, string> toString)
        {
            T rep;
            Set<T> part;
            var partitions = new Map<T, Set<T>>(comparer);
            foreach (var kv in nodes)
            {
                rep = kv.Value.Find().Element;
                if (!partitions.TryFindValue(rep, out part))
                {
                    part = new Set<T>(comparer);
                    partitions.Add(rep, part);
                }

                part.Add(kv.Key);
            }

            T t;
            foreach (var kv in partitions)
            {
                Console.Write("Class { ");
                foreach (var e in kv.Value)
                {
                    Console.Write("{0} ", toString(e));
                }

                Console.WriteLine("}");
                for (int i = 0; i < trackMaps.Length; ++i)
                {
                    Console.Write("  Tracker {0}: ", i);
                    if (GetTracker(kv.Key, i, out t))
                    {
                        Console.WriteLine(toString(t));
                    }
                    else
                    {
                        Console.WriteLine("NONE");
                    }
                }

                for (int i = 0; i < setTrackMaps.Length; ++i)
                {
                    Console.Write("  Tracked Set {0}: {{ ", i);
                    foreach (var e in GetTrackSet(kv.Key, i))
                    {
                        Console.Write("{0} ", toString(e));
                    }

                    Console.WriteLine("}");
                }
            }
        }

        private Node GetNode(T element)
        {
            Node n;
            if (!nodes.TryFindValue(element, out n))
            {
                n = new Node(element, nextNodeId++);
                this.classes.Add(n);
                this.nodes.Add(element, n);
            }

            return n;
        }

        protected class Node
        {
            private T element;
            private Node parent;
            private int rank;
            private int uID;

            public Node(T element, int uID)
            {
                rank = 0;
                parent = this;

                this.uID = uID;
                this.element = element;
            }

            public T Element
            {
                get { return element; }
                set { element = value; }
            }

            public int Rank
            {
                get { return this.rank; }
            }

            public static Node Union(Node x, Node y, EquivalenceRelation<T> erel)
            {
                var px = x.Find();
                var py = y.Find();
                if (px == py)
                {
                    return px;
                }

                Node root = null;
                Node child = null;
                if (px.rank > py.rank)
                {
                    root = py.parent = px;
                    child = py;
                }
                else
                {
                    root = px.parent = py;
                    child = px;
                    if (px.rank == py.rank)
                    {
                        py.rank++;
                    }
                }

                erel.classes.Remove(child);
                Set<T> trackRoot, trackChild;
                for (int i = 0; i < erel.setTrackMaps.Length; ++i)
                {
                    erel.setTrackMaps[i].TryFindValue(child, out trackChild);
                    if (trackChild == null)
                    {
                        continue;
                    }

                    erel.setTrackMaps[i].TryFindValue(root, out trackRoot);
                    if (trackRoot == null)
                    {
                        erel.setTrackMaps[i].Add(root, trackChild);
                    }
                    else if (trackChild.Count <= trackRoot.Count)
                    {
                        trackRoot.UnionWith(trackChild);
                    }
                    else
                    {
                        trackChild.UnionWith(trackRoot);
                        erel.setTrackMaps[i][root] = trackChild;
                    }

                    erel.setTrackMaps[i].Remove(child);
                }

                bool isTrackedR, isTrackedC;
                T trackR, trackC;
                for (int i = 0; i < erel.trackMaps.Length; ++i)
                {
                    isTrackedC = erel.trackMaps[i].TryFindValue(child, out trackC);
                    if (!isTrackedC)
                    {
                        continue;
                    }

                    isTrackedR = erel.trackMaps[i].TryFindValue(root, out trackR);
                    if (!isTrackedR)
                    {
                        erel.trackMaps[i].Add(root, trackC);
                    }

                    erel.trackMaps[i].Remove(child);
                }

                return root;
            }

            public Node Find()
            {
                if (this.parent == this)
                {
                    return this;
                }

                this.parent = this.parent.Find();
                return this.parent;
            }

            public static int Compare(Node x, Node y)
            {
                return x.uID - y.uID;
            }
        }
    }
}