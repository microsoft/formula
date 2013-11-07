namespace FormulaBuild
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net.Http;
    using System.IO;
    using System.IO.Compression;
    using System.Reflection;

    internal static class SourceDownloader
    {
        public enum DependencyKind { Z3, GPPG, GPLEX };
        private const string ReferrerString = "http://{0}.codeplex.com/SourceControl/latest";
        private const string DownloadString = "http://download-codeplex.sec.s-msft.com/Download/SourceControlFileDownload.ashx?ProjectName={0}&changeSetId={1}";
        private const string GardensPointBootString = "http://download-codeplex.sec.s-msft.com/Download/Release?ProjectName=gplex&DownloadId=721249&FileTime=130217769402930000&Build=20821";
        private const string GardensPointReferrerString = "http://gplex.codeplex.com/releases/view/108701";
        private const string GardensPointBootFile = "..\\..\\..\\..\\..\\Ext\\GPLEX\\boot_.zip";
        private const string GardensPointBootDir = "..\\..\\..\\..\\..\\Ext\\GPLEX\\boot_";
        private const string GPPGName = "gplex-distro-1.2.1\\binaries\\Gppg.exe";
        private const string GPLexName = "gplex-distro-1.2.1\\binaries\\gplex.exe";
        private const string WinDirEnvVar = "WinDir";
        private const string CscName = "csc.exe";
        private const string MSbuildName = "msbuild.exe";

        private static readonly string[] FrameworkLocs = new string[]
        {
            "Microsoft.NET\\Framework64\\v4.0.30319",
            "Microsoft.NET\\Framework\\v4.0.30319"
        };

        private static readonly Tuple<string, string, string, string>[] Versions = new Tuple<string, string, string, string>[] 
        {
            new Tuple<string, string, string, string>("z3", "33f941aaec11bf7ef754d5779e581ba4a26b3018", "..\\..\\..\\..\\..\\Ext\\Z3\\z3_.zip", "..\\..\\..\\..\\..\\Ext\\Z3\\z3_\\"),
            new Tuple<string, string, string, string>("gppg", "84257", "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg_.zip", "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg_\\"),
            new Tuple<string, string, string, string>("gplex", "84980", "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex_.zip", "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex_\\")
        };

        public static bool GetFrameworkDir(out DirectoryInfo framework)
        {
            try
            {
                var winDir = Environment.GetEnvironmentVariable(WinDirEnvVar);
                foreach (var dir in FrameworkLocs)
                {
                    framework = new DirectoryInfo(Path.Combine(winDir, dir));
                    if (framework.Exists)
                    {
                        return true;
                    }
                }

                framework = null;
                return false;
            }
            catch (Exception e)
            {
                framework = null;
                Program.WriteError("Could not locate .NET framework directory - {0}", e.Message);
                return false;
            }
        }

        public static bool GetCsc(out FileInfo csc)
        {
            return GetFrameworkFile(CscName, out csc);
        }

        public static bool GetMsbuild(out FileInfo msbuild)
        {
            return GetFrameworkFile(MSbuildName, out msbuild);
        }

        public static bool Download(DependencyKind dep, out DirectoryInfo outputDir)
        {
            try
            {
                var projVersion = Versions[(int)dep];
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                var outputFile = new FileInfo(Path.Combine(runningLoc.DirectoryName, projVersion.Item3));
                outputDir = new DirectoryInfo(Path.Combine(runningLoc.DirectoryName, projVersion.Item4));
                // Kill existing directories
                if (outputFile.Exists)
                {
                    outputFile.Delete();
                }

                if (outputDir.Exists)
                {
                    outputDir.Delete(true);
                }

                // Create a New HttpClient object.
                Program.WriteInfo("Downloading dependency {0} to {1}...", projVersion.Item1, outputFile.FullName);
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Referrer = new Uri(string.Format(ReferrerString, projVersion.Item1));                
                using (var strm = client.GetStreamAsync(string.Format(DownloadString, projVersion.Item1, projVersion.Item2)).Result)
                {
                    using (var sw = new System.IO.StreamWriter(outputFile.FullName))
                    {
                        strm.CopyTo(sw.BaseStream);
                    }
                }

                Program.WriteInfo("Extracting dependency {0} to {1}...", projVersion.Item1, outputDir.FullName);
                ZipFile.ExtractToDirectory(outputFile.FullName, outputDir.FullName);                
            }
            catch (Exception e)
            {
                outputDir = null;
                Program.WriteError("Failed to get dependency {0} : {1}", dep, e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gardens point lexer / parser must be built using an existing build. 
        /// Must download a binary drop in order to bootstrap the build.
        /// </summary>
        /// <returns></returns>
        public static bool DownloadGardensPointBoot(out FileInfo gppg, out FileInfo gplex)
        {
            gppg = gplex = null;

            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                var outputFile = new FileInfo(Path.Combine(runningLoc.DirectoryName, GardensPointBootFile));
                var outputDir = new DirectoryInfo(Path.Combine(runningLoc.DirectoryName, GardensPointBootDir));
                // Kill existing directories
                if (outputFile.Exists)
                {
                    outputFile.Delete();
                }

                if (outputDir.Exists)
                {
                    outputDir.Delete(true);
                }

                // Create a New HttpClient object.
                Program.WriteInfo("Downloading dependency {0} to {1}...", "Gardens Point boot strapper", outputFile.FullName);
                HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Referrer = new Uri(GardensPointReferrerString);
                using (var strm = client.GetStreamAsync(GardensPointBootString).Result)
                {
                    using (var sw = new System.IO.StreamWriter(outputFile.FullName))
                    {
                        strm.CopyTo(sw.BaseStream);
                    }
                }

                Program.WriteInfo("Extracting dependency {0} to {1}...", "Gardens Point boot strapper", outputDir.FullName);
                ZipFile.ExtractToDirectory(outputFile.FullName, outputDir.FullName);

                gppg = new FileInfo(Path.Combine(outputDir.FullName, GPPGName));
                if (!gppg.Exists)
                {
                    Program.WriteError("Could not find {0} executable", gppg.FullName);
                    return false;
                }

                gplex = new FileInfo(Path.Combine(outputDir.FullName, GPLexName));
                if (!gplex.Exists)
                {
                    Program.WriteError("Could not find {0} executable", gplex.FullName);
                    return false;
                }
            }
            catch (Exception e)
            {
                Program.WriteError("Failed to get dependency {0} - {1}", "Gardens Point boot strapper", e.Message);
                return false;
            }

            return true;
        }

        private static bool GetFrameworkFile(string fileName, out FileInfo file)
        {
            try
            {
                DirectoryInfo framework;
                if (!GetFrameworkDir(out framework))
                {
                    Program.WriteError("Could not locate {0} - {1}", fileName, "missing .NET framework");
                }

                var files = framework.GetFiles(fileName, SearchOption.TopDirectoryOnly);
                Contract.Assert(files.Length <= 1);
                if (files.Length == 0)
                {
                    file = null;
                    return false;
                }
                else
                {
                    file = files[0];
                    return true;
                }
            }
            catch (Exception e)
            {
                file = null;
                Program.WriteError("Could not locate {0} - {1}", fileName, e.Message);
                return false;
            }
        }
    }
}
