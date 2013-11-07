namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Model : Node
    {
        private LinkedList<ModRef> includes;

        private LinkedList<ContractItem> contracts;

        private LinkedList<ModelFact> facts;

        public override int ChildCount
        {
            get { return 1 + includes.Count + contracts.Count + facts.Count; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Model; }
        }

        public string Name
        {
            get;
            private set;
        }

        public bool IsPartial
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        public ModRef Domain
        {
            get;
            private set;
        }

        public ImmutableCollection<ModRef> Compositions
        {
            get;
            private set;
        }

        public ComposeKind ComposeKind
        {
            get;
            private set;
        }

        public ImmutableCollection<ContractItem> Contracts
        {
            get;
            private set;
        }

        public ImmutableCollection<ModelFact> Facts
        {
            get;
            private set;
        }

        internal Model(Span span, string name, bool isPartial, ModRef domain, ComposeKind kind)
            : base(span)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            Contract.Requires(domain != null);

            IsPartial = isPartial;
            Name = name;
            Domain = domain;
            ComposeKind = kind;

            includes = new LinkedList<ModRef>();
            Compositions = new ImmutableCollection<ModRef>(includes);

            contracts = new LinkedList<ContractItem>();
            Contracts = new ImmutableCollection<ContractItem>(contracts);

            facts = new LinkedList<ModelFact>();
            Facts = new ImmutableCollection<ModelFact>(facts);
            Config = new Config(span);
        }

        /// <summary>
        /// DO NOT call directly. Only used by the parser.
        /// </summary>
        internal Model(Span span, string name, bool isPartial)
            : base(span)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            IsPartial = isPartial;
            Name = name;
            Domain = new ModRef(span, "?", null, null);
            ComposeKind = ComposeKind.None;

            includes = new LinkedList<ModRef>();
            Compositions = new ImmutableCollection<ModRef>(includes);

            contracts = new LinkedList<ContractItem>();
            Contracts = new ImmutableCollection<ContractItem>(contracts);

            facts = new LinkedList<ModelFact>();
            Facts = new ImmutableCollection<ModelFact>(facts);
            Config = new Config(span);
        }

        private Model(Model n)
            : base(n.Span)
        {
            Name = n.Name;
            ComposeKind = n.ComposeKind;
            IsPartial = n.IsPartial;
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

        public override bool TryGetBooleanAttribute(AttributeKind attribute, out bool value)
        {
            if (attribute == AttributeKind.IsPartial)
            {
                value = IsPartial;
                return true;
            }

            value = false;
            return false;
        }

        public override bool TryGetKindAttribute(AttributeKind attribute, out object value)
        {
            if (attribute == AttributeKind.ComposeKind)
            {
                value = ComposeKind;
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

            if (pred.AttributePredicate == null)
            {
                return true;
            }

            return pred.AttributePredicate(AttributeKind.Name, Name) &&
                   pred.AttributePredicate(AttributeKind.IsPartial, IsPartial) &&
                   pred.AttributePredicate(AttributeKind.ComposeKind, ComposeKind);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Model(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Domain = TakeClone<ModRef>(cenum);
                cnode.Compositions = new ImmutableCollection<ModRef>(TakeClones<ModRef>(includes.Count, cenum, out cnode.includes));
                cnode.Config = TakeClone<Config>(cenum);
                cnode.Contracts = new ImmutableCollection<ContractItem>(TakeClones<ContractItem>(contracts.Count, cenum, out cnode.contracts));
                cnode.Facts = new ImmutableCollection<ModelFact>(TakeClones<ModelFact>(facts.Count, cenum, out cnode.facts));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Model(this);
            var occurs = 0;
            cnode.Domain = CloneField<ModRef>(Domain, replace, pos, ref occurs);
            cnode.Compositions = new ImmutableCollection<ModRef>(CloneCollection<ModRef>(includes, replace, pos, ref occurs, out cnode.includes));
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.Contracts = new ImmutableCollection<ContractItem>(CloneCollection<ContractItem>(contracts, replace, pos, ref occurs, out cnode.contracts));
            cnode.Facts = new ImmutableCollection<ModelFact>(CloneCollection<ModelFact>(facts, replace, pos, ref occurs, out cnode.facts));
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind + (int)ComposeKind;
            unchecked
            {
                v += Name.GetHashCode() + IsPartial.GetHashCode();
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

            var nn = (Model)n;
            return nn.Name == Name &&
                   nn.IsPartial == IsPartial &&
                   nn.ComposeKind == ComposeKind &&
                   nn.includes.Count == includes.Count &&
                   nn.contracts.Count == contracts.Count &&
                   nn.facts.Count == facts.Count;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield return Domain;

                foreach (var cmp in includes)
                {
                    yield return cmp;
                }

                yield return Config;

                foreach (var ci in Contracts)
                {
                    yield return ci;
                }

                foreach (var f in facts)
                {
                    yield return f;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                var index = 0;
                yield return new ChildInfo(Domain, ChildContextKind.Domain, index, index);
                ++index;

                foreach (var cmp in includes)
                {
                    yield return new ChildInfo(cmp, ChildContextKind.Includes, index, index - 1);
                    ++index;
                }

                yield return new ChildInfo(Config, ChildContextKind.AnyChildContext, index, index);
                ++index;

                foreach (var c in contracts)
                {
                    yield return new ChildInfo(c, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                foreach (var f in facts)
                {
                    yield return new ChildInfo(f, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }
            }
        }

        /// <summary>
        /// DO NOT USE - Mutating operation; only called by parser. 
        /// </summary>
        /// <param name="modRef"></param>
        internal void SetDomain(ModRef modRef)
        {
            Contract.Requires(modRef != null);
            Domain = modRef;
        }

        /// <summary>
        /// DO NOT USE - Mutating operation; only called by parser. 
        /// </summary>
        /// <param name="modRef"></param>
        internal void SetCompose(ComposeKind kind)
        {
            Contract.Requires(ComposeKind == ComposeKind.None);
            ComposeKind = kind;
        }

        internal void AddCompose(ModRef modRef, bool addLast = true)
        {
            Contract.Requires(modRef != null);
            Contract.Requires(ComposeKind != ComposeKind.None);
            if (addLast)
            {
                includes.AddLast(modRef);
            }
            else
            {
                includes.AddFirst(modRef);
            }
        }

        internal void AddFact(ModelFact f, bool addLast = true)
        {
            Contract.Requires(f != null);
            if (addLast)
            {
                facts.AddLast(f);
            }
            else
            {
                facts.AddFirst(f);
            }
        }

        internal void AddContract(ContractItem ci, bool addLast = true)
        {
            Contract.Requires(ci != null && CanHaveContract(ci.ContractKind));
            if (addLast)
            {
                contracts.AddLast(ci);
            }
            else
            {
                contracts.AddFirst(ci);
            }
        }
    }
}
