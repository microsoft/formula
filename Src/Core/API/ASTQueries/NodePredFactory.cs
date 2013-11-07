namespace Microsoft.Formula.API.ASTQueries
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public sealed class NodePredFactory
    {
        private static NodePredFactory theInstance = new NodePredFactory();

        public static NodePredFactory Instance
        {
            get { return theInstance; }
        }

        public NodePredAtom True
        {
            get;
            private set;
        }

        public NodePredAtom False
        {
            get;
            private set;
        }

        public NodePredStar Star
        {
            get;
            private set;
        }

        public NodePredOr Module
        {
            get;
            private set;
        }

        public NodePredOr TypeDecl
        {
            get;
            private set;
        }

        public NodePredFactory()
        {
            Star = new NodePredStar();
            
            True = new NodePredAtom(NodeKind.AnyNodeKind, ChildContextKind.AnyChildContext, -1, int.MaxValue, null);

            False = new NodePredFalse();

            Module = MkPredicate(NodeKind.Model) |
                     MkPredicate(NodeKind.Domain) |
                     MkPredicate(NodeKind.Transform) |
                     MkPredicate(NodeKind.TSystem) |
                     MkPredicate(NodeKind.Machine);

            TypeDecl = MkPredicate(NodeKind.UnnDecl) |
                       MkPredicate(NodeKind.ConDecl) |
                       MkPredicate(NodeKind.MapDecl);
        }

        public NodePredAtom MkPredicate(NodeKind targetKind)
        {
            return new NodePredAtom(targetKind, ChildContextKind.AnyChildContext, -1, int.MaxValue, null);
        }

        public NodePredAtom MkPredicate(ChildContextKind context)
        {
            return new NodePredAtom(NodeKind.AnyNodeKind, context, -1, int.MaxValue, null);
        }

        public NodePredAtom MkPredicate(Func<AttributeKind, object, bool> attributePredicate)
        {
            return new NodePredAtom(NodeKind.AnyNodeKind, ChildContextKind.AnyChildContext, -1, int.MaxValue, attributePredicate);
        }

        public NodePredAtom MkNamePredicate(string name)
        {
            return new NodePredAtom(NodeKind.AnyNodeKind, ChildContextKind.AnyChildContext, -1, int.MaxValue, (a, o) => HasName(a, o, name));
        }

        /// <summary>
        /// Makes a predicate that matches folders to the program, and then matches the remainder
        /// </summary>
        public NodePred[] MkProgramPredicate(ProgramName prog, params NodePred[] remainder)
        {
            Contract.Requires(prog != null);
            var segs = prog.Uri.Segments;
            Contract.Assert(segs.Length > 1 && segs[0] == "/");
            var preds = new NodePred[segs.Length + (remainder == null ? 0 : remainder.Length)];
            preds[0] = MkPredicate(NodeKind.Folder) & MkNamePredicate("/");
            for (int i = 1; i < segs.Length - 1; ++i)
            {
                var seg = segs[i];
                preds[i] = MkPredicate(NodeKind.Folder) & MkNamePredicate(seg.Substring(0, seg.Length - 1));
            }

            preds[segs.Length - 1] = MkPredicate(NodeKind.Program) & MkNamePredicate(prog.ToString());

            if (remainder != null)
            {
                for (int i = 0; i < remainder.Length; ++i)
                {
                    preds[segs.Length + i] = remainder[i];
                }
            }

            return preds;
        }
              
        public NodePredAtom MkPredicate(
            NodeKind targetKind,
            ChildContextKind childContext,
            int childIndexLower,
            int childIndexUpper,
            Func<AttributeKind, object, bool> attributePredicate)
        {
            Contract.Requires(childIndexLower <= childIndexUpper);
            return new NodePredAtom(targetKind, childContext, childIndexLower, childIndexUpper, attributePredicate);
        }

        private static bool HasName(AttributeKind attr, object obj, string name)
        {
            if (attr != AttributeKind.Name)
            {
                return true;
            }
            else if (obj is string)
            {
                return ((string)obj) == name;
            }
            else if (obj is ProgramName)
            {
                return ((ProgramName)obj).ToString() == name.ToLowerInvariant();
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
