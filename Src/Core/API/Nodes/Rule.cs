namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Rule : Node
    {
        private LinkedList<Node> heads;
        private LinkedList<Body> bodies;

        public override int ChildCount
        {
            get { return heads.Count + bodies.Count + (Config == null ? 0 : 1); }
        }

        public bool IsFact
        {
            get { return bodies.Count == 0; }
        }

        public Config Config
        {
            get;
            private set;
        }

        public ImmutableCollection<Node> Heads
        {
            get;
            private set;
        }

        public ImmutableCollection<Body> Bodies
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Rule; }
        }

        internal Rule(Span span)
            : base(span)
        {
            heads = new LinkedList<Node>();
            Heads = new ImmutableCollection<Node>(heads);

            bodies = new LinkedList<Body>();
            Bodies = new ImmutableCollection<Body>(bodies);
        }

        private Rule(Rule n)
            : base(n.Span)
        {
            CompilerData = n.CompilerData;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Rule(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Heads = new ImmutableCollection<Node>(TakeClones<Node>(heads.Count, cenum, out cnode.heads));
                cnode.Bodies = new ImmutableCollection<Body>(TakeClones<Body>(bodies.Count, cenum, out cnode.bodies));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Rule(this);
            int occurs = 0;

            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Heads = new ImmutableCollection<Node>(CloneCollection<Node>(heads, replace, pos, ref occurs, out cnode.heads));
            cnode.Bodies = new ImmutableCollection<Body>(CloneCollection<Body>(bodies, replace, pos, ref occurs, out cnode.bodies));
            return cnode;
        }

        internal override bool IsLocallyEquivalent(Node n)
        {
            if (n == this)
            {
                return true;
            }
            else if (n.NodeKind != NodeKind)
            {
                return false;
            }

            var nn = (Rule)n;
            return nn.bodies.Count == bodies.Count &&
                   nn.heads.Count == heads.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        internal void AddHead(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsFuncOrAtom);
            if (addLast)
            {
                heads.AddLast(n);
            }
            else
            {
                heads.AddFirst(n);
            }
        }

        internal void AddBody(Body n, bool addLast = true)
        {
            Contract.Requires(n != null);
            if (addLast)
            {
                bodies.AddLast(n);
            }
            else
            {
                bodies.AddFirst(n);
            }
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var n in heads)
                {
                    yield return n;
                }

                foreach (var n in bodies)
                {
                    yield return n;
                }
            }
        }
    }
}
