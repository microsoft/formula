namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class ModApply : Node
    {
        private LinkedList<Node> args;

        public override int ChildCount
        {
            get { return 1 + args.Count; }
        }

        public ModRef Module
        {
            get;
            private set;
        }

        public ImmutableCollection<Node> Args
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.ModApply; }
        }

        internal ModApply(Span span, ModRef modref)
            : base(span)
        {
            Contract.Requires(modref != null);
            Module = modref;
            args = new LinkedList<Node>();
            Args = new ImmutableCollection<Node>(args);
        }

        private ModApply(ModApply n, bool keepCompilerData)
            : base(n.Span)
        {
            CompilerData = keepCompilerData ? n.CompilerData : null;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren, bool keepCompilerData)
        {
            var cnode = new ModApply(this, keepCompilerData);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                cnode.Module = TakeClone<ModRef>(cenum);
                cnode.Args = new ImmutableCollection<Node>(TakeClones<Node>(args.Count, cenum, out cnode.args));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new ModApply(this, true);
            int occurs = 0;
            cnode.Module = CloneField<ModRef>(Module, replace, pos, ref occurs);
            cnode.Args = new ImmutableCollection<Node>(CloneCollection<Node>(args, replace, pos, ref occurs, out cnode.args));
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

            return ((ModApply)n).args.Count == args.Count;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                v += (Module.Rename == null ? 0 : Module.Rename.GetHashCode()) +
                     (Module.Location == null ? 0 : Module.Location.GetHashCode()) +
                     Module.Name.GetHashCode();
            }

            return v;
        }

        public override IEnumerable<Node> Children
        {
            get
            {
                yield return Module;
                foreach (var a in args)
                {
                    yield return a;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                int index = 0;
                yield return new ChildInfo(Module, ChildContextKind.Operator, index, index);
                ++index;

                foreach (var a in args)
                {
                    yield return new ChildInfo(a, ChildContextKind.Args, index, index - 1);
                    ++index;
                }
            }
        }

        internal void AddArg(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsModAppArg);

            if (addLast)
            {
                args.AddLast(n);
            }
            else
            {
                args.AddFirst(n);
            }
        }
    }
}
