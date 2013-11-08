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
        static void Main(string[] args)     
        {
            var result = GardensPointBuilder.Build() &&
                         Z3Builder.Build();

            if (!result)
            {
                WriteError("Build failed");
                Environment.ExitCode = FailCode;
                return;
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
    }
}
