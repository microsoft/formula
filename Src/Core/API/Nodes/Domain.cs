namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class Domain : Node
    {
        private LinkedList<ModRef> compositions;
        private LinkedList<Rule> rules;
        private LinkedList<Node> typeDecls;
        private LinkedList<ContractItem> conforms;

        public override int ChildCount
        {
            get { return compositions.Count + rules.Count + typeDecls.Count + conforms.Count + 1; }
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

        public ImmutableCollection<ContractItem> Conforms
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
            get { return NodeKind.Domain; }
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

        internal Domain(Span span, string name, ComposeKind kind)
            : base(span)
        {
            Contract.Requires(name != null);
            Name = name;
            ComposeKind = kind;
            Config = new Config(span);

            compositions = new LinkedList<ModRef>();
            Compositions = new ImmutableCollection<ModRef>(compositions);

            rules = new LinkedList<Rule>();
            Rules = new ImmutableCollection<Rule>(rules);

            typeDecls = new LinkedList<Node>();
            TypeDecls = new ImmutableCollection<Node>(typeDecls);

            conforms = new LinkedList<ContractItem>();
            Conforms = new ImmutableCollection<ContractItem>(conforms);
        }

        private Domain(Domain d)
            : base(d.Span)
        {
            ComposeKind = d.ComposeKind;
            Name = d.Name;
            CompilerData = d.CompilerData;
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

            return pred.AttributePredicate(AttributeKind.ComposeKind, ComposeKind) &&
                   pred.AttributePredicate(AttributeKind.Name, Name);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new Domain(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Compositions = new ImmutableCollection<ModRef>(TakeClones<ModRef>(compositions.Count, cenum, out cnode.compositions));
                cnode.Config = TakeClone<Config>(cenum);
                cnode.TypeDecls = new ImmutableCollection<Node>(TakeClones<Node>(typeDecls.Count, cenum, out cnode.typeDecls));
                cnode.Rules = new ImmutableCollection<Rule>(TakeClones<Rule>(rules.Count, cenum, out cnode.rules));
                cnode.Conforms = new ImmutableCollection<ContractItem>(TakeClones<ContractItem>(conforms.Count, cenum, out cnode.conforms));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Domain(this);
            var occurs = 0;
            cnode.Compositions = new ImmutableCollection<ModRef>(CloneCollection<ModRef>(compositions, replace, pos, ref occurs, out cnode.compositions));
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.TypeDecls = new ImmutableCollection<Node>(CloneCollection<Node>(typeDecls, replace, pos, ref occurs, out cnode.typeDecls));
            cnode.Rules = new ImmutableCollection<Rule>(CloneCollection<Rule>(rules, replace, pos, ref occurs, out cnode.rules));
            cnode.Conforms = new ImmutableCollection<ContractItem>(CloneCollection<ContractItem>(conforms, replace, pos, ref occurs, out cnode.conforms));
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

            var nn = (Domain)n;
            return nn.Name == Name &&
                   nn.ComposeKind == ComposeKind &&
                   nn.compositions.Count == compositions.Count &&
                   nn.conforms.Count == conforms.Count &&
                   nn.typeDecls.Count == typeDecls.Count &&
                   nn.rules.Count == rules.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind + (int)ComposeKind;
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
                foreach (var m in compositions)
                {
                    yield return m;
                }

                yield return Config;

                foreach (var t in typeDecls)
                {
                    yield return t;
                }

                foreach (var r in rules)
                {
                    yield return r;
                }

                foreach (var c in conforms)
                {
                    yield return c;
                }
            }
        }

        internal void AddConforms(ContractItem ci, bool addLast = true)
        {
            Contract.Requires(ci != null);
            Contract.Requires(ci.ContractKind == ContractKind.ConformsProp);
            if (addLast)
            {
                conforms.AddLast(ci);
            }
            else
            {
                conforms.AddFirst(ci);
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

        internal void AddCompose(ModRef modRef, bool addLast = true)
        {
            Contract.Requires(modRef != null);
            Contract.Requires(ComposeKind != ComposeKind.None);
            if (addLast)
            {
                compositions.AddLast(modRef);
            }
            else
            {
                compositions.AddFirst(modRef);
            }
        }
    }
}
