namespace Microsoft.Formula.API.Nodes
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Microsoft.Formula.Common;

    public abstract class Node 
    {
        internal const int CancelCheckFreq = 500;

        private static readonly Node[] noNodes = new Node[0];

        protected int? cachedHashCode = null;

        public abstract int ChildCount
        {
            get;
        }

        public bool IsQuoteItem
        {
            get { return IsFuncOrAtom || NodeKind == NodeKind.QuoteRun; }
        }

        public bool IsContractSpec
        {
            get { return NodeKind == NodeKind.Body || NodeKind == NodeKind.CardPair; }
        }

        public bool IsParamType
        {
            get { return IsTypeTerm || NodeKind == NodeKind.ModRef; }
        }

        public bool IsTypeTerm
        {
            get { return NodeKind == Formula.API.NodeKind.Union || IsUnionComponent; }
        }

        public bool IsUnionComponent
        {
            get { return NodeKind == NodeKind.Id || NodeKind == NodeKind.Enum; }
        }

        public bool IsEnumElement
        {
            get { return NodeKind == NodeKind.Id || NodeKind == NodeKind.Cnst || NodeKind == NodeKind.Range; }
        }

        public bool IsAtom
        {
            get { return NodeKind == NodeKind.Id || NodeKind == NodeKind.Cnst; }
        }

        public bool IsFuncOrAtom
        {
            get { return NodeKind == NodeKind.Id || NodeKind == NodeKind.Cnst || NodeKind == NodeKind.FuncTerm || NodeKind == NodeKind.Compr || NodeKind == NodeKind.Quote; }
        }

        public bool IsModAppArg
        {
            get { return NodeKind == NodeKind.Id || NodeKind == NodeKind.Cnst || NodeKind == NodeKind.FuncTerm || NodeKind == NodeKind.ModRef || NodeKind == NodeKind.Quote; }
        }

        public bool IsDomOrTrans
        {
            get { return NodeKind == NodeKind.Domain || NodeKind == NodeKind.Transform; }
        }

        public bool IsModule
        {
            get { return NodeKind == NodeKind.Domain || NodeKind == NodeKind.Transform || NodeKind == NodeKind.TSystem || NodeKind == NodeKind.Model || NodeKind == NodeKind.Machine; }
        }

        public bool IsTypeDecl
        {
            get { return NodeKind == NodeKind.ConDecl || NodeKind == NodeKind.MapDecl || NodeKind == NodeKind.UnnDecl; }
        }

        public bool IsConstraint
        {
            get { return NodeKind == NodeKind.Find || NodeKind == NodeKind.RelConstr; }
        }

        public bool IsConfigSettable
        {
            get
            {
                return NodeKind == NodeKind.Rule ||
                       NodeKind == NodeKind.Step ||
                       NodeKind == NodeKind.Update ||
                       NodeKind == NodeKind.Property ||
                       NodeKind == NodeKind.ContractItem ||
                       NodeKind == NodeKind.ModelFact ||
                       IsTypeDecl;
            }
        }

        [Pure]
        public bool CanHaveContract(ContractKind kind)
        {
            switch (kind)
            {
                case ContractKind.ConformsProp:
                    return NodeKind == NodeKind.Domain;
                case ContractKind.EnsuresProp:
                case ContractKind.RequiresProp:
                    return NodeKind == NodeKind.Transform || NodeKind == NodeKind.Model;
                case ContractKind.RequiresSome:
                case ContractKind.RequiresAtLeast:
                case ContractKind.RequiresAtMost:
                    return NodeKind == NodeKind.Model;
                default:
                    throw new NotImplementedException();
            }
        }

        public abstract NodeKind NodeKind
        {
            get;
        }

        public Span Span
        {
            get;
            private set;
        }

        internal object CompilerData
        {
            get;
            set;
        }

        internal Node(Span span)
        {
            Span = span;
        }

        /// <summary>
        /// Only used by Program nodes
        /// </summary>
        internal Node()
        {}

        /// <summary>
        /// Returns the child of this node in order
        /// </summary>
        public abstract IEnumerable<Node> Children { get; }

        /// <summary>
        /// Returns more detailed information about children in order.
        /// </summary>
        public virtual IEnumerable<ChildInfo> ChildrenInfo 
        { 
            get
            {
                int index = 0;
                foreach (var c in Children)
                {
                    yield return new ChildInfo(c, ChildContextKind.AnyChildContext, index, index);
                    ++index;
                }
            }
        }

        public virtual bool TryGetStringAttribute(AttributeKind attribute, out string value)
        {
            value = null;
            return false;
        }

        public virtual bool TryGetNumericAttribute(AttributeKind attribute, out Rational value)
        {
            value = Rational.Zero;
            return false;
        }

        public virtual bool TryGetBooleanAttribute(AttributeKind attribute, out bool value)
        {
            value = false;
            return false;
        }

        public virtual bool TryGetKindAttribute(AttributeKind attribute, out object value)
        {
            value = null;
            return false;
        }

        /// <summary>
        /// Returns true if this node satisfies the atomic predicate.
        /// </summary>
        protected virtual bool EvalAtom(ASTQueries.NodePredAtom pred, ChildContextKind context, int absPos, int relPos)
        {
            if (pred.PredicateKind == NodePredicateKind.False ||
                (pred.TargetKind != NodeKind.AnyNodeKind && pred.TargetKind != NodeKind))
            {
                return false;
            }

            if (pred.ChildContext != ChildContextKind.AnyChildContext && pred.ChildContext != context)
            {
                return false;
            }

            if (pred.ChildContext == ChildContextKind.AnyChildContext &&
                (absPos < pred.ChildIndexLower || absPos > pred.ChildIndexUpper))
            {
                return false;
            }

            if (pred.ChildContext != ChildContextKind.AnyChildContext &&
                (relPos < pred.ChildIndexLower || relPos > pred.ChildIndexUpper))
            {
                return false;
            }

            return true;            
        }

        internal bool Eval(ASTQueries.NodePred pred, ChildContextKind context, int absPos, int relPos)
        {
            if (pred.PredicateKind == NodePredicateKind.Star)
            {
                return true;
            }
            else if (pred.PredicateKind == NodePredicateKind.False)
            {
                return false;
            }
            else if (pred.PredicateKind == NodePredicateKind.Atom)
            {
                return EvalAtom((ASTQueries.NodePredAtom)pred, context, absPos, relPos);
            }
            else if (pred.PredicateKind == NodePredicateKind.Or)
            {
                var or = (ASTQueries.NodePredOr)pred;
                if (or.Arg1.PredicateKind == NodePredicateKind.Or)
                {
                    return Eval(or.Arg2, context, absPos, relPos) ||
                           Eval(or.Arg1, context, absPos, relPos);
                }
                else
                {
                    return Eval(or.Arg1, context, absPos, relPos) ||
                           Eval(or.Arg2, context, absPos, relPos);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Enumerates the children of this node from which the subquery may be satisfied.
        /// </summary>
        internal IEnumerable<ChildInfo> GetFeasibleChildren(ASTQueries.NodePred[] query, int pos)
        {
            var p = query[pos];
            foreach (var c in ChildrenInfo)
            {
                if (c.Node.Eval(p, c.Context, c.AbsolutePos, c.RelativePos) &&
                    ASTQueries.ASTSchema.Instance.IsQueryFeasible(c.Node.NodeKind, c.Context, query, pos))
                {
                    yield return c;
                }
            }
        }

        /// <summary>
        /// Returns the clone of this node, if clonedChildren are already clones
        /// of all the children.
        /// </summary>
        internal abstract Node DeepClone(IEnumerable<Node> clonedChildren);

        /// <summary>
        /// Returns a shallow copy of this node where the ith child node is replaced
        /// by the node replace. A shallow clone of an atom 
        /// </summary>
        internal abstract Node ShallowClone(Node replace, int pos);

        /// <summary>
        /// Returns true if n is equivalent to this node when
        /// only taking into account properties of this node
        /// and the number of children.
        /// </summary>
        internal abstract bool IsLocallyEquivalent(Node n);

        /// <summary>
        /// Returns a detailed hash of the node kind. For example, if the
        /// node is a DataTerm, then the detailed node kind includes the 
        /// constructor name.
        /// </summary>
        protected abstract int GetDetailedNodeKindHash();

        /// <summary>
        /// Note that node equality is not redefined, but will be explosed through AST.
        /// Therefore, this method will be called by the ASTConcr.
        /// </summary>
        internal int GetNodeHash()
        {
            if (cachedHashCode != null)
            {
                return (int)cachedHashCode;
            }

            new ASTComputation<int>(
                this,
                (n) => 
                { 
                    return n.cachedHashCode == null ? n.Children : null; 
                },
                (n, chashes) =>
                {
                    if (n.cachedHashCode != null)
                    {
                        return (int)n.cachedHashCode;
                    }

                    int num;
                    unchecked
                    {
                        num = 2 + n.GetDetailedNodeKindHash();
                    }

                    foreach (var c in chashes)
                    {
                        unchecked
                        {
                            num += -1640531527 + c + ((num << 6) + (num >> 2));
                        }
                    }

                    return (int)(n.cachedHashCode = num);
                }).Compute();

            return (int)cachedHashCode;
        }

        internal AST<Node> FindAny(
                    LinkedList<ChildInfo> pathToNode,
                    ASTQueries.NodePred[] query, 
                    CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(query != null);
            Contract.Requires(pathToNode != null && pathToNode.Last.Value.Node == this);

            if (query.Length == 0)
            {
                return null;
            }

            var initContext = ChildContextKind.AnyChildContext;
            int initAbsPos = 0, initRelPos = 0;

            if (pathToNode.Count > 1)
            {
                var prev = pathToNode.Last.Previous;
                foreach (var c in prev.Value.Node.ChildrenInfo)
                {
                    if (c.AbsolutePos == pathToNode.Last.Value.AbsolutePos)
                    {
                        initContext = c.Context;
                        initAbsPos = c.AbsolutePos;
                        initRelPos = c.RelativePos;
                        break;
                    }
                }
            }

            if (!Eval(query[0], initContext, initAbsPos, initRelPos) ||
                !ASTQueries.ASTSchema.Instance.IsQueryFeasible(NodeKind, initContext, query, 0))
            {
                return null;
            }

            var qpath = new LinkedList<ChildInfo>(pathToNode);
            var ctok = cancel == default(CancellationToken) ? ASTComputationBase.MkControlToken() : ASTComputationBase.MkControlToken(cancel, CancelCheckFreq);
            var astComp = new ASTComputationUpDown<int, bool>(
                this,
                (n, pos) => FindAnyUnfold(n == this, n, pos, query, ctok, qpath),
                (n, v, c) =>
                {
                    if (qpath.Last.Value.Node != this)
                    {
                        qpath.RemoveLast();
                    }

                    return true;
                },
                ctok);
            astComp.Compute(0);

            if (!ctok.IsSuspended)
            {
                return null;
            }
            else if (cancel != default(CancellationToken) && cancel.IsCancellationRequested)
            {
                return null;
            }

            Action<ChildInfo, bool> extender;
            var ast = Factory.Instance.MkEmptyAST(qpath.Last.Value.Node.NodeKind, out extender);
            var crnt = qpath.First;
            while (crnt != null)
            {
                extender(crnt.Value, crnt.Next == null);
                crnt = crnt.Next;
            }

            return ast;
        }

        internal void FindAll(
            LinkedList<ChildInfo> pathToNode,
            ASTQueries.NodePred[] query, 
            Action<IEnumerable<ChildInfo>, Node> visitor, 
            CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(query != null);
            Contract.Requires(pathToNode != null && pathToNode.Last.Value.Node == this);

            if (query.Length == 0)
            {
                return;
            }

            var initContext = ChildContextKind.AnyChildContext;
            int initAbsPos = 0, initRelPos = 0;

            if (pathToNode.Count > 1)
            {
                var prev = pathToNode.Last.Previous;
                foreach (var c in prev.Value.Node.ChildrenInfo)
                {
                    if (c.AbsolutePos == pathToNode.Last.Value.AbsolutePos)
                    {
                        initContext = c.Context;
                        initAbsPos = c.AbsolutePos;
                        initRelPos = c.RelativePos;
                        break;
                    }
                }
            }

            if (!Eval(query[0], initContext, initAbsPos, initRelPos) ||
                !ASTQueries.ASTSchema.Instance.IsQueryFeasible(NodeKind, initContext, query, 0))
            {
                return;
            }

            var qpath = new LinkedList<ChildInfo>(pathToNode);
            var ctok = cancel == default(CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, CancelCheckFreq);
            var astComp = new ASTComputationUpDown<int, bool>(
                this,
                (n, pos) => FindAllUnfold(n == this, n, pos, query, qpath, visitor),
                (n, v, c) =>
                {
                    if (qpath.Last.Value.Node != this)
                    {
                        qpath.RemoveLast();
                    }

                    return true;
                },
                ctok);
            astComp.Compute(0);
        }

        protected static LinkedList<S> TakeClones<S>(int length, IEnumerator<Node> clonedChildren, out LinkedList<S> nodesOut)
            where S : Node
        {
            nodesOut = new LinkedList<S>();
            bool result;
            for (int i = 0; i < length; ++i)
            {
                result = clonedChildren.MoveNext();
                Contract.Assert(result);
                nodesOut.AddLast((S)clonedChildren.Current);
            }

            return nodesOut;
        }

        protected static S TakeClone<S>(IEnumerator<Node> clonedChildren)
            where S : Node
        {
            var result = clonedChildren.MoveNext();
            Contract.Assert(result);
            return (S)clonedChildren.Current;
        }

        protected static LinkedList<S> CloneCollection<S>(LinkedList<S> nodes,
                                             Node clone,
                                             int pos,
                                             ref int occurs,
                                             out LinkedList<S> nodesOut)
            where S : Node
        {
            if (occurs > pos || occurs + nodes.Count < pos)
            {
                occurs = occurs + nodes.Count;
                nodesOut = new LinkedList<S>(nodes);
                return nodesOut;
            }

            nodesOut = new LinkedList<S>();
            foreach (var n in nodes)
            {
                if (pos == occurs)
                {
                    nodesOut.AddLast((S)clone);
                }
                else
                {
                    nodesOut.AddLast(n);
                }

                ++occurs;
            }

            return nodesOut;
        }

        protected static S CloneField<S>(S fieldVal, Node clone, int pos, ref int occurs)
            where S : Node
        {
            var result = pos == occurs ? (S)clone : fieldVal;
            ++occurs;
            return result;
        }

        private static IEnumerable<Tuple<Node, int>> FindAnyUnfold(
                                                    bool isStartNode,
                                                    Node n,
                                                    int pos,
                                                    ASTQueries.NodePred[] query,
                                                    ASTComputationBase.IControlToken ctok,
                                                    LinkedList<ChildInfo> path)
        {
            var next = GetNextNonStar(query, pos);
            if (isStartNode && pos == 0 && query[pos].PredicateKind == NodePredicateKind.Star && next < query.Length)
            {
                var ci = path.Last.Value;
                if (n.Eval(query[next], ci.Context, ci.AbsolutePos, ci.RelativePos))
                {
                    yield return new Tuple<Node, int>(ci.Node, next);
                }
            }

            if (pos >= query.Length - 1 || next >= query.Length)
            {
                ctok.Suspend();
                yield break;
            }
            else if (query[pos].PredicateKind != NodePredicateKind.Star)
            {
                next = GetNextNonStar(query, pos + 1);
                if (next < query.Length && next > pos + 1)
                {
                    foreach (var c in n.GetFeasibleChildren(query, next))
                    {
                        path.AddLast(c);
                        yield return new Tuple<Node, int>(c.Node, next);
                    }
                }

                foreach (var c in n.GetFeasibleChildren(query, pos + 1))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, pos + 1);
                }
            }
            else
            {
                foreach (var c in n.GetFeasibleChildren(query, next))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, next);
                }

                foreach (var c in n.GetFeasibleChildren(query, pos))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, pos);
                }
            }
        }

        private static IEnumerable<Tuple<Node, int>> FindAllUnfold(
                                                    bool isStartNode,
                                                    Node n,
                                                    int pos,
                                                    ASTQueries.NodePred[] query,
                                                    LinkedList<ChildInfo> path,
                                                    Action<IEnumerable<ChildInfo>, Node> visitor)
        {
            var next = GetNextNonStar(query, pos);
            if (isStartNode && pos == 0 && query[pos].PredicateKind == NodePredicateKind.Star && next < query.Length)
            {
                var ci = path.Last.Value;
                if (n.Eval(query[next], ci.Context, ci.AbsolutePos, ci.RelativePos))
                {
                    yield return new Tuple<Node, int>(ci.Node, next);
                }
            }

            if (pos >= query.Length - 1)
            {
                visitor(path, path.Last.Value.Node);
                yield break;
            }
            else if (next >= query.Length)
            {
                visitor(path, path.Last.Value.Node);
                foreach (var c in n.GetFeasibleChildren(query, pos))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, pos);
                }
            }            
            else if (query[pos].PredicateKind != NodePredicateKind.Star)
            {
                next = GetNextNonStar(query, pos + 1);
                if (next < query.Length && next > pos + 1)
                {
                    foreach (var c in n.GetFeasibleChildren(query, next))
                    {
                        path.AddLast(c);
                        yield return new Tuple<Node, int>(c.Node, next);
                    }
                }

                foreach (var c in n.GetFeasibleChildren(query, pos + 1))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, pos + 1);
                }
            }
            else
            {
                foreach (var c in n.GetFeasibleChildren(query, next))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, next);
                }

                foreach (var c in n.GetFeasibleChildren(query, pos))
                {
                    path.AddLast(c);
                    yield return new Tuple<Node, int>(c.Node, pos);
                }
            }
        }

        private static int GetNextNonStar(ASTQueries.NodePred[] query, int pos)
        {
            while (pos < query.Length && query[pos].PredicateKind == NodePredicateKind.Star)
            {
                ++pos;
            }

            return pos;
        }   
    }
}
