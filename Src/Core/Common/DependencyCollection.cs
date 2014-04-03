namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    internal enum DependencyNodeKind { Normal, Cyclic }

    internal class DependencyCollection<S, T>
    {
        private const int CancelFreq = 500;
        private const int NextMark = 0;
        private const int EnqueuedMark = 1;
        private const int OrderMark = 2;
        private const int SCCMark = 3;

        private List<IDependencyNode> topologicalSort = null;
        private List<IDependencyNode> sccs = null;
        private Map<Node, Cycle> cycleMap = null;

        private Comparison<S> resourceComparer;
        private Comparison<T> roleComparer;
        private Map<S, Node> resourceMap;

        public Comparison<S> ResourceComparer
        {
            get { return resourceComparer; }
        }

        public Comparison<T> RoleComparer
        {
            get { return roleComparer; }
        }

        public DependencyCollection(Comparison<S> resourceComparer, Comparison<T> roleComparer)
        {
            this.resourceComparer = resourceComparer;
            this.roleComparer = roleComparer;
            this.resourceMap = new Map<S, Node>(resourceComparer);
        }

        public IEnumerable<IDependencyNode> GetSCCs(out int n, CancellationToken cancel = default(CancellationToken))
        {
            if (sccs == null)
            {
                BuildSCCs(cancel);
            }

            n = sccs == null ? 0 : sccs.Count;
            return sccs;
        }

        public IEnumerable<IDependencyNode> GetTopologicalSort(out int n, CancellationToken cancel = default(CancellationToken))
        {
            if (topologicalSort == null)
            {
                BuildTopologicalSort(cancel);
            }

            n = topologicalSort == null ? 0 : topologicalSort.Count;
            return topologicalSort;
        }

        public void Add(S resource)
        {
            MkNode(resource);
        }

        public void Add(S provider, S requester, T role)
        {
            var np = MkNode(provider);
            var nr = MkNode(requester);
            np.AddProvides(nr, role);
            nr.AddRequest(np, role);
        }

        public bool TryGetNode(S resource, out IDependencyNode node)
        {
            Node internalNode;
            if (!resourceMap.TryFindValue(resource, out internalNode))
            {
                node = null;
                return false;
            }

            node = internalNode;
            return true;
        }

        public void Debug_PrintCollection(
            Func<S, string> toStringRes, 
            Func<T, string> toStringRole,
            bool includeSCCs)
        {
            var idMap = new Map<S, int>(resourceComparer);
            var cycleIdMap = new Map<S, int>(resourceComparer);
            int nodeId = 0;
            
            Console.WriteLine(@"<?xml version='1.0' encoding='utf-8'?>
                              <DirectedGraph xmlns=""http://schemas.microsoft.com/vs/2009/dgml"">
                              <Nodes>");
            foreach (var kv in resourceMap)
            {
                idMap.Add(kv.Key, nodeId);
                Console.WriteLine(
                        @"<Node Id=""{0}"" Label=""{1}"" />",
                        nodeId,
                        toStringRes(kv.Key));
                ++nodeId;
            }

            if (includeSCCs)
            {
                if (sccs == null)
                {
                    BuildSCCs(default(CancellationToken));
                }

                foreach (var n in sccs)
                {
                    if (n.Kind == DependencyNodeKind.Normal)
                    {
                        continue;
                    }

                    Console.WriteLine(
                            @"<Node Id=""{0}"" Group=""Expanded"" />",
                            nodeId);
                    cycleIdMap.Add(n.InternalNodes.GetSomeElement().Resource, nodeId);
                    ++nodeId;
                }
            }

            Console.WriteLine("</Nodes>\n<Links>");
            int srcId, dstId;
            foreach (var kv in resourceMap)
            {
                srcId = idMap[kv.Key];
                foreach (var e in kv.Value.Provides)
                {
                    dstId = idMap[e.Target.Resource];
                    Console.WriteLine(
                        @"<Link Source=""{0}"" Target=""{1}"" Label=""{2}"" />",
                        srcId,
                        dstId,
                        toStringRole(e.Role));
                }
            }

            if (includeSCCs)
            {
                foreach (var n in sccs)
                {
                    if (n.Kind == DependencyNodeKind.Normal)
                    {
                        continue;
                    }

                    srcId = cycleIdMap[n.InternalNodes.GetSomeElement().Resource];
                    foreach (var intr in n.InternalNodes)
                    {
                        Console.WriteLine(
                            @"<Link Source=""{0}"" Target=""{1}"" Category=""Contains"" />",
                            srcId,
                            idMap[intr.Resource]);
                    }
                }
            }

            Console.WriteLine("</Links>");
            if (includeSCCs)
            {
                Console.WriteLine(@"
                  <Categories>
                    <Category Id=""Contains"" Label=""Contains"" IsContainment=""True"" />
                  </Categories>
                  <Properties>
                    <Property Id=""Group"" Label=""Group"" DataType=""Microsoft.VisualStudio.GraphModel.GraphGroupStyle"" />
                    <Property Id=""IsContainment"" DataType=""System.Boolean"" />
                  </Properties>");
            }

            Console.WriteLine("</DirectedGraph>");
        }

        private Node MkNode(S resource)
        {
            Node n;
            if (!resourceMap.TryFindValue(resource, out n))
            {
                n = new Node(resource, this);
                resourceMap.Add(resource, n);
                Invalidate();
            }

            return n;
        }

        private void Invalidate()
        {
            sccs = null;
            topologicalSort = null;
            cycleMap = null;
        }
        
        private void BuildSCCs(System.Threading.CancellationToken cancel)
        {
            sccs = new List<IDependencyNode>();
            cycleMap = new Map<Node, Cycle>((x, y) => resourceComparer(x.Resource, y.Resource));

            foreach (var n in resourceMap.Values)
            {
                n.Marks[EnqueuedMark] = false;
                n.Marks[NextMark] = null;
                n.Marks[OrderMark] = -1;
                n.Marks[SCCMark] = -1;
            }

            var sccStack = new Stack<Node>();
            var dfsStack = new Stack<Node>();
            int depth = 0, workCount = 1;
            foreach (var n in resourceMap.Values)
            {
                BuildSCCs(n, dfsStack, sccStack, ref depth, ref workCount, cancel);
            }

            foreach (var n in sccs)
            {
                if (n.Kind == DependencyNodeKind.Cyclic)
                {
                    ((Cycle)n).Close();
                }
            }
        }

        private void BuildSCCs(Node start, Stack<Node> dfsStack, Stack<Node> sccStack, ref int depth, ref int workCount, CancellationToken cancel)
        {
            if ((bool)start.Marks[EnqueuedMark] == true)
            {
                return;
            }

            sccStack.Push(start);
            dfsStack.Push(start);
            start.Marks[EnqueuedMark] = true;
            start.Marks[OrderMark] = depth;
            start.Marks[SCCMark] = depth;
            start.Marks[NextMark] = start.Provides.GetEnumerator();
            ++depth;
            
            IEnumerator<EndPoint> it;
            while (dfsStack.Count > 0)
            {
                if (workCount % CancelFreq == 0)
                {
                    workCount = 1;
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }
                }
                else
                {
                    ++workCount;
                }

                var n = dfsStack.Peek();
                it = (IEnumerator<EndPoint>)n.Marks[NextMark];
                if (it.MoveNext())
                {
                    var m = (Node)it.Current.Target;
                    if ((bool)m.Marks[EnqueuedMark] == true)
                    {
                        if (m.Marks[NextMark] != null)
                        {
                            n.Marks[SCCMark] = Math.Min((int)n.Marks[SCCMark], (int)m.Marks[OrderMark]);
                        }
                    }
                    else
                    {
                        dfsStack.Push(m);
                        sccStack.Push(m);
                        m.Marks[EnqueuedMark] = true;
                        m.Marks[OrderMark] = depth;
                        m.Marks[SCCMark] = depth;
                        m.Marks[NextMark] = m.Provides.GetEnumerator();
                        ++depth;
                    }
                }
                else
                {
                    dfsStack.Pop();
                    foreach (var ep in n.Provides)
                    {
                        var m = (Node)ep.Target;
                        if (m.Marks[NextMark] != null)
                        {
                            n.Marks[SCCMark] = Math.Min((int)n.Marks[SCCMark], (int)m.Marks[SCCMark]);
                        }
                    }

                    if ((int)n.Marks[OrderMark] == (int)n.Marks[SCCMark])
                    {
                        Node m;
                        if (n.HasLoop || sccStack.Peek() != n)
                        {
                            var cycle = new Cycle(this);
                            sccs.Add(cycle);
                            do
                            {
                                m = sccStack.Pop();
                                cycle.Add(m);
                                m.Marks[NextMark] = null;
                                Contract.Assert((int)m.Marks[SCCMark] >= (int)n.Marks[SCCMark]);
                            }
                            while (m != n);
                        }
                        else
                        {
                            m = sccStack.Pop();
                            Contract.Assert(n == m);
                            m.Marks[NextMark] = null;
                            sccs.Add(m);
                        }
                    }
                }
            }
        }

        private void BuildTopologicalSort(CancellationToken cancel)
        {
            if (sccs == null)
            {
                BuildSCCs(cancel);
            }

            //// The EnqueueMark is true if the node has passed through the sorting queue.
            //// The NextMark is the set of dependencies remaining.

            int workCount = 1;
            topologicalSort = new List<IDependencyNode>();
            var queue = new Queue<IDependencyNode>();
            foreach (var i in sccs)
            {
                if (workCount % CancelFreq == 0)
                {
                    workCount = 1;
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }
                }
                else
                {
                    ++workCount;
                }

                i.Marks[NextMark] = null;
                if (i.Requests.Count == 0)
                {
                    i.Marks[EnqueuedMark] = true;
                    queue.Enqueue(i);
                }
                else
                {
                    i.Marks[EnqueuedMark] = false;
                }
            }

            IDependencyNode d, dp;
            EndPoint lifted;
            Set<EndPoint> liftedRequests;
            while (queue.Count > 0)
            {
                if (workCount % CancelFreq == 0)
                {
                    workCount = 1;
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }
                }
                else
                {
                    ++workCount;
                }

                d = queue.Dequeue();
                topologicalSort.Add(d);
                foreach (var ep in d.Provides)
                {
                    dp = ep.Target.SCCNode;
                    if (((bool)dp.Marks[EnqueuedMark]))
                    {
                        continue;
                    }

                    lifted = new EndPoint(d.SCCNode, ep.Role);
                    if (dp.Marks[NextMark] == null)
                    {
                        liftedRequests = GetLiftedRequests(dp);
                        dp.Marks[NextMark] = liftedRequests;
                    }
                    else
                    {
                        liftedRequests = (Set<EndPoint>)dp.Marks[NextMark];
                    }

                    liftedRequests.Remove(lifted);
                    if (liftedRequests.Count == 0)
                    {
                        dp.Marks[NextMark] = null;
                        dp.Marks[EnqueuedMark] = true;
                        queue.Enqueue(dp);
                    }                    
                }
            }
        }

        /// <summary>
        /// Lifts the set of requests onto the SCCs. Two requests with same roles from nodes in the same SCC
        /// are collapsed to a single request on that SCC.
        /// </summary>
        private Set<EndPoint> GetLiftedRequests(IDependencyNode n)
        {
            Contract.Requires(sccs != null);
            n = n.SCCNode;
            var requests = new Set<EndPoint>(EndPoint.Compare);
            foreach (var m in n.Requests)
            {
                requests.Add(new EndPoint(m.Target.SCCNode, m.Role));
            }

            return requests;
        }

        private int Compare(IDependencyNode n1, IDependencyNode n2)
        {
            if (n1.Kind == DependencyNodeKind.Normal)
            {
                if (n2.Kind == DependencyNodeKind.Cyclic)
                {
                    return -1;
                }

                return resourceComparer(n1.Resource, n2.Resource);
            }
            else if (n2.Kind == DependencyNodeKind.Normal)
            {
                if (n1.Kind == DependencyNodeKind.Cyclic)
                {
                    return 1;
                }

                return resourceComparer(n1.Resource, n2.Resource);
            }

            Contract.Assert(sccs != null);
            Contract.Assert(n1.InternalNodes.Count > 0 && n2.InternalNodes.Count > 0);
            return resourceComparer(n1.InternalNodes.GetSomeElement().Resource,
                                    n2.InternalNodes.GetSomeElement().Resource);
        }

        internal interface IDependencyNode
        {
            DependencyCollection<S, T> Owner
            {
                get;
            }

            DependencyNodeKind Kind
            {
                get;
            }

            S Resource
            {
                get;
            }
            
            Set<EndPoint> Provides
            {
                get;
            }

            Set<EndPoint> Requests
            {
                get;
            }

            Set<EndPoint> InternalEnds
            {
                get;
            }

            Set<IDependencyNode> InternalNodes
            {
                get;
            }

            IDependencyNode SCCNode
            {
                get;
            }

            /// <summary>
            /// DO NOT MODIFY
            /// </summary>
            object[] Marks
            {
                get;
            }

            bool ContainsResource(S resource);
        }

        private class Node : IDependencyNode
        {
            private static readonly Set<EndPoint> EmptyEndSet = new Set<EndPoint>(EndPoint.Compare);
            private static readonly Set<IDependencyNode> EmptyNodeSet = new Set<IDependencyNode>((x, y) => 0);

            private object[] marks = new object[] { null, null, null, null };

            public DependencyNodeKind Kind
            {
                get { return DependencyNodeKind.Normal; }
            }

            public DependencyCollection<S, T> Owner
            {
                get;
                private set;
            }

            public S Resource
            {
                get;
                private set;
            }

            public Set<EndPoint> Provides
            {
                get;
                private set;
            }

            public Set<EndPoint> Requests
            {
                get;
                private set;
            }

            public Set<EndPoint> InternalEnds
            {
                get{ return EmptyEndSet; }
            }

            public Set<IDependencyNode> InternalNodes
            {
                get { return EmptyNodeSet; }
            }

            public object[] Marks
            {
                get { return marks; }
            }

            public bool HasLoop
            {
                get;
                private set;
            }

            public IDependencyNode SCCNode
            {
                get
                {
                    Contract.Assert(Owner.cycleMap != null);
                    Cycle sccNode;
                    if (Owner.cycleMap.TryFindValue(this, out sccNode))
                    {
                        return sccNode;
                    }
                    else
                    {
                        return this;
                    }
                }
            }

            public void AddProvides(Node requester, T role)
            {
                var ep = new EndPoint(requester, role);
                if (!Provides.Contains(ep))
                {
                    if (requester == this)
                    {
                        HasLoop = true;
                    }

                    Provides.Add(ep);
                    Owner.Invalidate();
                }
            }

            public void AddRequest(Node provider, T role)
            {
                var ep = new EndPoint(provider, role);
                if (!Requests.Contains(ep))
                {
                    if (provider == this)
                    {
                        HasLoop = true;
                    }

                    Requests.Add(ep);
                    Owner.Invalidate();
                }
            }

            public bool ContainsResource(S resource)
            {
                return Owner.resourceComparer(resource, Resource) == 0;
            }

            public Node(S resource, DependencyCollection<S, T> owner)
            {
                Resource = resource;
                Owner = owner;
                Provides = new Set<EndPoint>(EndPoint.Compare);
                Requests = new Set<EndPoint>(EndPoint.Compare);
            }            
        }

        private class Cycle : IDependencyNode
        {
            private object[] marks = new object[] { -1, -1, -1, -1 };

            public DependencyNodeKind Kind
            {
                get { return DependencyNodeKind.Cyclic; }
            }

            public object[] Marks
            {
                get { return marks; }
            }

            public DependencyCollection<S, T> Owner
            {
                get;
                private set;
            }

            public S Resource
            {
                get { throw new InvalidOperationException(); }
            }

            public Set<EndPoint> Provides
            {
                get;
                private set;
            }

            public Set<EndPoint> Requests
            {
                get;
                private set;
            }

            public Set<EndPoint> InternalEnds
            {
                get;
                private set;
            }
 
            public Set<IDependencyNode> InternalNodes
            {
                get;
                private set;
            }

            public IDependencyNode SCCNode
            {
                get
                {
                    Contract.Assert(Owner.cycleMap != null);
                    return this;
                }
            }

            public Cycle(DependencyCollection<S, T> owner)
            {
                Owner = owner;
                Provides = new Set<EndPoint>(EndPoint.Compare);
                Requests = new Set<EndPoint>(EndPoint.Compare);
                InternalEnds = new Set<EndPoint>(EndPoint.Compare);
                InternalNodes = new Set<IDependencyNode>(owner.Compare);
            }

            public void Add(Node n)
            {
                Owner.cycleMap.Add(n, this);
                InternalNodes.Add(n);
            }

            public void Close()
            {
                Cycle c;
                foreach (var n in InternalNodes)
                {
                    foreach (var ep in n.Requests)
                    {
                        if (Owner.cycleMap.TryFindValue((Node)ep.Target, out c) && c == this)
                        {
                            InternalEnds.Add(ep);
                        }
                        else
                        {
                            Requests.Add(ep);
                        }
                    }

                    foreach (var ep in n.Provides)
                    {
                        if (!Owner.cycleMap.TryFindValue((Node)ep.Target, out c) || c != this)
                        {
                            Provides.Add(ep);
                        }
                    }
                }
            }

            public bool ContainsResource(S resource)
            {
                Node n;
                if (!Owner.resourceMap.TryFindValue(resource, out n))
                {
                    return false;
                }

                return InternalNodes.Contains(n);
            }
        }

        internal struct EndPoint
        {
            private IDependencyNode node;
            private T role;

            public IDependencyNode Target
            {
                get { return node; }
            }

            public T Role
            {
                get { return role; }
            }

            public EndPoint(IDependencyNode node, T role)
            {
                this.node = node;
                this.role = role;
            }

            public static int Compare(EndPoint e1, EndPoint e2)
            {
                Contract.Assert(e1.node.Owner == e2.node.Owner);
                var cmp = e1.Target.Owner.roleComparer(e1.Role, e2.Role);
                if (cmp != 0)
                {
                    return cmp;
                }

                return e1.node.Owner.Compare(e1.node, e2.node);
            }
        }
    }
}
