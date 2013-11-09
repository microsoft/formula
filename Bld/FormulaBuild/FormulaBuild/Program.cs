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

        static void Main(string[] args)     
        {
            if (args.Length > 1)
            {
                WriteError("Unexpected number of arguments");
                PrintUsage();
                Environment.ExitCode = FailCode;
                return;
            }

            if (args.Length == 1 && args[0].Trim() == HelpFlag)
            {
                PrintUsage();
                return;
            }

            if (args.Length == 1 && args[0].Trim() == LayoutFlag)
            {
                GardensPointBuilder.PrintOutputs();
                Z3Builder.PrintOutputs();
                return;
            }

            bool isDebug = false;
            if (args.Length == 1)
            {
                if (args[0].Trim() == DebugFlag)
                {
                    isDebug = true;
                }
                else
                {
                    WriteError("Unexpected flag {0}", args[0]);
                    PrintUsage();
                    Environment.ExitCode = FailCode;
                    return;
                }
            }

            WriteInfo("Building in {0} configuration", isDebug ? "debug" : "release");

            var result = GardensPointBuilder.Build() &&
                         Z3Builder.Build() &&
                         FormulaBuilder.Build(isDebug);

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
            Program.WriteInfo("USAGE: build.bat [{0} | {1} | {2}]", HelpFlag, DebugFlag, LayoutFlag);
            Program.WriteInfo("{0}: Prints this message", HelpFlag);
            Program.WriteInfo("{0}: Build debug versions for Formula", DebugFlag);
            Program.WriteInfo("{0}: The expected layout of external dependencies (relative to FormulaBuild.exe)", LayoutFlag);
        }
    }
}
