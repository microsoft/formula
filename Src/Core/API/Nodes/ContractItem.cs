namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class ContractItem : Node
    {
        private LinkedList<Node> specification;

        public override int ChildCount
        {
            get { return specification.Count + (Config == null ? 0 : 1); }
        }

        public ContractKind ContractKind
        {
            get;
            private set;
        }

        public ImmutableCollection<Node> Specification
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
            get { return NodeKind.ContractItem; }
        }

        /// <summary>
        /// Only use if this is a non-cardinality contract.
        /// </summary>
        internal IEnumerable<Body> Bodies
        {
            get
            {
                foreach (var s in specification)
                {
                    Contract.Assert(s.NodeKind == NodeKind.Body);
                    yield return ((Body)s);
                }
            }
        }

        internal ContractItem(Span span, ContractKind contractKind)
            : base(span)
        {
            ContractKind = contractKind;
            specification = new LinkedList<Node>();
            Specification = new ImmutableCollection<Node>(specification);
        }

        private ContractItem(ContractItem n)
            : base(n.Span)
        {
            ContractKind = n.ContractKind;
            CompilerData = n.CompilerData;
        }

        public override bool TryGetKindAttribute(AttributeKind attribute, out object value)
        {
            if (attribute == AttributeKind.ContractKind)
            {
                value = ContractKind;
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

            return pred.AttributePredicate == null ? true : pred.AttributePredicate(AttributeKind.ContractKind, ContractKind);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new ContractItem(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Specification = new ImmutableCollection<Node>(TakeClones<Node>(specification.Count, cenum, out cnode.specification));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new ContractItem(this);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Specification = new ImmutableCollection<Node>(CloneCollection<Node>(specification, replace, pos, ref occurs, out cnode.specification));
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

            var nn = (ContractItem)n;
            return nn.ContractKind == ContractKind && nn.specification.Count == specification.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            return (int)NodeKind + (int)ContractKind;
        }

        internal void AddSpecification(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsContractSpec);
            if (addLast)
            {
                specification.AddLast(n);
            }
            else
            {
                specification.AddFirst(n);
            }
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var s in specification)
                {
                    yield return s;
                }
            }
        }
    }
}
