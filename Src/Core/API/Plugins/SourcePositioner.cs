namespace Microsoft.Formula.API.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Common;
    using Nodes;

    /// <summary>
    /// Maps zero-indexed (line, col) pairs to locations in a source program.
    /// </summary>
    public sealed class SourcePositioner
    {
        private Span quoteSpan;
        private int lastLine;

        private Set<LineColTransform> transforms = 
            new Set<LineColTransform>(LineColTransform.Compare);

        internal SourcePositioner(Quote q, string escapePrefix)
        {
            Contract.Requires(q != null);
          
            quoteSpan = q.Span;
            int line = 0, col = 0;
            int escapeId = 0;
            int length;
            int prefixLength = escapePrefix == null ? 0 : escapePrefix.Length;
            bool isNewLine;

            QuoteRun qr;
            foreach (var n in q.Contents)
            {
                if (n.NodeKind == NodeKind.QuoteRun)
                {
                    qr = (QuoteRun)n;
                    length = qr.Text.Length;
                    if (length == 0)
                    {
                        continue;
                    }

                    isNewLine = qr.Text[length - 1] == '\n';
                    transforms.Add(
                        new LineColTransform(
                            line,
                            col,
                            isNewLine ? int.MaxValue : col + length - 1,
                            n.Span.StartLine,
                            n.Span.StartCol,
                            false));

                    if (isNewLine)
                    {
                        col = 0; ++line;
                    }
                    else
                    {
                        col += length;
                    }
                }
                else
                {
                    length = prefixLength + escapeId.ToString().Length;
                    ++escapeId;
                    transforms.Add(
                        new LineColTransform(
                            line,
                            col,
                            col + length - 1,
                            n.Span.StartLine,
                            n.Span.StartCol,
                            true));
                    col += length;
                }
            }

            lastLine = line;
        }

        public Span GetSourcePosition(Span span)
        {
            return GetSourcePosition(span.StartLine, span.StartCol, span.EndLine, span.EndCol);
        }

        public Span GetSourcePosition(int startLine, int startCol, int endLine, int endCol)
        {
            int srcLine1, srcCol1;
            int srcLine2, srcCol2;

            GetSourcePosition(startLine, startCol, out srcLine1, out srcCol1);
            GetSourcePosition(endLine, endCol, out srcLine2, out srcCol2);

            //// Don't trust that the user provided coordinates in a meaningful order.
            if (srcLine1 > srcLine2 || (srcLine1 == srcLine2 && srcCol1 > srcCol2))
            {
                return new Span(srcLine2, srcCol2, srcLine1, srcCol1);
            }
            else
            {
                return new Span(srcLine1, srcCol1, srcLine2, srcCol2);
            }
        }

        public void GetSourcePosition(int line, int col, out int srcLine, out int srcCol)
        {
            if (line < 0)
            {
                srcLine = quoteSpan.StartLine + line;
                srcCol = col + 1;
            }
            else if (line > lastLine || transforms.Count == 0)
            {
                srcLine = quoteSpan.EndLine + (line - lastLine);
                srcCol = col + 1;
            }
            else
            {
                LineColTransform trans;
                if (!transforms.Contains(new LineColTransform(line, col), out trans))
                {
                    trans = transforms.GetLargestElement();
                }

                srcLine = trans.srcLine;
                if (trans.isCollapsed)
                {
                    srcCol = trans.srcStartCol;
                }
                else
                {
                    srcCol = trans.srcStartCol + (col - trans.startCol);
                }
            }
        }

        private struct LineColTransform
        {
            /// <summary>
            /// The inclusive starting (line, startCol) pair in the stream text.
            /// The inclusive ending (line, endCol) pair in the stream text.
            /// It is assumed that line breaks always end a quoterun. 
            /// </summary>
            public int line;
            public int startCol;
            public int endCol;

            /// <summary>
            /// The corresponding starting (line, col) pair in the source text.
            /// </summary>
            public int srcLine;
            public int srcStartCol;

            /// <summary>
            /// True if the entire range maps to the source starting 
            /// </summary>
            public bool isCollapsed;

            /// <summary>
            /// Used to create a transform.
            /// </summary>
            public LineColTransform(int line, int startCol, int endCol, int srcLine, int srcStartCol, bool isCollapsed)
            {
                Contract.Requires(startCol <= endCol);
                this.line = line;
                this.startCol = startCol;
                this.endCol = endCol;
                this.srcLine = srcLine;
                this.srcStartCol = srcStartCol;
                this.isCollapsed = isCollapsed;
            }

            /// <summary>
            /// Used to create a probe in the set of transforms.
            /// </summary>
            public LineColTransform(int line, int col)
            {
                this.line = line;
                this.startCol = col;
                this.endCol = col;
                this.srcLine = 0;
                this.srcStartCol = 0;
                this.isCollapsed = false;
            }

            /// <summary>
            /// Two transform are considered the same if they overlap.
            /// Transforms should be non-overlapping, but probes will overlap with transforms.
            /// </summary>
            public static int Compare(LineColTransform t1, LineColTransform t2)
            {
                if (t1.line < t2.line)
                {
                    return -1;
                }
                else if (t2.line < t1.line)
                {
                    return 1;
                }
                else if (t1.startCol <= t2.startCol)
                {
                    return t1.endCol < t2.startCol ? -1 : 0;
                }
                else
                {
                    return t2.endCol < t1.startCol ? 1 : 0;
                }
            }

            public void Debug_Print()
            {
                if (isCollapsed)
                {
                    Console.WriteLine(
                        "({0}, {1} - {2}) --> ({3}, {4})",
                        line,
                        startCol,
                        endCol,
                        srcLine,
                        srcStartCol);
                }
                else
                {
                    Console.WriteLine(
                        "({0}, {1} - {2}) --> ({3}, {4} - {5})",
                        line,
                        startCol,
                        endCol,
                        srcLine,
                        srcStartCol,
                        endCol == int.MaxValue ? endCol : srcStartCol + (endCol - startCol));
                }
            }
        }
    }
}
