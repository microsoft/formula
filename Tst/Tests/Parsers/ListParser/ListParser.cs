namespace ListParser
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Formula.API;
    using Microsoft.Formula.API.ASTQueries;
    using Microsoft.Formula.API.Nodes;
    using Microsoft.Formula.API.Plugins;
    using Microsoft.Formula.Compiler;
    using Microsoft.Formula.Common;

    public class ListParser : IQuoteParser
    {
        private enum ParserState { None, Nat, Id };

        private static readonly Tuple<string, CnstKind, string>[] noSettings = new Tuple<string, CnstKind, string>[0];
        private static readonly AST<Domain> listDomain;
        private const string consName = "Cons";
        private const string nilName = "NIL";
        private const char idPrefix = '$';
        private const string idPrefixStr = "$";
        private const string listDomainStr =
        @"
        domain Lists 
        {{
            {0} ::= new (left: any {0} + Natural + {{ {1} }}, right: any {0} + Natural + {{ {1} }}).
        }}
        ";

        static ListParser()
        {
            var progName = new ProgramName("env:///temp.4ml");
            var progText = string.Format(listDomainStr, consName, nilName);
            var task = Factory.Instance.ParseText(progName, progText);
            task.Wait();
            if (!task.Result.Succeeded)
            {
                throw new Exception("Could not parse domain definition");
            }

            var query = new NodePred[] { 
              NodePredFactory.Instance.Star, 
              NodePredFactory.Instance.MkPredicate(NodeKind.Domain) };
            listDomain = (AST<Domain>)task.Result.Program.FindAny(query);
        }

        public string UnquotePrefix
        {
            get { return idPrefixStr; }
        }

        public AST<Domain> SuggestedDataModel
        {
            get { return listDomain; }
        }

        public string Description
        {
            get { return "Implements a sample parser for lists"; }
        }

        public IEnumerable<Tuple<string, CnstKind, string>> SuggestedSettings
        {
            get { return noSettings; }
        }

        public ListParser()
        {
        }

        public IQuoteParser CreateInstance(
                            AST<Node> module,
                            string collectionName,
                            string instanceName)
        {
            return new ListParser();
        }

        public bool Parse(
                Configuration config,
                Stream quoteStream,
                SourcePositioner positioner,
                out AST<Node> results,
                out List<Flag> flags)
        {
            results = null;
            flags = new List<Flag>();
            var lines = new LinkedList<string>();
            var revList = new LinkedList<AST<Node>>();
            using (var sr = new StreamReader(quoteStream))
            {
                while (!sr.EndOfStream)
                {
                    lines.AddLast(sr.ReadLine());
                }
            }

            char c;
            string token = string.Empty;
            var state = ParserState.None;
            int lineNum = 0, colNum = 0;
            int sL = 0, sC = 0;
            foreach (var line in lines)
            {
                for (colNum = 0; colNum < line.Length; ++colNum)
                {
                    c = line[colNum];
                    if (char.IsDigit(c))
                    {
                        token += c;
                        if (state == ParserState.None)
                        {
                            state = ParserState.Nat;
                            sL = lineNum;
                            sC = colNum;
                        }
                    }
                    else if (c == idPrefix)
                    {
                        if (state == ParserState.None)
                        {
                            token = idPrefixStr;
                            state = ParserState.Id;
                            sL = lineNum;
                            sC = colNum;
                        }
                        else 
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                positioner.GetSourcePosition(lineNum, colNum, lineNum, colNum),
                                Constants.QuotationError.ToString("Unexpected character " + c),
                                Constants.QuotationError.Code));
                            return false;
                        }
                    }
                    else if (c == ' ' || c == '\t')
                    {
                        if (state == ParserState.Nat)
                        {
                            Contract.Assert(token.Length > 0);
                            Rational r;
                            Rational.TryParseFraction(token, out r);
                            revList.AddFirst(Factory.Instance.MkCnst(r, positioner.GetSourcePosition(sL, sC, lineNum, colNum - 1)));
                            token = string.Empty;
                        }
                        else if (state == ParserState.Id)
                        {
                            if (token.Length < 2)
                            {
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    positioner.GetSourcePosition(lineNum, colNum, lineNum, colNum),
                                    Constants.QuotationError.ToString("Bad id"),
                                    Constants.QuotationError.Code));
                                return false;
                            }

                            revList.AddFirst(Factory.Instance.MkId(token, positioner.GetSourcePosition(sL, sC, lineNum, colNum - 1)));
                            token = string.Empty;
                        }

                        state = ParserState.None;
                    }
                    else
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            positioner.GetSourcePosition(lineNum, colNum, lineNum, colNum),
                            Constants.QuotationError.ToString("Unexpected character " + c),
                            Constants.QuotationError.Code));
                        return false;
                    }
                }

                if (state == ParserState.Nat)
                {
                    Contract.Assert(token.Length > 0);
                    Rational r;
                    Rational.TryParseFraction(token, out r);
                    revList.AddFirst(Factory.Instance.MkCnst(r, positioner.GetSourcePosition(sL, sC, lineNum, colNum)));
                }
                else if (state == ParserState.Id)
                {
                    if (token.Length < 2)
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            positioner.GetSourcePosition(lineNum, colNum, lineNum, colNum),
                            Constants.QuotationError.ToString("Bad id"),
                            Constants.QuotationError.Code));
                        return false;
                    }

                    revList.AddFirst(Factory.Instance.MkId(token, positioner.GetSourcePosition(sL, sC, lineNum, colNum)));
                }

                token = string.Empty;
                state = ParserState.None;
                ++lineNum;
            }

            if (revList.Count == 0)
            {
                results = Factory.Instance.MkId(nilName, positioner.GetSourcePosition(0, 0, 0, 0));
            }
            else 
            {
                foreach (var item in revList)
                {
                    if (results == null)
                    {
                        results = item;
                    }
                    else
                    {
                        results = Factory.Instance.AddArg(
                                    Factory.Instance.MkFuncTerm(Factory.Instance.MkId(consName, item.Node.Span), item.Node.Span),
                                    results);
                        results = Factory.Instance.AddArg((AST<FuncTerm>)results, item, false);
                    }
                }
            }

            return true;
        }

        public bool Render(
                Configuration config,
                TextWriter writer,
                AST<Node> ast,
                out List<Flag> flags)
        {
            throw new NotImplementedException();
        }
    }
}
