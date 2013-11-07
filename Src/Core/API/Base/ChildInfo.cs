namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using Nodes;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public struct ChildInfo
    {
        private Node node;
        private ChildContextKind context;
        private int absPos;
        private int relPos;

        public Node Node
        {
            get { return node; }
        }

        public ChildContextKind Context
        {
            get { return context; }
        }

        public int AbsolutePos
        {
            get { return absPos; }
        }

        public int RelativePos
        {
            get { return relPos; }
        }

        internal ChildInfo(Node node, ChildContextKind context, int absPos, int relPos)
        {
            this.node = node;
            this.context = context;
            this.absPos = absPos;
            this.relPos = relPos;
        }        
    }
}
