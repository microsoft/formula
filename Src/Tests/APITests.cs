using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    public class APITests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        public APITests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            
        }

        [Fact]
        public void TestLexer()
        {
            ICharStream charStream = Antlr4.Runtime.CharStreams.fromstring("partial model {}");
            FormulaLexer lexer = new FormulaLexer(charStream);
            CommonTokenStream tokenStream = new CommonTokenStream(lexer);
            tokenStream.Fill();
            IList<IToken> tokens = tokenStream.GetTokens();
            Assert.Equal(5, tokens.Count);
            Assert.Equal(FormulaLexer.PARTIAL, tokens[0].Type);
            Assert.Equal(FormulaLexer.MODEL, tokens[1].Type);
            Assert.Equal(FormulaLexer.LCBRACE, tokens[2].Type);
            Assert.Equal(FormulaLexer.RCBRACE, tokens[3].Type);
            Assert.Equal(FormulaLexer.Eof, tokens[4].Type);
        }

        [Fact]
        public void TestParser()
        {
            IList<IToken> tokens = new List<IToken>();

            CommonToken modelToken = new CommonToken(FormulaParser.MODEL, "model");
            CommonToken domainToken = new CommonToken(FormulaParser.DOMAIN, "domain");
            CommonToken eofToken = new CommonToken(FormulaParser.Eof, "EOF");
            
            tokens.Add(modelToken);
            tokens.Add(domainToken);
            tokens.Add(eofToken);

            ITokenSource lts = new ListTokenSource(tokens);
            CommonTokenStream cts = new CommonTokenStream(lts);
            FormulaParser parser = new FormulaParser(cts);
            FormulaParser.ProgramContext pt = parser.program();
            
            Assert.Contains("domain", pt.GetText());
        }
    }
}