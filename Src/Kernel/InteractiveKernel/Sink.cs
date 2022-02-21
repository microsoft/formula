using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Formula.API;
using Microsoft.Formula.Common;
using Microsoft.Formula.CommandLine;

namespace Microsoft.Jupyter.Core
{
    public class Sink : IMessageSink
    {
        private StringBuilder _strBuilder;
        private StringWriter _tw;

        private StringBuilder _estrBuilder;
        private StringWriter _etw;

        private bool printedErr = false;
        private SpinLock printedErrLock = new SpinLock();
        public bool PrintedError
        {
            get
            {
                bool gotLock = false;
                try
                {
                    //// printedErrLock.Enter(ref gotLock);
                    return printedErr;
                }
                finally
                {
                    if (gotLock)
                    {
                        //// printedErrLock.Exit();
                    }
                }
            }
        }

        public Sink()
        {
            _strBuilder = new StringBuilder();
            _tw = new StringWriter(_strBuilder);

            Console.SetOut(_tw);

            _estrBuilder = new StringBuilder();
            _etw = new StringWriter(_estrBuilder);

            Console.SetError(_etw);
        }

        public string GetStdOut()
        {
            return _strBuilder.ToString();
        }

        public string GetStdErr()
        {
            return _estrBuilder.ToString();
        }

        public void Clear()
        {
            _strBuilder.Clear();
            _estrBuilder.Clear();
        }

        public System.IO.TextWriter Writer
        {
            get { return _tw; }
        }

        public void WriteMessage(string msg)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(msg);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessage(string msg, SeverityKind severity)
        {
            switch (severity)
            {
                case SeverityKind.Info:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(msg);
                    break;
                case SeverityKind.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.Write(msg);
                    break;
                case SeverityKind.Error:
                    SetPrintedError();
                    Console.Error.Write(msg);
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.Write(msg);
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
            }

            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessageLine(string msg)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public void WriteMessageLine(string msg, SeverityKind severity)
        {
            switch (severity)
            {
                case SeverityKind.Info:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(msg);
                    break;
                case SeverityKind.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Error.WriteLine(msg);
                    break;
                case SeverityKind.Error:
                    SetPrintedError();
                    Console.Error.WriteLine(msg);
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(msg);
                    break;
            }

            Console.ForegroundColor = ConsoleColor.Gray;
        }

        private void SetPrintedError()
        {
            bool gotLock = false;
            try
            {
                printedErrLock.Enter(ref gotLock);
                printedErr = true;
            }
            finally
            {
                if (gotLock)
                {
                    printedErrLock.Exit();
                }
            }
        }
    }
}
