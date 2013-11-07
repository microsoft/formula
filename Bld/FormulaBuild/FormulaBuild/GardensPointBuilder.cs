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

    internal static class GardensPointBuilder
    {
        private const string gplexGenBat = "SpecFiles\\GenerateAll.bat";
        private const string gplexGenBatOut = "SpecFiles\\GenerateAll_Out.bat";

        private const string gppgGenBat = "ParserGenerator\\SpecFiles\\GenerateAll.bat";
        private const string gppgGenBatOut = "ParserGenerator\\SpecFiles\\GenerateAll_Out.bat";

        private const string gplexProj = "GPLEXv1.csproj";
        private const string gppgProj = "GPPG.csproj";
        private const string msbuildArgs = "\"{0}\" /p:Configuration=Release";

        private static readonly string[] outputs = new string[]
        {
            "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex45.exe",
            "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg45.exe"
        };

        private static readonly Tuple<string, string>[] gplexMoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>("bin\\debug\\gplex.exe", "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex45.exe"),
        };

        private static readonly Tuple<string, string>[] gppgMoveMap = new Tuple<string, string>[]
        {
            new Tuple<string, string>("bin\\debug\\gppg.exe", "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg45.exe"),
        };

        public static bool Build()
        {
            if (Verify(outputs))
            {
                Program.WriteInfo("Gardens Point dependencies have already been built; skipping this build step.");
                return true;
            }

            var result = true;
            FileInfo gppg, gplex;
            DirectoryInfo gplexSrc, gppgSrc;
            result = SourceDownloader.DownloadGardensPointBoot(out gppg, out gplex) && result;
            result = SourceDownloader.Download(SourceDownloader.DependencyKind.GPLEX, out gplexSrc) && result;
            result = SourceDownloader.Download(SourceDownloader.DependencyKind.GPPG, out gppgSrc) && result;
            if (!result)
            {
                Program.WriteError("Could not acquire Gardens Point dependencies");
                return false;
            }

            FileInfo csc;
            result = SourceDownloader.GetCsc(out csc) && result;
            if (!result)
            {
                Program.WriteError("Could not find CSharp compiler");
                return false;
            }

            FileInfo msbuild;
            result = SourceDownloader.GetMsbuild(out msbuild) && result;
            if (!result)
            {
                Program.WriteError("Could not find msbuild");
                return false;
            }


            //// Next try to compile gplex
            result = GenerateSpecFiles(gplexSrc, gplexGenBat, gplexGenBatOut, csc, gplex, gppg) &&
                     UpgradeAndCompile(gplexSrc, gplexProj, msbuild, "v4.5") &&
                     DoMove(gplexSrc, gplexMoveMap) &&
                     result;
            if (!result)
            {
                Program.WriteError("Could not compile the gplex dependency");
                return false;
            }

            //// Next try to compile gppg
            result = GenerateSpecFiles(gppgSrc, gppgGenBat, gppgGenBatOut, csc, gplex, gppg) &&
                     UpgradeAndCompile(gppgSrc, gppgProj, msbuild, "v4.5") &&
                     DoMove(gppgSrc, gppgMoveMap) &&
                     result;
            if (!result)
            {
                Program.WriteError("Could not compile the gppg dependency");
                return false;
            }

            return result;
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

        /// <summary>
        /// Upgrades the framework version of the projFile and then compiles the project.
        /// </summary>
        private static bool UpgradeAndCompile(DirectoryInfo srcRoot, string projFile, FileInfo msbuild, string frameworkVersion)
        {
            try
            {
                //// First, write bat file to create absolute paths
                var inProj = new FileInfo(Path.Combine(srcRoot.FullName, projFile));
                if (!inProj.Exists)
                {
                    Program.WriteError("Cannot find file {0}", inProj.FullName);
                    return false;
                }

                var outProj = new FileInfo(Path.Combine(srcRoot.FullName, projFile + ".tmp"));
                Console.WriteLine(outProj.FullName);
                using (var sr = new StreamReader(inProj.FullName))
                {
                    using (var sw = new StreamWriter(outProj.FullName))
                    {
                        while (!sr.EndOfStream)
                        {
                            var line = sr.ReadLine();
                            if (line.Trim().StartsWith("<TargetFrameworkVersion>", StringComparison.InvariantCultureIgnoreCase))
                            {
                                sw.WriteLine("<TargetFrameworkVersion>{0}</TargetFrameworkVersion>", frameworkVersion);
                            }
                            else if (!line.Contains("app.config"))
                            {
                                //// Filter out app.config, because some project appear to be broken w.r.t. this file.
                                sw.WriteLine(line);
                            }
                        }
                    }
                }

                inProj.Delete();
                outProj.MoveTo(inProj.FullName);

                var psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.WorkingDirectory = inProj.Directory.FullName;
                psi.FileName = msbuild.FullName;
                psi.Arguments = string.Format(msbuildArgs, inProj.FullName);
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
                Program.WriteError("Could not complile Gardens Point dependencies - {0}", e.Message);
                return false;
            }
        }

        private static bool GenerateSpecFiles(
            DirectoryInfo srcRoot, 
            string batFileIn, 
            string batFileOut, 
            FileInfo csc, 
            FileInfo gplex, 
            FileInfo gppg)
        {
            try
            {
                //// First, write bat file to create absolute paths
                var inBat = new FileInfo(Path.Combine(srcRoot.FullName, batFileIn));
                if (!inBat.Exists)
                {
                    Program.WriteError("Cannot find file {0}", inBat.FullName);
                    return false;
                }

                var outBat = new FileInfo(Path.Combine(srcRoot.FullName, batFileOut));
                using (var sr = new StreamReader(inBat.FullName))
                {
                    using (var sw = new StreamWriter(outBat.FullName))
                    {
                        while (!sr.EndOfStream)
                        {
                            sw.WriteLine(ExpandBatCall(sr.ReadLine(), csc, gplex, gppg));
                        }
                    }
                }

                var psi = new ProcessStartInfo();
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.WorkingDirectory = outBat.Directory.FullName;
                psi.FileName = outBat.FullName;
                psi.CreateNoWindow = true;

                var process = new Process();
                process.StartInfo = psi;
                process.OutputDataReceived += OutputReceived;
                process.Start();
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
                process.WaitForExit();

                Program.WriteInfo("EXIT: {0}", process.ExitCode);
                outBat.Delete();
                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Program.WriteError("Could not complile Gardens Point dependencies - {0}", e.Message);
                return false;
            }
        }

        private static string ExpandBatCall(string line, params FileInfo[] calls)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            line = line.Trim();
            foreach (var f in calls)
            {
                var nameWithExt = f.Name;
                var nameWithoutExt = f.Name.Substring(0, f.Name.IndexOf('.'));
                if (line.StartsWith(nameWithoutExt + " ", StringComparison.InvariantCultureIgnoreCase))
                {
                    return string.Format("\"{0}\" {1}", f.FullName, line.Substring(nameWithoutExt.Length));
                }
                else if (line.StartsWith(nameWithExt + " ", StringComparison.InvariantCultureIgnoreCase))
                {
                    return string.Format("\"{0}\" {1}", f.FullName, line.Substring(nameWithExt.Length));
                }
            }

            return line;
        }

        private static void OutputReceived(
            object sender,
            DataReceivedEventArgs e)
        {
            Console.WriteLine("OUT: {0}", e.Data);
        }
    }
}
