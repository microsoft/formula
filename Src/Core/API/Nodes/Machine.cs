namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Machine : Node
    {
        private LinkedList<Param> inputs;
        private LinkedList<Step> bootSeq;
        private LinkedList<Update> initials;
        private LinkedList<Update> nexts;
        private LinkedList<Property> properties;
        private LinkedList<ModRef> stateDomains;

        public override int ChildCount
        {
            get 
            {
                return 1 + inputs.Count +
                       bootSeq.Count +
                       initials.Count +
                       nexts.Count +
                       properties.Count +
                       stateDomains.Count;
            }
        }

        public ImmutableCollection<ModRef> StateDomains
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public ImmutableCollection<Param> Inputs
        {
            get;
            private set;
        }
             
        public ImmutableCollection<Step> BootSequence
        {
            get;
            private set;
        }

        public ImmutableCollection<Update> Initials
        {
            get;
            private set;
        }

        public ImmutableCollection<Update> Nexts
        {   
            get;
            private set;
        }

        public ImmutableCollection<Property> Properties
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Machine; }
        }

        internal Machine(Span span, string name)
            : base(span)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            stateDomains = new LinkedList<ModRef>();
            StateDomains = new ImmutableCollection<ModRef>(stateDomains);

            Name = name;
            Config = new Config(span);

            inputs = new LinkedList<Param>();
            Inputs = new ImmutableCollection<Param>(inputs);

            bootSeq = new LinkedList<Step>();
            BootSequence = new ImmutableCollection<Step>(bootSeq);

            initials = new LinkedList<Update>();
            Initials = new ImmutableCollection<Update>(initials);

            nexts = new LinkedList<Update>();
            Nexts = new ImmutableCollection<Update>(nexts);

            properties = new LinkedList<Property>();
            Properties = new ImmutableCollection<Property>(properties);
        }

        private Machine(Machine n, bool keepCompilerData)
            : base(n.Span)
        {
            Name = n.Name;
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            if (attribute == AttributeKind.Name)
            {
                value = Name;
                return true;
            }

            value = null;
            return false;
        }

        protected override bool EvalAtom(ASTQueries.NodePredAtom pred, ChildContextKind context, int absPos, int relPos)
        {
            if (!base.EvalAtom(pred, context, absPos, relPos))
            {
                return false;
            }

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.Name, Name);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Machine(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Inputs = new ImmutableCollection<Param>(TakeClones<Param>(inputs.Count, cenum, out cnode.inputs));
                cnode.StateDomains = new ImmutableCollection<ModRef>(TakeClones<ModRef>(stateDomains.Count, cenum, out cnode.stateDomains));
                cnode.Config = TakeClone<Config>(cenum);
                cnode.BootSequence = new ImmutableCollection<Step>(TakeClones<Step>(bootSeq.Count, cenum, out cnode.bootSeq));
                cnode.Initials = new ImmutableCollection<Update>(TakeClones<Update>(initials.Count, cenum, out cnode.initials));
                cnode.Nexts = new ImmutableCollection<Update>(TakeClones<Update>(nexts.Count, cenum, out cnode.nexts));
                cnode.Properties = new ImmutableCollection<Property>(TakeClones<Property>(properties.Count, cenum, out cnode.properties));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Machine(this, true);
            int occurs = 0;
            cnode.Inputs = new ImmutableCollection<Param>(CloneCollection<Param>(inputs, replace, pos, ref occurs, out cnode.inputs));
            cnode.StateDomains = new ImmutableCollection<ModRef>(CloneCollection<ModRef>(stateDomains, replace, pos, ref occurs, out cnode.stateDomains));
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.BootSequence = new ImmutableCollection<Step>(CloneCollection<Step>(bootSeq, replace, pos, ref occurs, out cnode.bootSeq));
            cnode.Initials = new ImmutableCollection<Update>(CloneCollection<Update>(initials, replace, pos, ref occurs, out cnode.initials));
            cnode.Nexts = new ImmutableCollection<Update>(CloneCollection<Update>(nexts, replace, pos, ref occurs, out cnode.nexts));
            cnode.Properties = new ImmutableCollection<Property>(CloneCollection<Property>(properties, replace, pos, ref occurs, out cnode.properties));
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Name.GetHashCode();
            }

            return v;
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

            var nn = (Machine)n;
            return nn.Name == Name &&
                   nn.inputs.Count == inputs.Count &&
                   nn.stateDomains.Count == stateDomains.Count &&
                   nn.bootSeq.Count == bootSeq.Count &&
                   nn.initials.Count == initials.Count &&
                   nn.nexts.Count == nexts.Count &&
                   nn.properties.Count == properties.Count;
        }

        internal void AddInput(Param param, bool addLast = true)
        {
            Contract.Requires(param != null);
            if (addLast)
            {
                inputs.AddLast(param);
            }
            else
            {
                inputs.AddFirst(param);
            }
        }

        internal void AddBootStep(Step step, bool addLast = true)
        {
            Contract.Requires(step != null);
            if (addLast)
            {
                bootSeq.AddLast(step);
            }
            else
            {
                bootSeq.AddFirst(step);
            }
        }

        internal void AddUpdate(Update update, bool isInitUpdate, bool addLast = true)
        {
            Contract.Requires(update != null);

            if (isInitUpdate)
            {
                if (addLast)
                {
                    initials.AddLast(update);
                }
                else
                {
                    initials.AddFirst(update);
                }
            }
            else
            {
                if (addLast)
                {
                    nexts.AddLast(update);
                }
                else
                {
                    nexts.AddFirst(update);
                }
            }
        }

        internal void AddProperty(Property prop, bool addLast = true)
        {
            Contract.Requires(prop != null);
            if (addLast)
            {
                properties.AddLast(prop);
            }
            else
            {
                properties.AddFirst(prop);
            }
        }

        internal void AddStateDomain(ModRef mod, bool addLast = true)
        {
            Contract.Requires(mod != null);
            if (addLast)
            {
                stateDomains.AddLast(mod);
            }
            else
            {
                stateDomains.AddFirst(mod);
            }
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                foreach (var input in inputs)
                {
                    yield return input;
                }

                foreach (var sd in StateDomains)
                {
                    yield return sd;
                }

                yield return Config;

                foreach (var s in bootSeq)
                {
                    yield return s;
                }

                foreach (var i in initials)
                {
                    yield return i;
                }

                foreach (var n in nexts)
                {
                    yield return n;
                }

                foreach (var p in properties)
                {
                    yield return p;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                int index = 0;
                foreach (var input in inputs)
                {
                    yield return new ChildInfo(input, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                foreach (var sd in stateDomains)
                {
                    yield return new ChildInfo(sd, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                yield return new ChildInfo(Config, ChildContextKind.AnyChildContext, index, index);
                ++index;

                foreach (var s in bootSeq)
                {
                    yield return new ChildInfo(s, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                int relIndex = 0;
                foreach (var i in initials)
                {
                    yield return new ChildInfo(i, ChildContextKind.Initials, index, relIndex);
                    ++index;
                    ++relIndex;
                }

                relIndex = 0;
                foreach (var n in nexts)
                {
                    yield return new ChildInfo(n, ChildContextKind.Nexts, index, relIndex);
                    ++index;
                    ++relIndex;
                }

                foreach (var p in properties)
                {
                    yield return new ChildInfo(p, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }
            }
        }
    }
}
