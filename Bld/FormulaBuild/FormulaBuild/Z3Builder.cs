namespace FormulaBuild
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net.Http;
    using System.IO;
    using System.IO.Compression;
    using System.Reflection;
    using System.Diagnostics;

    internal static class Z3Builder
    {
        private const string Z3Buildx86 = "..\\..\\..\\..\\z3Buildx86.bat";
        private const string Z3Buildx64 = "..\\..\\..\\..\\z3Buildx64.bat";
        private const string Z3x86Drop = "..\\..\\..\\..\\..\\Ext\\Z3\\x86";
        private const string Z3x64Drop = "..\\..\\..\\..\\..\\Ext\\Z3\\x64";

        private static readonly string[] outputs = new string[]
        {
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.dll",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.exp",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.lib",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.pdb",
            //// "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\vc110.pdb",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\Microsoft.Z3.dll",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\Microsoft.Z3.pdb",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.exe",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.exp",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.lib",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.pdb",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.dll",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.exp",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.lib",
            //// "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\vc110.pdb",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\Microsoft.Z3.dll",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\Microsoft.Z3.pdb",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.exe",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.exp",
            "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.lib"
        };

        private static readonly Tuple<string, string>[] z3x86MoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>("build\\x86\\libz3.dll", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.dll"),
            new Tuple<string, string>("build\\x86\\libz3.exp", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.exp"),
            new Tuple<string, string>("build\\x86\\libz3.lib", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.lib"),
            new Tuple<string, string>("build\\x86\\libz3.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\libz3.pdb"),
            //// new Tuple<string, string>("build\\x86\\vc110.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\vc110.pdb"),
            new Tuple<string, string>("build\\x86\\Microsoft.Z3.dll", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\Microsoft.Z3.dll"),
            new Tuple<string, string>("build\\x86\\Microsoft.Z3.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\Microsoft.Z3.pdb"),
            new Tuple<string, string>("build\\x86\\z3.exe", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.exe"),
            new Tuple<string, string>("build\\x86\\z3.exp", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.exp"),
            new Tuple<string, string>("build\\x86\\z3.lib", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.lib"),
            new Tuple<string, string>("build\\x86\\z3.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x86\\z3.pdb")
        };

        private static readonly Tuple<string, string>[] z3x64MoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>("build\\x64\\libz3.dll", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.dll"),
            new Tuple<string, string>("build\\x64\\libz3.exp", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.exp"),
            new Tuple<string, string>("build\\x64\\libz3.lib", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\libz3.lib"),
            //// new Tuple<string, string>("build\\x64\\vc110.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\vc110.pdb"),
            new Tuple<string, string>("build\\x64\\Microsoft.Z3.dll", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\Microsoft.Z3.dll"),
            new Tuple<string, string>("build\\x64\\Microsoft.Z3.pdb", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\Microsoft.Z3.pdb"),
            new Tuple<string, string>("build\\x64\\z3.exe", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.exe"),
            new Tuple<string, string>("build\\x64\\z3.exp", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.exp"),
            new Tuple<string, string>("build\\x64\\z3.lib", "..\\..\\..\\..\\..\\Ext\\Z3\\x64\\z3.lib"),
        };

        public static bool Build(bool isRebuildForced)
        {
            if (!isRebuildForced && Verify(outputs))
            {
                Program.WriteInfo("Z3 dependencies have already been built; skipping this build step.");
                return true;
            }

            var result = true;
            DirectoryInfo z3Src = new DirectoryInfo(@"C:\Projects\Git\Formula\Ext\Z3\z3_");
            result = SourceDownloader.Download(SourceDownloader.DependencyKind.Z3, out z3Src) && result;
            if (!result)
            {
                Program.WriteError("Could not acquire Z3 dependency");
                return false;
            }

            FileInfo vcVars;
            result = SourceDownloader.GetVCVarsBat(out vcVars) && result;
            if (!result)
            {
                Program.WriteError("Could not find Visual Studio environment variables");
                return false;
            }

            Program.WriteInfo("Building Z3 for x86.");
            DirectoryInfo outDir;
            result = BuildPlatform(vcVars, Z3Buildx86) && 
                     SourceDownloader.GetBuildRelDir(Z3x86Drop, true, out outDir) &&
                     DoMove(z3Src, z3x86MoveMap) && 
                     result;
            if (!result)
            {
                Program.WriteError("Could not build z3 (x86)");
                return false;
            }

            Program.WriteInfo("Building Z3 for x64.");
            result = BuildPlatform(vcVars, Z3Buildx64) && 
                     SourceDownloader.GetBuildRelDir(Z3x64Drop, true, out outDir) &&
                     DoMove(z3Src, z3x64MoveMap) &&
                     result;
            if (!result)
            {
                Program.WriteError("Could not build z3 (x64)");
                return false;
            }

            return result;
        }

        public static void PrintOutputs()
        {
            foreach (var o in outputs)
            {
                Program.WriteInfo("Z3 dependency: {0}", o);
            }
        }

        /// <summary>
        /// Returns true if all outputs exist on the filesystem.
        /// Returns false if some output is missing or exception.
        /// </summary>
        /// <param name="outputs"></param>
        /// <returns></returns>
        private static bool Verify(string[] outputs)
        {
            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                foreach (var t in outputs)
                {
                    var outFile = new FileInfo(Path.Combine(runningLoc.Directory.FullName, t));
                    if (!outFile.Exists)
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool DoMove(DirectoryInfo srcRoot, Tuple<string, string>[] moveMap)
        {
            bool result = true;
            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                foreach (var t in moveMap)
                {
                    var inFile = new FileInfo(Path.Combine(srcRoot.FullName, t.Item1));
                    if (!inFile.Exists)
                    {
                        result = false;
                        Program.WriteError("Could not find output file {0}", inFile.Name);
                    }

                    inFile.CopyTo(Path.Combine(runningLoc.Directory.FullName, t.Item2), true);
                    Program.WriteInfo("Moved output {0} --> {1}", inFile.FullName, Path.Combine(runningLoc.Directory.FullName, t.Item2));
                }

                return result;
            }
            catch (Exception e)
            {
                Program.WriteError("Unable to move output files - {0}", e.Message);
                return false;
            }
        }

        private static bool BuildPlatform(FileInfo vcVars, string bat)
        {
            try
            {
                FileInfo batFile;
                if (!SourceDownloader.GetBuildRelFile(bat, out batFile))
                {
                    Program.WriteError("Could not find file {0}", bat);
                }

                var psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.WorkingDirectory = batFile.Directory.FullName;
                psi.FileName = batFile.FullName;
                psi.Arguments = string.Format("\"{0}\"", vcVars.FullName);
                psi.CreateNoWindow = true;

                var process = new Process();
                process.StartInfo = psi;
                process.OutputDataReceived += OutputReceived;
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();

                Program.WriteInfo("EXIT: {0}", process.ExitCode);
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Program.WriteError("Failed to build z3 ({0}) - {1}", bat, e.Message);
                return false;
            }
        }

        private static void OutputReceived(
            object sender,
            DataReceivedEventArgs e)
        {
            Console.WriteLine("OUT: {0}", e.Data);
        }
    }
}
