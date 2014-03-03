namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Update : Node
    {
        private LinkedList<Id> states;

        private LinkedList<ModApply> choices;

        public override int ChildCount
        {
            get { return states.Count + choices.Count + (Config == null ? 0 : 1); }
        }

        public ImmutableCollection<ModApply> Choices
        {
            get;
            private set;
        }

        public ImmutableCollection<Id> States
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Update; }
        }
        
        internal Update(Span span)
            : base(span)
        {
            choices = new LinkedList<ModApply>();
            Choices = new ImmutableCollection<ModApply>(choices);

            states = new LinkedList<Id>();
            States = new ImmutableCollection<Id>(states);
        }

        private Update(Update n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Update(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.States = new ImmutableCollection<Id>(TakeClones<Id>(states.Count, cenum, out cnode.states));
                cnode.Choices = new ImmutableCollection<ModApply>(TakeClones<ModApply>(choices.Count, cenum, out cnode.choices));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Update(this, true);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.States = new ImmutableCollection<Id>(CloneCollection<Id>(states, replace, pos, ref occurs, out cnode.states));
            cnode.Choices = new ImmutableCollection<ModApply>(CloneCollection<ModApply>(choices, replace, pos, ref occurs, out cnode.choices));
            return cnode;
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
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

            var nn = (Update)n;
            return nn.states.Count == states.Count &&
                   nn.choices.Count == choices.Count;
        }

        internal void AddChoice(ModApply n, bool addLast = true)
        {
            Contract.Requires(n != null);
            if (addLast)
            {
                choices.AddLast(n);
            }
            else
            {
                choices.AddFirst(n);
            }
        }

        internal void AddState(Id n, bool addLast = true)
        {
            Contract.Requires(n != null);
            if (addLast)
            {
                states.AddLast(n);
            }
            else
            {
                states.AddFirst(n);
            }
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var s in states)
                {
                    yield return s;
                }

                foreach (var c in choices)
                {
                    yield return c;
                }
            }
        }
    }
}
