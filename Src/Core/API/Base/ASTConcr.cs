namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Nodes;

    public class ASTConcr<T> : AST<T> where T : Node
    {
        private int? cachedHashCode = null;

        private Node root;
        
        private T node;

        private LinkedList<ChildInfo> path = new LinkedList<ChildInfo>();
                
        public Node Root { get { return root; } }
        
        public T Node { get { return node; } }

        public IEnumerable<ChildInfo> Path { get { return path; } }

        /// <summary>
        /// Constructor used during cloning and find operations
        /// </summary>
        internal ASTConcr()
        {
            root = null;
            node = null;
        }

        internal ASTConcr(T root, bool computeRootHash = true)
        {
            this.root = root;
            this.node = root;
            path.AddFirst(new ChildInfo(root, ChildContextKind.AnyChildContext, -1, -1));

            if (computeRootHash)
            {
                root.GetNodeHash();
            }
        }

        internal ASTConcr<T> ShallowClone()
        {
            var clone = new ASTConcr<T>();
            var crnt = path.Last;

            Node n = null, p = null;
            int pos = -1;
            while (crnt != null)
            {
                n = crnt.Value.Node.ShallowClone(p, pos);
                pos = crnt.Value.AbsolutePos;
                p = n;

                clone.path.AddFirst(new ChildInfo(n, crnt.Value.Context, pos, crnt.Value.RelativePos));
                crnt = crnt.Previous;
            }

            clone.root = clone.path.First.Value.Node;
            clone.node = (T)clone.path.Last.Value.Node;
            return clone;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<Node> GetEnumerator()
        {
            return new ASTEnumerator(root);
        }

        public Node GetPathParent(int i = 0)
        {
            int j = 0;
            var crnt = path.Last.Previous;
            while (crnt != null)
            {
                if (j == i)
                {
                    return crnt.Value.Node;
                }

                ++j;
                crnt = crnt.Previous;
            }

            return null;
        }

        public Node GetPathParent(Node n)
        {
            if (n == null)
            {
                return path.Last.Value.Node;
            }

            var m = path.Last;
            while (m != null)
            {
                if (Factory.Instance.IsEqualRoots(m.Value.Node, n))
                {
                    return m.Previous == null ? null : m.Previous.Value.Node;
                }

                m = m.Previous;
            }

            return null;
        }

        public AST<Node> DeepClone(CancellationToken cancel = default(CancellationToken))
        {
            var result = Compute<Node>((n) => n.Children, (n, clones) => n.DeepClone(clones), cancel);
            return result == null ? null : Factory.Instance.ToAST(result);
        }

        public S Compute<S>(
            Func<Node, IEnumerable<Node>> unfold, 
            Func<Node, IEnumerable<S>, S> fold,
            CancellationToken cancel = default(CancellationToken))
        {
            var ctok = cancel == default(CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, Nodes.Node.CancelCheckFreq);
            return new ASTComputation<S>(this.node, unfold, fold, ctok).Compute();
        }

        public S Compute<R, S>(
            R initval,
            Func<Node, R, IEnumerable<Tuple<Node, R>>> unfold,
            Func<Node, R, IEnumerable<S>, S> fold,
            CancellationToken cancel = default(CancellationToken))
        {
            var ctok = cancel == default(CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, Nodes.Node.CancelCheckFreq);
            return new ASTComputationUpDown<R, S>(this.node, unfold, fold, ctok).Compute(initval);
        }

        public S Compute<S>(
            AST<Node> otherTree,
            Func<Node, Node, Tuple<IEnumerable<Node>, IEnumerable<Node>>> unfold,
            Func<Node, Node, IEnumerable<S>, S> fold,
            CancellationToken cancel = default(CancellationToken))
        {
            var ctok = cancel == default(CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, Nodes.Node.CancelCheckFreq);
            return new ASTComputation2<S>(node, otherTree.Node, unfold, fold, ctok).Compute();
        }

        public AST<Node> Substitute(
                     ASTQueries.NodePredAtom filter,
                     Func<IEnumerable<ChildInfo>, NodeKind> subKind,
                     Func<IEnumerable<ChildInfo>, Node> sub,
                     CancellationToken cancel = default(CancellationToken))
        {
            var query = new ASTQueries.NodePred[]
            {
                ASTQueries.NodePredFactory.Instance.Star,
                ASTQueries.NodePredFactory.Instance.MkPredicate(NodeKind.Id) & filter
            };

            if (Root.NodeKind == NodeKind.Id)
            {
                if (Root.Eval(query[1], ChildContextKind.AnyChildContext, 0, 0))
                {
                    var rootPath = new ChildInfo[]{ new ChildInfo(Root, ChildContextKind.AnyChildContext, -1, -1) };
                    var kind = subKind(rootPath);
                    var m = sub(rootPath);
                    return m.NodeKind != kind ? this : Factory.Instance.ToAST(m);
                }
                else 
                {
                    return this;
                }
            }

            AST<Node> crntAst = Factory.Instance.ToAST(Root);
            this.FindAll(
                query,
                (pt, x) =>
                {
                    var list = (LinkedList<ChildInfo>)pt;
                    var prev = list.Last.Previous;
                    var rkind = subKind(pt);
                    if (!ASTQueries.ASTSchema.Instance.CanReplace(
                                prev.Value.Node, 
                                list.Last.Value.Node, 
                                list.Last.Value.Context, 
                                rkind))
                    {
                        return;
                    }

                    var rep = sub(pt);
                    if (rep.NodeKind != rkind)
                    {
                        return;
                    }

                    var newAST = crntAst == this ? this : Factory.Instance.FromAbsPositions(crntAst.Root, pt);
                    var crnt = ((LinkedList<ChildInfo>)newAST.Path).Last;
                    var subPath = new LinkedList<ChildInfo>();
                    Node n = null, p = null;
                    int pos = -1;
                    while (crnt != null)
                    {
                        n = pos == -1 ? rep : crnt.Value.Node.ShallowClone(p, pos);
                        pos = crnt.Value.AbsolutePos;
                        p = n;

                        subPath.AddFirst(new ChildInfo(n, crnt.Value.Context, pos, crnt.Value.RelativePos));
                        crnt = crnt.Previous;
                    }

                    crntAst = Factory.Instance.ToAST(n);
                }, 
                cancel);

            crntAst.GetHashCode();
            return crntAst;
        }

        public AST<Node> FindAny(ASTQueries.NodePred[] query, CancellationToken cancel = default(CancellationToken))
        {
            return node.FindAny(path, query, cancel);
        }

        public void FindAll(
                ASTQueries.NodePred[] query, 
                Action<IEnumerable<ChildInfo>, Node> visitor, 
                CancellationToken cancel = default(CancellationToken))
        {
            node.FindAll(path, query, visitor, cancel);
        }

        public void SaveAs(string filename, CancellationToken cancel = default(CancellationToken), EnvParams envParams = null)
        {
            Contract.Assert(filename != null);
            var file = new System.IO.FileInfo(filename);
            using (var sw = new System.IO.StreamWriter(file.FullName))
            {
                Print(sw, cancel, new EnvParams(envParams, new Uri(file.FullName, UriKind.Absolute)));
            }
        }

        public void Print(System.IO.TextWriter wr, CancellationToken cancel = default(CancellationToken), EnvParams envParams = null)
        {
            Printing.Print(root, wr, cancel, envParams);
        }

        public override int GetHashCode()
        {
            if (cachedHashCode != null)
            {
                return (int)cachedHashCode;
            }

            var num = root.GetNodeHash();
            unchecked
            {
                foreach (var kv in path)
                {
                    num += -1640531527 + kv.Node.GetNodeHash() + kv.AbsolutePos.GetHashCode() + ((num << 6) + (num >> 2));
                }
            }

            cachedHashCode = num;
            return num;
        }

        public override bool Equals(object obj)
        {
            if (obj == this)
            {
                return true;
            }

            var ast = obj as ASTConcr<T>;
            if (ast == null || ast.GetHashCode() != GetHashCode() || path.Count != ast.path.Count)
            {
                return false;
            }

            var pA = path.First;
            var pB = ast.path.First;
            while (pA != null)
            {
                if (pA.Value.AbsolutePos != pB.Value.AbsolutePos)
                {
                    return false;
                }

                pA = pA.Next;
                pB = pB.Next;
            }

            return Factory.Instance.IsEqualRoots(root, ast.root);
        }

        internal void ExtendPath(ChildInfo childInfo, bool endPath)
        {
            Contract.Requires(childInfo.Node != null);
            path.AddLast(childInfo);
            if (endPath)
            {
                this.root = path.First.Value.Node;
                this.node = (T)path.Last.Value.Node;
                GetHashCode();
            }
        }  
    }
}
