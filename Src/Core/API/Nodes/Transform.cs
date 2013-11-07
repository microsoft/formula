namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Transform : Node
    {
        private LinkedList<Param> inputs;
        private LinkedList<Param> outputs;
        private LinkedList<ContractItem> contracts;
        private LinkedList<Rule> rules;
        private LinkedList<Node> typeDecls;

        public override int ChildCount
        {
            get 
            {
                return 1 + inputs.Count +
                       outputs.Count +
                       contracts.Count +
                       rules.Count +
                       typeDecls.Count;
            }
        }

        public string Name
        {
            get;
            private set;
        }

        public ImmutableCollection<Rule> Rules
        {
            get;
            private set;
        }

        public ImmutableCollection<Node> TypeDecls
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

        public ImmutableCollection<ContractItem> Contracts
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Transform; }
        }

        internal Transform(Span span, string name)
            : base(span)
        {
            Name = name;
            Config = new Config(span);

            rules = new LinkedList<Rule>();
            Rules = new ImmutableCollection<Rule>(rules);

            typeDecls = new LinkedList<Node>();
            TypeDecls = new ImmutableCollection<Node>(typeDecls);

            inputs = new LinkedList<Param>();
            Inputs = new ImmutableCollection<Param>(inputs);

            outputs = new LinkedList<Param>();
            Outputs = new ImmutableCollection<Param>(outputs);

            contracts = new LinkedList<ContractItem>();
            Contracts = new ImmutableCollection<ContractItem>(contracts);            
        }

        private Transform(Transform n)
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
            var cnode = new Transform(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Inputs = new ImmutableCollection<Param>(TakeClones<Param>(inputs.Count, cenum, out cnode.inputs));
                cnode.Outputs = new ImmutableCollection<Param>(TakeClones<Param>(outputs.Count, cenum, out cnode.outputs));
                cnode.Config = TakeClone<Config>(cenum);
                cnode.Contracts = new ImmutableCollection<ContractItem>(TakeClones<ContractItem>(contracts.Count, cenum, out cnode.contracts));
                cnode.TypeDecls = new ImmutableCollection<Node>(TakeClones<Node>(typeDecls.Count, cenum, out cnode.typeDecls));
                cnode.Rules = new ImmutableCollection<Rule>(TakeClones<Rule>(rules.Count, cenum, out cnode.rules));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Transform(this);
            var occurs = 0;
            cnode.Inputs = new ImmutableCollection<Param>(CloneCollection<Param>(inputs, replace, pos, ref occurs, out cnode.inputs));
            cnode.Outputs = new ImmutableCollection<Param>(CloneCollection<Param>(outputs, replace, pos, ref occurs, out cnode.outputs));
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.Contracts = new ImmutableCollection<ContractItem>(CloneCollection<ContractItem>(contracts, replace, pos, ref occurs, out cnode.contracts));
            cnode.TypeDecls = new ImmutableCollection<Node>(CloneCollection<Node>(typeDecls, replace, pos, ref occurs, out cnode.typeDecls));
            cnode.Rules = new ImmutableCollection<Rule>(CloneCollection<Rule>(rules, replace, pos, ref occurs, out cnode.rules));
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

            var nn = (Transform)n;
            return nn.Name == Name &&
                   nn.inputs.Count == inputs.Count &&
                   nn.outputs.Count == outputs.Count &&
                   nn.contracts.Count == contracts.Count &&
                   nn.typeDecls.Count == typeDecls.Count &&
                   nn.rules.Count == rules.Count;
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

                foreach (var c in contracts)
                {
                    yield return c;
                }

                foreach (var t in typeDecls)
                {
                    yield return t;
                }

                foreach (var r in rules)
                {
                    yield return r;
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

                foreach (var c in contracts)
                {
                    yield return new ChildInfo(c, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                foreach (var t in typeDecls)
                {
                    yield return new ChildInfo(t, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                foreach (var r in rules)
                {
                    yield return new ChildInfo(r, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }
            }
        }

        internal void AddTypeDecl(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsTypeDecl);
            if (addLast)
            {
                typeDecls.AddLast(n);
            }
            else
            {
                typeDecls.AddFirst(n);
            }
        }

        internal void AddRule(Rule n, bool addLast = true)
        {
            Contract.Requires(n != null);
            if (addLast)
            {
                rules.AddLast((Rule)n);
            }
            else
            {
                rules.AddFirst((Rule)n);
            }
        }

        internal void AddContract(ContractItem ci, bool addLast = true)
        {
            Contract.Requires(ci != null);
            Contract.Requires(CanHaveContract(ci.ContractKind));
            if (addLast)
            {
                contracts.AddLast(ci);
            }
            else
            {
                contracts.AddFirst(ci);
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
    }
}
