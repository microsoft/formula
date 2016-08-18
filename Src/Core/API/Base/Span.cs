namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Runtime.Serialization;
    using Common;

    public struct Span
    {
        public static Span Unknown
        {
            get { return default(Span); }
        }

        private int startLine;
        private int startCol;
        private int endLine;
        private int endCol;
        private ProgramName program;

        public int StartLine
        {
            get { return startLine; }
        }

        public int StartCol
        {
            get { return startCol; }
        }

        public int EndLine
        {
            get { return endLine; }
        }

        public int EndCol
        {
            get { return endCol; }
        }

        /// <summary>
        /// The program where the related span occurs.
        /// </summary>
        public ProgramName Program
        {
            get { return program; }
        }

        public Span(int startLine, int startCol, int endLine, int endCol, ProgramName program)
        {
            if (program == null)
            {
                throw new ArgumentNullException("program");
            }
            this.startLine = startLine;
            this.startCol = startCol;
            this.endLine = endLine;
            this.endCol = endCol;
            this.program = program;
        }

        public static int Compare(Span s, Span t)
        {
            if (s.program != t.program)
            {
                int rc = Uri.Compare(s.program.Uri, t.program.Uri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase);
                if (rc != 0)
                {
                    return rc;
                }
            }
            if (s.startLine != t.startLine)
            {
                return s.startLine < t.startLine ? -1 : 1;
            }
            else if (s.startCol != t.startCol)
            {
                return s.startCol < t.startCol ? -1 : 1;
            }
            else if (s.endLine != t.endLine)
            {
                return s.endLine < t.endLine ? -1 : 1;
            }
            else if (s.endCol != t.endCol)
            {
                return s.endCol < t.endCol ? -1 : 1;
            }

            return 0;
        }
    }
}
