namespace FormulaBuild
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    class Program
    {
        private const int FailCode = 1;
        private const string DebugFlag = "-d";
        private const string HelpFlag = "-h";
        private const string LayoutFlag = "-l";
        private const string ExtFlag = "-e";

        bool isDebug = false;
        bool isForced = false;
        bool layout = false;
        bool solver = true;

        bool ParseCommandLine(string[] args)
        {
            for (int i = 0, n = args.Length; i < n; i++)
            {
                string arg = args[i];
                if (arg[0] == '/' || arg[0] == '-')
                {
                    switch (arg.Substring(1).ToLowerInvariant())
                    {
                        case "h":
                        case "?":
                        case "help":
                            return false;
                        case "l":
                            layout = true;
                            break;
                        case "d":
                            isDebug = true;
                            break;
                        case "e":
                            isForced = true;
                            break;
                        case "solver":
                            if (i+1<n )
                            {
                                bool s;
                                if (bool.TryParse(args[++i], out s))
                                {
                                    solver = s;
                                }
                                else
                                {
                                    WriteError("Expecting 'true' or 'false' after -solver argument, but found: {0}", args[i]);
                                    return false;
                                }
                            }
                            break;
                        default:
                            WriteError("Unexpected flag {0}", arg);
                            return false;
                    }
                }
                else
                {
                    WriteError("Unexpected argument: {0}", arg);
                    return false;
                }
            }
            return true;
        }

        static void Main(string[] args)
        {
            Program p = new FormulaBuild.Program();
            if (!p.ParseCommandLine(args))
            {
                PrintUsage();
                Environment.ExitCode = FailCode;
                return;
            }
            p.Run();
        }

        void Run()
        { 
            if (layout)
            {
                SourceDownloader.PrintSourceURLs();
                GardensPointBuilder.PrintOutputs();
                Z3Builder.PrintOutputs();
                return;
            }

            string python = FindInPath("Python.exe");
            if (python == null)
            {
                Program.WriteError("Could not find Python, please install Python 2.7");
                Program.WriteError("and make sure the location is in your PATH environment.");
                Program.WriteError("See: https://www.python.org/downloads/release");
                return;
            }

            WriteInfo("Building in {0} configuration", isDebug ? "debug" : "release");

            var result = GardensPointBuilder.Build(isForced);

            if (solver)
            {
                result |= Z3Builder.Build(isForced);
            }

            result |= FormulaBuilder.Build(isDebug, solver, isForced);

            if (!result)
            {
                WriteError("Build failed");
                Environment.ExitCode = FailCode;
                return;
            }
            else
            {
                WriteInfo("Build succeeded");
            }
        }

        public static void WriteError(string format, params object[] args)
        {
            var crnt = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: " + format, args);
            Console.ForegroundColor = crnt;
        }

        public static void WriteWarning(string format, params object[] args)
        {
            var crnt = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: " + format, args);
            Console.ForegroundColor = crnt;
        }

        public static void WriteInfo(string format, params object[] args)
        {
            var crnt = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(format, args);
            Console.ForegroundColor = crnt;
        }

        private static void PrintUsage()
        {
            Program.WriteInfo("USAGE: build.bat [{0} | {1} | {2} | {3}]", HelpFlag, DebugFlag, LayoutFlag, ExtFlag);
            Program.WriteInfo("{0}: Prints this message", HelpFlag);
            Program.WriteInfo("{0}: Build debug versions for Formula", DebugFlag);
            Program.WriteInfo("{0}: The expected layout of external dependencies (relative to FormulaBuild.exe)", LayoutFlag);
            Program.WriteInfo("{0}: Force rebuild of external dependencies", DebugFlag);
        }

        private static string FindInPath(string tool)
        {
            foreach (string s in Environment.GetEnvironmentVariable("PATH").Split(';'))
            {
                string fullPath = Path.Combine(s, tool);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            return null;
        }

    }
}
