namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;

    using API;
    using API.Nodes;

    /// <summary>
    /// A location is a path in a "normalized" AST. The root of the AST must be a program and
    /// the AST does not share nodes. In a set of locations, there should never be
    /// two locations from different programs with the same name. These conditions are maintained
    /// by the compiler. Comparing locations is then by lexicographic path ordering.
    /// </summary>
    internal struct Location
    {
        private AST<Node> ast;
        public AST<Node> AST
        {
            get { return ast; }
        }

        public Program Program
        {
            get { return (Program)ast.Root; }
        }

        public Location(AST<Node> path)
        {
            Contract.Requires(path != null);
            Contract.Requires(path.Root.NodeKind == NodeKind.Program);
            this.ast = path;
        }

        public string GetFileLocationString(EnvParams envParams)
        {
            if (ast == null)
            {
                return "unknown program (?, ?)";
            }

            return string.Format(
                "{0} ({1}, {2})",
                ((Program)ast.Root).Name.ToString(envParams),
                ast.Node.Span.StartLine,
                ast.Node.Span.StartCol);
        }

        public static Location MkLocation(Node start, IEnumerable<ChildInfo> path)
        {
            Contract.Requires(start != null && path != null);
            Contract.Requires(start.NodeKind == NodeKind.Program);
            return new Location(Factory.Instance.FromAbsPositions(start, path));
        }

        public static int Compare(Location l1, Location l2)
        {
            if (l1.ast == null)
            {
                return l2.ast == null ? 0 : -1;
            }
            else if (l2.ast == null)
            {
                return 1;
            }
            else if (l1.ast == l2.ast)
            {
                return 0;
            }

            var cmp = ProgramName.Compare(((Program)l1.ast.Root).Name, ((Program)l2.ast.Root).Name);
            if (cmp != 0)
            {
                return cmp;
            }

            bool move1, move2;
            using (var it1 = l1.ast.Path.GetEnumerator())
            {
                using (var it2 = l2.ast.Path.GetEnumerator())
                {
                    while ((move1 = it1.MoveNext()) & (move2 = it2.MoveNext()))
                    {
                        cmp = it1.Current.AbsolutePos - it2.Current.AbsolutePos;
                        if (cmp != 0)
                        {
                            return cmp;
                        }
                    }

                    if (move1 && !move2)
                    {
                        return 1;
                    }
                    else if (move2 && !move1)
                    {
                        return -1;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }                        
        }
    }
}
