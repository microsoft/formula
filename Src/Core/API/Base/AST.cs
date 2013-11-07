namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using Nodes;

    public interface AST<out T> : IEnumerable<Node> where T : Node
    {
        /// <summary>
        /// The node at the root of this AST
        /// </summary>
        Node Root { get; }

        /// <summary>
        /// A node of type T at the end of a path in the AST
        /// </summary>
        T Node { get; }

        /// <summary>
        /// The path from the Root to Node
        /// </summary>
        IEnumerable<ChildInfo> Path { get; }

        /// <summary>
        /// If n is null, then returns the last node in the path.
        /// If n != null is a node in Path, then returns the parent
        /// of n. Otherwise returns null.
        /// 
        /// </summary>
        Node GetPathParent(Node n);

        /// <summary>
        /// Returns the ith parent of the last node,
        /// if such a parent exists. Otherwise, return null.
        /// </summary>
        Node GetPathParent(int i = 0);

        /// <summary>
        /// Performs an AST computation using unfold and fold operations starting from Node.
        /// Compute(n, unfold, fold) = fold(n, Compute(unfold(n))).
        /// </summary>
        S Compute<S>(
            Func<Node, IEnumerable<Node>> unfold,
            Func<Node, IEnumerable<S>, S> fold, 
            CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Performs an AST computation using unfold and fold operations starting from Node.
        /// The unfold operation can return a value to be propagated downwards.
        /// 
        /// Compute(a, v, unfold, fold) = fold(a, Compute(a_1, v_1), ... Compute(a_n, v_n)) 
        /// 
        /// where:
        /// The (a_1, v_1)...(a_n, v_n) is returned by unfold(a, v).
        /// </summary>
        S Compute<R, S>(
            R initVal,
            Func<Node, R, IEnumerable<Tuple<Node, R>>> unfold,
            Func<Node, R, IEnumerable<S>, S> fold,
            CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Performs an AST computation over two trees by unfold and fold operations starting from Node.
        /// Let A be this tree and B be the other tree. Let a \in A and b \in B.
        /// 
        /// Then:
        /// Compute(a, b, unfold, fold) = 
        /// fold(a, b, Compute(a_1, b_1), ... Compute(a_n, b_n)) 
        /// 
        /// where:
        /// a_1 ... a_n are returned by the first enumerator of unfold(a, b)
        /// b_1 ... b_n are returned by the second enumerator of unfold(a, b).
        /// 
        /// If one enumerator enumerates fewer elements, then the remaining
        /// elements are null.
        /// </summary>
        S Compute<S>(
            AST<Node> otherTree,
            Func<Node, Node, Tuple<IEnumerable<Node>, IEnumerable<Node>>> unfold,
            Func<Node, Node, IEnumerable<S>, S> fold,
            CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Returns the first path from this.Node satisfying query. This path includes the path from Root -> Node.
        /// </summary>
        AST<Node> FindAny(
            ASTQueries.NodePred[] query, 
            CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Visits all the paths in the AST satisfying the query starting from this.Node.
        /// Paths a returned as ienumerables and include the path from Root -> Node.
        /// IEnumerables can turned into proper ASTs using the factory method: FromAbsPositions().
        /// Do not cache the IEnumerables.
        /// </summary>
        void FindAll(
                     ASTQueries.NodePred[] query, 
                     Action<IEnumerable<ChildInfo>, Node> visitor,
                     CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Simultaneously finds and substitutes all the Ids at or below this.Node satisfying filter.
        /// For each match m, subKind(m) returns the kind of node that Id will be substituted by.
        /// If subKind(m) would be a valid substitution in the tree, then sub(m) is called to return
        /// the substitution. Otherwise, the substitution is ignored for that match.
        /// </summary>
        AST<Node> Substitute(
                     ASTQueries.NodePredAtom filter,
                     Func<IEnumerable<ChildInfo>, NodeKind> subKind,
                     Func<IEnumerable<ChildInfo>, Node> sub,
                     CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Prints this AST to textwriter.
        /// </summary>
        void Print(TextWriter wr, CancellationToken cancel = default(CancellationToken), EnvParams envParams = null);        

        /// <summary>
        /// Performs a deep clone of this AST. All node sharing is removed by cloning.
        /// Returns null if canceled before clone completes.
        /// </summary>
        AST<Node> DeepClone(CancellationToken cancel = default(CancellationToken));

        /// <summary>
        /// Save this AST to a filename
        /// </summary>
        void SaveAs(string filename, CancellationToken cancel = default(CancellationToken), EnvParams envParams = null);
    }
}
