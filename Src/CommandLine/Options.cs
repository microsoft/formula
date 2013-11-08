namespace Microsoft.Formula.CommandLine
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;
    using Microsoft.Formula.Common;

    internal enum OptValueKind { Id, Integer, String }

    internal class Options
    {
        private OptValueKind? kind = null;
        private string token = "";

        private LinkedList<Tuple<string, LinkedList<Tuple<OptValueKind, object>>>> options =
            new LinkedList<Tuple<string, LinkedList<Tuple<OptValueKind, object>>>>();

        public IEnumerable<Tuple<string, LinkedList<Tuple<OptValueKind, object>>>> OptionLists
        {
            get
            {
                return options;
            }
        }

        public void StartToken(OptValueKind? kind, char c = '\0')
        {
            this.kind = kind;
            token = "";
            if (c != '\0')
            {
                token += c;
            }
        }

        public void AppendToken(char c)
        {
            token += c;
        }

        public void EndToken()
        {
            if (token == "" && kind == null)
            {
                return;
            }
            else if (kind == null)
            {
                options.AddLast(
                    new Tuple<string, LinkedList<Tuple<OptValueKind, object>>>(
                        token,
                        new LinkedList<Tuple<OptValueKind, object>>()));
            }
            else if (kind == OptValueKind.Integer)
            {
                Contract.Assert(options.Count > 0);
                var opt = options.Last.Value.Item2;
                Rational rat;
                var canParse = Rational.TryParseDecimal(token, out rat);
                Contract.Assert(canParse);
                opt.AddLast(new Tuple<OptValueKind, object>((OptValueKind)kind, rat));
            }
            else if (kind == OptValueKind.Id)
            {
                Contract.Assert(options.Count > 0);
                Contract.Assert(!string.IsNullOrEmpty(token));
                var opt = options.Last.Value.Item2;
                opt.AddLast(new Tuple<OptValueKind, object>((OptValueKind)kind, token));
            }
            else if (kind == OptValueKind.String)
            {
                Contract.Assert(options.Count > 0);
                var opt = options.Last.Value.Item2;
                opt.AddLast(new Tuple<OptValueKind, object>((OptValueKind)kind, token));
            }
            else
            {
                throw new NotImplementedException();
            }

            token = "";
            kind = null;
        }
    }
}
