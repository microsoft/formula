namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Runtime.Serialization;
    using Nodes;

    public sealed class Flag
    {
        public SeverityKind Severity
        {
            get;
            private set;
        }

        public int Code
        {
            get;
            private set;
        }

        public Span Span
        {
            get;
            private set;
        }

        public Node Node
        {
            get;
            private set;
        }

        public ProgramName ProgramName
        {
            get;
            private set;
        }

        public string Message
        {
            get;
            private set;
        }

        public Flag(SeverityKind severity, Span span, string message, int code, ProgramName progName = null)
        {
            Severity = severity;
            Span = span;
            Message = string.IsNullOrWhiteSpace(message) ? "" : message;
            Code = code;
            Node = null;
            ProgramName = progName;
        }

        public Flag(SeverityKind severity, Node node, string message, int code, ProgramName progName = null)
        {
            Contract.Requires(node != null);
            Severity = severity;
            Span = node.Span;
            Message = string.IsNullOrWhiteSpace(message) ? "" : message;
            Code = code;
            Node = node;
            ProgramName = progName;
        }
    }
}
