using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Antlr4.Runtime;

namespace Core.API.Parser
{
    class FormulaLexerErrorListener : Antlr4.Runtime.BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {

            System.Console.WriteLine("Error at line {0} column {1}: found token {2}. Expected: ", line, charPositionInLine, offendingSymbol.Text);

            foreach (var item in e.GetExpectedTokens().ToList())
            {
                System.Console.Write(FormulaLexer.DefaultVocabulary. GetSymbolicName(item) + ", ");
            }
            base.SyntaxError(output, recognizer, offendingSymbol, line, charPositionInLine, msg, e);
        }
    }
}
