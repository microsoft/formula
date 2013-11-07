namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    public sealed class Folder : Node
    {
        private LinkedList<Folder> subFolders;
        private LinkedList<Program> programs;

        public override int ChildCount
        {
            get { return subFolders.Count + programs.Count; }
        }

        public ImmutableCollection<Folder> SubFolders
        {
            get;
            private set;
        }

        public ImmutableCollection<Program> Programs
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.Folder; }
        }

        public string Name
        {
            get;
            private set;
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                foreach (var f in subFolders)
                {
                    yield return f;
                }

                foreach (var p in programs)
                {
                    yield return p;
                }            
            }
        }

        internal Folder(string name)
            : base(default(Span))
        {
            Contract.Requires(name != null);
            Name = name;

            subFolders = new LinkedList<Folder>();
            SubFolders = new ImmutableCollection<Folder>(subFolders);

            programs = new LinkedList<Program>();
            Programs = new ImmutableCollection<Program>(Programs);
        }

        private Folder(Folder f)
            : base(f.Span)
        {
            Name = f.Name;
            CompilerData = f.CompilerData;
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
            var cnode = new Folder(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.SubFolders = new ImmutableCollection<Folder>(TakeClones<Folder>(subFolders.Count, cenum, out cnode.subFolders));
                cnode.Programs = new ImmutableCollection<Program>(TakeClones<Program>(programs.Count, cenum, out cnode.programs));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new Folder(this);
            int occurs = 0;
            cnode.SubFolders = new ImmutableCollection<Folder>(CloneCollection<Folder>(subFolders, replace, pos, ref occurs, out cnode.subFolders));
            cnode.Programs = new ImmutableCollection<Program>(CloneCollection<Program>(programs, replace, pos, ref occurs, out cnode.programs));
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

            var nn = (Folder)n;
            return nn.subFolders.Count == subFolders.Count &&
                   nn.programs.Count == programs.Count;
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

        internal void AddProgram(Program p)
        {
            Contract.Requires(p != null);
            programs.AddLast(p);
        }

        internal void AddSubFolder(Folder f)
        {
            Contract.Requires(f != null);
            subFolders.AddLast(f);
        }
    }
}
