namespace Microsoft.Formula.CommandLine
{
    using System;
    using System.Threading;
    using API;

    public sealed class CommandLineProgram
    {
        public static void Main(string[] args)
        {
            var sink = new ConsoleSink();
            var chooser = new ConsoleChooser();
            var envParams = new EnvParams();
            using (var ci = new CommandInterface(sink, chooser, envParams))
            {
                Console.CancelKeyPress += (x, y) => 
                { 
                    y.Cancel = true;
                    ci.Cancel();                 
                };

                //// If errors occured while parsing switches
                //// then treat this an exit condition.
                bool isExit;
                ci.DoOptions(out isExit);
                if (isExit || sink.PrintedError)
                {
                    Environment.ExitCode = sink.PrintedError ? 1 : 0;
                    return;
                }

                if (OperatingSystem.IsMacOS())
                {
                    InteractivePrompt.Run(ci);
                    Environment.ExitCode = sink.PrintedError ? 1 : 0;
                    return;
                }

                string line;
                while (true)
                {
                    //// Because pressing CTRL-C may return a null line
                    line = Console.ReadLine();
                    //// Exit on CTRL-D
                    if (line == null)
                    {
                        Environment.ExitCode = sink.PrintedError ? 1 : 0;
                        return;
                    }
                    line = line == null ? string.Empty : line.Trim();
                    if (line == CommandInterface.ExitCommand ||
                        line == CommandInterface.ExitShortCommand)
                    {
                        Environment.ExitCode = sink.PrintedError ? 1 : 0;
                        return;
                    }

                    ci.DoCommand(line);
                }
            }
        }

        public sealed class ConsoleChooser : IChooser
        {
            public ConsoleChooser()
            {
                Interactive = true;
            }

            public bool Interactive { get; set; }

            public bool GetChoice(out DigitChoiceKind choice)
            {
                if (!Interactive)
                {
                    choice = DigitChoiceKind.Zero;
                    return true;
                }

                string line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line) ||
                    !char.IsDigit(line, 0))
                {
                    choice = DigitChoiceKind.Zero;
                    return false;
                }
                switch (line[0])
                {
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        choice = (DigitChoiceKind)(line[0] - '0');
                        return true;
                    default:
                        choice = DigitChoiceKind.Zero;
                        return false;
                }
            }
        }

        public sealed class ConsoleSink : IMessageSink
        {
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

            public System.IO.TextWriter Writer
            {
                get { return Console.Out; }
            }

            public void WriteMessage(string msg)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }

            public void WriteMessage(string msg, API.SeverityKind severity)
            {
                switch (severity)
                {
                    case API.SeverityKind.Info:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case API.SeverityKind.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case API.SeverityKind.Error:
                        SetPrintedError();
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }

                Console.Write(msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }

            public void WriteMessageLine(string msg)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(msg);
                Console.ForegroundColor = ConsoleColor.Gray;
            }

            public void WriteMessageLine(string msg, API.SeverityKind severity)
            {
                switch (severity)
                {
                    case API.SeverityKind.Info:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case API.SeverityKind.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case API.SeverityKind.Error:
                        SetPrintedError();
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }

                Console.WriteLine(msg);
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
}
