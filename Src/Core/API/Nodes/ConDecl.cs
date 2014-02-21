namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class ConDecl : Node
    {
        private LinkedList<Field> fields;

        public override int ChildCount
        {
            get { return fields.Count + (Config == null ? 0 : 1); }
        }

        public ImmutableCollection<Field> Fields
        {
            get;
            private set;
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

        public bool IsNew
        {
            get;
            private set;
        }

        public bool IsSub
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.ConDecl; }
        }

        internal ConDecl(Span span, string name, bool isNew, bool isSub)
            : base(span)
        {
            Contract.Requires(name != null);
            Contract.Requires(!isSub || !isNew);

            IsNew = isNew;
            IsSub = isSub;
            Name = name;
            fields = new LinkedList<Field>();
            Fields = new ImmutableCollection<Field>(fields);
        }

        private ConDecl(ConDecl n)
            : base(n.Span)
        {
            IsNew = n.IsNew;
            IsSub = n.IsSub;
            Name = n.Name;
            CompilerData = n.CompilerData;
        }

        public override bool TryGetBooleanAttribute(AttributeKind attribute, out bool value)
        {
            if (attribute == AttributeKind.IsNew)
            {
                value = IsNew;
                return true;
            }
            else if (attribute == AttributeKind.IsSub)
            {
                value = IsSub;
                return true;
            }

            value = false;
            return false;
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

            if (pred.AttributePredicate == null)
            {
                return true;
            }

            return pred.AttributePredicate(AttributeKind.Name, Name) &&
                   pred.AttributePredicate(AttributeKind.IsNew, IsNew) &&
                   pred.AttributePredicate(AttributeKind.IsSub, IsSub);
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new ConDecl(this);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Fields = new ImmutableCollection<Field>(CloneCollection<Field>(fields, replace, pos, ref occurs, out cnode.fields));
            return cnode;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new ConDecl(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Fields = new ImmutableCollection<Field>(TakeClones<Field>(fields.Count, cenum, out cnode.fields));
            }

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

            var nn = (ConDecl)n;
            return nn.IsNew == IsNew && nn.IsSub == IsSub && nn.Name == Name && nn.fields.Count == fields.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Name.GetHashCode() + IsNew.GetHashCode() + IsSub.GetHashCode();
            }

            return v;
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var f in fields)
                {
                    yield return f;
                }
            }
        }

        internal void AddField(Field fld, bool addLast = true)
        {
            Contract.Requires(fld != null);
            if (addLast)
            {
                fields.AddLast(fld);
            }
            else
            {
                fields.AddFirst(fld);
            }
        }
    }
}
