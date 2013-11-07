namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;

    public sealed class FuncTerm : Node
    {
        private LinkedList<Node> args;

        public override int ChildCount
        {
            get { return args.Count + (Function is Node ? 1 : 0); }
        }

        public object Function
        {
            get;
            private set;
        }

        public override NodeKind NodeKind
        {
            get { return NodeKind.FuncTerm; }
        }

        public ImmutableCollection<Node> Args
        {
            get;
            private set;
        }

        internal FuncTerm(Span span, Id cons)
            : base(span)
        {
            Contract.Requires(cons != null);
            OpKind kind;

            if (ASTQueries.ASTSchema.Instance.TryGetOpKind(cons.Name, out kind))
            {
                Function = kind;
            }
            else
            {
                Function = cons;
            }

            args = new LinkedList<Node>();
            Args = new ImmutableCollection<Node>(args);
        }

        internal FuncTerm(Span span, OpKind op)
            : base(span)
        {
            Function = op;
            args = new LinkedList<Node>();
            Args = new ImmutableCollection<Node>(args);
        }

        private FuncTerm(FuncTerm n)
            : base(n.Span)
        {
            if (n.Function is OpKind)
            {
                Function = (OpKind)n.Function;
            }

            CompilerData = n.CompilerData;
        }

        internal override Node DeepClone(IEnumerable<Node> clonedChildren)
        {
            var cnode = new FuncTerm(this);
            cnode.cachedHashCode = this.cachedHashCode;
            using (var cenum = clonedChildren.GetEnumerator())
            {
                if (Function is Id)
                {
                    cnode.Function = TakeClone<Id>(cenum);
                }

                cnode.Args = new ImmutableCollection<Node>(TakeClones<Node>(args.Count, cenum, out cnode.args));
            }

            return cnode;
        }

        internal override Node ShallowClone(Node replace, int pos)
        {
            var cnode = new FuncTerm(this);
            int occurs = 0;

            if (Function is Id)
            {
                cnode.Function = CloneField<Id>((Id)Function, replace, pos, ref occurs);
            }
            
            cnode.Args = new ImmutableCollection<Node>(CloneCollection<Node>(args, replace, pos, ref occurs, out cnode.args));
            return cnode;
        }

        protected override int GetDetailedNodeKindHash()
        {
            var v = (int)NodeKind;
            unchecked
            {
                if (Function is Id)
                {
                    v += ((Id)Function).Name.GetHashCode();
                }
                else
                {
                    v += Function.GetHashCode();
                }
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

            var nn = (FuncTerm)n;
            if (nn.args.Count != args.Count)
            {
                return false;
            }
            else if (Function is OpKind)
            {
                if (!(nn.Function is OpKind))
                {
                    return false;
                }

                return ((OpKind)Function) == ((OpKind)nn.Function);
            }
            else 
            {
                var nid = nn.Function as Id;
                return nid != null && ((Id)Function).Name == nid.Name;
            }
        }

        public override IEnumerable<Node> Children
        {
            get 
            {
                if (Function is Id)
                {
                    yield return (Id)Function;
                }

                foreach (var n in args)
                {
                    yield return n;
                }
            }
        }

        public override IEnumerable<ChildInfo> ChildrenInfo
        {
            get
            {
                int index = 0;
                if (Function is Id)
                {
                    yield return new ChildInfo((Id)Function, ChildContextKind.Operator, index, index);
                    ++index;

                    foreach (var a in args)
                    {
                        yield return new ChildInfo(a, ChildContextKind.Args, index, index - 1);
                        ++index;
                    }
                }
                else
                {
                    foreach (var a in args)
                    {
                        yield return new ChildInfo(a, ChildContextKind.Args, index, index);
                        ++index;
                    }
                }
            }
        }

        internal void AddArg(Node n, bool addLast = true)
        {
            Contract.Requires(n != null && n.IsFuncOrAtom);

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
