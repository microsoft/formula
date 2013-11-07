namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;

    public sealed class RenderResult
    {
        private List<Flag> flags = new List<Flag>();

        public bool Succeeded
        {
            get;
            private set;
        }

        public AST<Node> Module
        {
            get;
            internal set;
        }

        public IEnumerable<Flag> Flags
        {
            get { return flags; }
        }

        internal RenderResult()
        {
            Succeeded = true;
        }

        internal void AddFlag(Flag flag)
        {
            flags.Add(flag);
            Succeeded = Succeeded && flag.Severity != SeverityKind.Error;
        }

        internal void AddFlags(IEnumerable<Flag> flags)
        {
            if (flags == null)
            {
                return;
            }

            foreach (var f in flags)
            {
                AddFlag(f);
            }
        }

        internal void Failed()
        {
            Succeeded = false;
        }
    }
}