namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class TSystem : Node
    {
        private LinkedList<Param> inputs;
        private LinkedList<Param> outputs;
        private LinkedList<Step> steps;

        public override int ChildCount
        {
            get { return 1 + inputs.Count + outputs.Count + steps.Count; }
        }

        public string Name
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        public ImmutableCollection<Param> Inputs
        {
            get;
            private set;
        }

        public ImmutableCollection<Param> Outputs
        {
            get;
            private set;
        }

        public ImmutableCollection<Step> Steps
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.TSystem; }
        }

        internal TSystem(Span span, string name)
            : base(span)
        {
            Name = name;
            Config = new Config(span);

            inputs = new LinkedList<Param>();
            Inputs = new ImmutableCollection<Param>(inputs);

            outputs = new LinkedList<Param>();
            Outputs = new ImmutableCollection<Param>(outputs);

            steps = new LinkedList<Step>();
            Steps = new ImmutableCollection<Step>(steps);
        }

        private TSystem(TSystem n)
            : base(n.Span)
        {
            Name = n.Name;
            CompilerData = n.CompilerData;
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

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new TSystem(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Inputs = new ImmutableCollection<Param>(TakeClones<Param>(inputs.Count, cenum, out cnode.inputs));
                cnode.Outputs = new ImmutableCollection<Param>(TakeClones<Param>(outputs.Count, cenum, out cnode.outputs));
                cnode.Config = TakeClone<Config>(cenum);
                cnode.Steps = new ImmutableCollection<Step>(TakeClones<Step>(steps.Count, cenum, out cnode.steps));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new TSystem(this);
            var occurs = 0;
            cnode.Inputs = new ImmutableCollection<Param>(CloneCollection<Param>(inputs, replace, pos, ref occurs, out cnode.inputs));
            cnode.Outputs = new ImmutableCollection<Param>(CloneCollection<Param>(outputs, replace, pos, ref occurs, out cnode.outputs));
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.Steps = new ImmutableCollection<Step>(CloneCollection<Step>(steps, replace, pos, ref occurs, out cnode.steps));
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

            var nn = (TSystem)n;
            return nn.Name == Name &&
                   nn.inputs.Count == inputs.Count &&
                   nn.outputs.Count == outputs.Count &&
                   nn.steps.Count == steps.Count;
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

        public override IEnumerable<Node> Children
        {
            get
            {
                foreach (var n in inputs)
                {
                    yield return n;
                }

                foreach (var n in outputs)
                {
                    yield return n;
                }

                yield return Config;

                foreach (var s in steps)
                {
                    yield return s;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                var index = 0;
                foreach (var n in inputs)
                {
                    yield return new ChildInfo(n, ChildContextKind.Inputs, index, index);
                    ++index;
                }

                var relIndex = 0;
                foreach (var n in outputs)
                {
                    yield return new ChildInfo(n, ChildContextKind.Outputs, index, relIndex);
                    ++index;
                    ++relIndex;
                }

                yield return new ChildInfo(Config, ChildContextKind.AnyChildContext, index, index);
                ++index;

                foreach (var s in steps)
                {
                    yield return new ChildInfo(s, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }
            }
        }

        internal void AddInput(Param p, bool addLast = true)
        {
            Contract.Requires(p != null);
            if (addLast)
            {
                inputs.AddLast(p);
            }
            else
            {
                inputs.AddFirst(p);
            }
        }

        internal void AddOutput(Param p, bool addLast = true)
        {
            Contract.Requires(p != null);
            if (addLast)
            {
                outputs.AddLast(p);
            }
            else
            {
                outputs.AddFirst(p);
            }
        }

        internal void AddStep(Step s, bool addLast = true)
        {
            Contract.Requires(s != null);
            if (addLast)
            {
                steps.AddLast(s);
            }
            else
            {
                steps.AddFirst(s);                    
            }
        }
    }
}
