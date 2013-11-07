namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;

    using Common;
    using Nodes;

    public sealed class InstallStatus
    {
        public AST<Program> Program
        {
            get;
            private set;
        }

        public InstallKind Status
        {
            get;
            internal set;
        }

        internal InstallStatus(AST<Program> program, InstallKind status)
        {
            Program = program;
            Status = status;
        }
    }
}
