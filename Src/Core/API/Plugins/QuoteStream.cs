namespace Microsoft.Formula.API.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.IO;
    using System.Threading;
    
    using Common;
    using Nodes;

    /// <summary>
    /// Exposes a seekable stream from a quote. Unquotes within the quote are replaced with textual Ids.
    /// Canceling the stream permanently moves the position to the end of the stream. 
    /// </summary>
    internal class QuoteStream : Stream
    {
        private static int CancelFreq = 500;

        private long length;
        private string prefix;        
        private long currentPos = 0;
        private IEnumerator<KeyValuePair<Interval, QuoteData>> current;

        private int cancelCount = 1;
        private bool isCanceled = false;
        private CancellationToken cancel;

        private Map<Interval, QuoteData> byteMap = 
            new Map<Interval, QuoteData>(Interval.Compare);

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }
    
        public QuoteStream(Quote quote, string prefix, CancellationToken cancel = default(CancellationToken))
        {
            this.prefix = prefix;
            this.cancel = cancel;
            BuildByteMap(quote);
        }

        public override long Length
        {
            get { return length; }
        }

        public override long Position
        {
            get
            {
                return currentPos;
            }
            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count <= 0 || currentPos >= Length)
            {
                return 0;
            }
            else if (IsCanceled())
            {
                return 0;
            }

            int returned = 0;
            Interval crntIntr;
            string crntString = GetCurrentString(out crntIntr);
            while (returned < count)
            {
                buffer[offset + returned] = (byte)crntString[(int)(currentPos - crntIntr.start)];
                ++returned;
                ++currentPos;

                if (currentPos == Length)
                {
                    break;
                }
                else if (currentPos > crntIntr.end)
                {
                    var didMove = current.MoveNext();
                    Contract.Assert(didMove);
                    crntString = GetCurrentString(out crntIntr);
                }
            }

            return returned;
        }
     
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (IsCanceled())
            {
                return currentPos;
            }

            switch (origin)
            {
                case SeekOrigin.Begin:
                    currentPos = offset;
                    break;
                case SeekOrigin.Current:
                    currentPos = currentPos + offset;
                    break;
                case SeekOrigin.End:
                    currentPos = Length + offset - 1;
                    break;
            }

            if (currentPos < 0 || currentPos > Length)
            {
                throw new IOException("Invalid Seek operation on stream");
            }

            current = byteMap.GetEnumerable(new Interval(currentPos, currentPos)).GetEnumerator();
            current.MoveNext();
            return currentPos;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        private void BuildByteMap(Quote quote)
        {
            Interval i;
            long nextEscapeId = 0;
            long nextIntervalStart = 0;
            foreach (var c in quote.Contents)
            {
                if (IsCanceled())
                {
                    currentPos = length = nextIntervalStart;
                    return;
                }
                else if (c.NodeKind == NodeKind.QuoteRun)
                {
                    var qr = (QuoteRun)c;
                    if (!string.IsNullOrEmpty(qr.Text))
                    {
                        i = new Interval(nextIntervalStart, nextIntervalStart + qr.Text.Length - 1);
                        byteMap.Add(i, new QuoteData(c, -1));
                        nextIntervalStart = i.end + 1;
                    }
                }
                else
                {
                    i = new Interval(nextIntervalStart, nextIntervalStart + prefix.Length + nextEscapeId.ToString().Length - 1);
                    byteMap.Add(i, new QuoteData(c, nextEscapeId++));
                    nextIntervalStart = i.end + 1;
                }
            }

            current = byteMap.GetEnumerator();
            current.MoveNext();
            length = nextIntervalStart;
        }

        private string GetCurrentString(out Interval interval)
        {
            var crnt = current.Current;
            interval = crnt.Key;
            if (crnt.Value.id < 0)
            {
                return ((QuoteRun)crnt.Value.item).Text;
            }
            else
            {
                return prefix + crnt.Value.id.ToString();
            }
        }

        private bool IsCanceled()
        {
            if (isCanceled)
            {
                return true;
            }
            else if (cancelCount % CancelFreq == 0)
            {
                cancelCount = 1;
                if (cancel.IsCancellationRequested)
                {
                    isCanceled = true;
                    currentPos = Length;
                    return true;
                }
            }
            else
            {
                ++cancelCount;
            }

            return false;
        }

        private struct QuoteData
        {
            public Node item;
            public long id;

            public QuoteData(Node quoteItem, long idIndex)
            {
                this.item = quoteItem;
                this.id = idIndex;
            }
        }

        private struct Interval
        {
            public long start;
            public long end;

            public Interval(long start, long end)
            {
                this.start = start;
                this.end = end;
            }

            /// <summary>
            /// The stream shall contain a set of non-overlapping intervals
            /// mapping bytes to QuoteItems. For this purpose the compare method
            /// considers two intervals to be the same is one contains the other.
            /// Then when seeking to a certain location k, the index containing
            /// that location is found by searching for the interval [k,k]
            /// </summary>
            public static int Compare(Interval i1, Interval i2)
            {
                if (i1.start >= i2.start && i1.end <= i2.end)
                {
                    return 0;
                }
                else if (i2.start >= i1.start && i2.end <= i1.end)
                {
                    return 0;
                }
                else if (i1.end < i2.start)
                {
                    return -1;
                }
                else
                {
                    return 1;
                }
            }
        }
    }
}
