namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    public sealed class Program : Node
    {
        private LinkedList<Node> modules;

        public override int ChildCount
        {
            get { return 1 + modules.Count; }
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Program; }
        }

        public ImmutableCollection<Node> Modules
        {
            get;
            private set;
        }

        public ProgramName Name
        {
            get;
            private set;
        }

        public Config Config
        {
            get;
            private set;
        }

        internal Program(ProgramName name)
        {
            Contract.Requires(name != null);

            Name = name;
            Config = new Config(default(Span));
            modules = new LinkedList<Node>();
            Modules = new ImmutableCollection<Node>(modules);
        }

        private Program(Program n, bool keepCompilerData)
            : base(n.Span)
        {
            Name = n.Name;
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        public override bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            if (attribute == AttributeKind.Name)
            {
                value = Name.ToString();
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

        public bool Save()
        {
            throw new NotImplementedException();
        }

        public bool SaveAs(string filename)
        {
            throw new NotImplementedException();
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new Program(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Config = TakeClone<Config>(cenum);
                cnode.Modules = new ImmutableCollection<Node>(TakeClones<Node>(modules.Count, cenum, out cnode.modules));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Program(this, true);
            int occurs = 0;
            cnode.Config = CloneField<Config>(Config, replace, pos, ref occurs);
            cnode.Modules = new ImmutableCollection<Node>(CloneCollection<Node>(modules, replace, pos, ref occurs, out cnode.modules));
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += Name.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.SafeUnescaped).ToLowerInvariant().GetHashCode();
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

            var nn = (Program)n;
            return nn.Name.Equals(Name) &&
                   nn.modules.Count == modules.Count;
        }

        internal void AddModule(Node n, bool addLast = true)
        {
            Contract.Requires(n != null);
            Contract.Requires(n.IsModule);
            if (addLast)
            {
                modules.AddLast(n);
            }
            else
            {
                modules.AddFirst(n);
            }
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield return Config;

                foreach (var m in modules)
                {
                    yield return m;
                }
            }
        }
    }
}
