namespace Microsoft.Formula.CommandLine
{
    using System;
    using System.IO;
    using API;

    public interface IMessageSink
    {
        TextWriter Writer { get; }

        void ResetPrintedError();

        void WriteMessage(string msg);
        
        void WriteMessage(string msg, SeverityKind severity);

        void WriteMessageLine(string msg);

        void WriteMessageLine(string msg, SeverityKind severity);       
    }
}
