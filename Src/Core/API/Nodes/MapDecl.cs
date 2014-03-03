namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class MapDecl : Node
    {
        private LinkedList<Field> dom;

        private LinkedList<Field> cod;

        public override int ChildCount
        {
            get { return dom.Count + cod.Count + (Config == null ? 0 : 1); }
        }

        public Config Config
        {
            get;
            private set;
        }

        public ImmutableCollection<Field> Dom
        {
            get;
            private set;
        }

        public ImmutableCollection<Field> Cod
        {
            get;
            private set;
        }

        public string Name
        {
            get;
            private set;
        }

        public MapKind MapKind
        {
            get;
            private set;
        }

        public bool IsPartial
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.MapDecl; }
        }

        internal MapDecl(Span span, string name, MapKind mapKind, bool isPartial)
            : base(span)
        {
            Contract.Requires(name != null);

            MapKind = mapKind;
            IsPartial = isPartial;
            Name = name;

            dom = new LinkedList<Field>();
            Dom = new ImmutableCollection<Field>(dom);

            cod = new LinkedList<Field>();
            Cod = new ImmutableCollection<Field>(cod);
        }

        private MapDecl(MapDecl n, bool keepCompilerData)
            : base(n.Span)
        {
            MapKind = n.MapKind;
            IsPartial = n.IsPartial;
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
            if (attribute == AttributeKind.MapKind)
            {
                value = MapKind;
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

            return pred.AttributePredicate(AttributeKind.MapKind, MapKind) &&
                   pred.AttributePredicate(AttributeKind.IsPartial, IsPartial) &&
                   pred.AttributePredicate(AttributeKind.Name, Name);
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new MapDecl(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Config != null)
                {
                    cnode.Config = TakeClone<Config>(cenum);
                }

                cnode.Dom = new ImmutableCollection<Field>(TakeClones<Field>(dom.Count, cenum, out cnode.dom));
                cnode.Cod = new ImmutableCollection<Field>(TakeClones<Field>(cod.Count, cenum, out cnode.cod));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new MapDecl(this, true);
            int occurs = 0;
            if (Config != null)
            {
                cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            }

            cnode.Dom = new ImmutableCollection<Field>(CloneCollection<Field>(dom, replace, pos, ref occurs, out cnode.dom));
            cnode.Cod = new ImmutableCollection<Field>(CloneCollection<Field>(cod, replace, pos, ref occurs, out cnode.cod));
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

            var nn = (MapDecl)n;
            return nn.Name == Name &&
                   nn.IsPartial == IsPartial &&
                   nn.MapKind == MapKind &&
                   nn.dom.Count == dom.Count &&
                   nn.cod.Count == cod.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Name.GetHashCode() + (int)MapKind + IsPartial.GetHashCode();
            }

            return v;
        }

        internal void SetConfig(Config conf)
        {
            Contract.Requires(conf != null);
            Config = conf;
        }

        internal void AddDomField(Field fld, bool addLast = true)
        {
            Contract.Requires(fld != null);
            if (addLast)
            {
                dom.AddLast(fld);
            }
            else
            {
                dom.AddFirst(fld);
            }
        }

        internal void AddCodField(Field fld, bool addLast = true)
        {
            Contract.Requires(fld != null);
            if (addLast)
            {
                cod.AddLast(fld);
            }
            else
            {
                cod.AddFirst(fld);
            }
        }

        /// <summary>
        /// Should only be called by the parser. Do not change
        /// partiality after a node has been returned through the API
        /// </summary>
        internal void ChangePartiality(bool isPartial)
        {
            IsPartial = isPartial;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                if (Config != null)
                {
                    yield return Config;
                }

                foreach (var fld in dom)
                {
                    yield return fld;
                }

                foreach (var fld in cod)
                {
                    yield return fld;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                int index = 0;
                if (Config != null)
                {
                    yield return new ChildInfo(Config, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }

                foreach (var fld in dom)
                {
                    yield return new ChildInfo(fld, ChildContextKind.Dom, index, index);
                    ++index;
                }

                int relIndex = 0;
                foreach (var fld in cod)
                {
                    yield return new ChildInfo(fld, ChildContextKind.Cod, index, relIndex);
                    ++index;
                    ++relIndex;
                }
            }
        }
    }
}
