namespace FormulaBuild
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.IO;
    using System.IO.Compression;
    using System.Reflection;
    using Microsoft.Win32;

    internal static class SourceDownloader
    {
        public enum DependencyKind { Z3, GPPG, GPLEX };
        private const string ReferrerString = "http://{0}.codeplex.com/SourceControl/latest";
        private const string DownloadString = "http://download-codeplex.sec.s-msft.com/Download/SourceControlFileDownload.ashx?ProjectName={0}&changeSetId={1}";
        private const string GardensPointReferrerString = "http://gplex.codeplex.com/releases/view/108701";
        private const string GardensPointBootDir = "..\\..\\..\\..\\..\\Ext\\GPLEX\\boot";
        private const string GPPGName = "gppg.exe";
        private const string GPLexName = "gplex.exe";
        private const string WinDirEnvVar = "WinDir";
        private const string CscName = "csc.exe";
        private const string MSbuildName = "msbuild.exe";
        private const string VsixInstallerName = "Common7\\IDE\\vsixinstaller.exe";
        private const string RegVS32SubKey = "SOFTWARE\\Microsoft\\VisualStudio\\{0}.0\\Setup\\VS";
        private const string RegVS64SubKey = "SOFTWARE\\Wow6432Node\\Microsoft\\VisualStudio\\{0}.0\\Setup\\VS";
        private const string VCVarsAll = "VC\\vcvarsall.bat";
        private const string Z3Version = "z3-4.4.0";

        private static readonly string[] FrameworkLocs = new string[]
        {
            "Microsoft.NET\\Framework64\\v4.0.30319",
            "Microsoft.NET\\Framework\\v4.0.30319"
        };

        private static readonly string[] FrameworkLocs32 = new string[]
        {
            "Microsoft.NET\\Framework\\v4.0.30319"
        };

        private static readonly Tuple<string, string, string, string>[] Versions = new Tuple<string, string, string, string>[] 
        {
            new Tuple<string, string, string, string>("z3", "52b54f395b88926703ed390b04ea7767bbadacd3", "..\\..\\..\\..\\..\\Ext\\Z3\\z3_.zip", "..\\..\\..\\..\\..\\Ext\\Z3\\z3_\\"),
            new Tuple<string, string, string, string>("gppg", "84257", "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg_.zip", "..\\..\\..\\..\\..\\Ext\\GPPG\\gppg_\\"),
            new Tuple<string, string, string, string>("gplex", "84980", "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex_.zip", "..\\..\\..\\..\\..\\Ext\\GPLEX\\gplex_\\")
        };

        public static bool GetBuildRelDir(string dirname, bool shouldCreate, out DirectoryInfo dir)
        {
            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                dir = new DirectoryInfo(Path.Combine(runningLoc.Directory.FullName, dirname));
                if (!dir.Exists && shouldCreate)
                {
                    dir.Create();
                    return true;
                }
                else
                {
                    return dir.Exists;
                }
            }
            catch (Exception e)
            {
                dir = null;
                Program.WriteError("Could not locate dir {0} - {1}", dirname, e.Message);
                return false;
            }
        }

        public static void PrintSourceURLs()
        {
            foreach (var v in Versions)
            {
                Program.WriteInfo("Source code: " + DownloadString, v.Item1, v.Item2);
            }
        }

        public static bool GetBuildRelFile(string filename, out FileInfo file)
        {
            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                file = new FileInfo(Path.Combine(runningLoc.Directory.FullName, filename));
                return file.Exists;
            }
            catch (Exception e)
            {
                file = null;
                Program.WriteError("Could not locate file {0} - {1}", filename, e.Message);
                return false;
            }
        }

        public static bool GetFrameworkDir(out DirectoryInfo framework, bool force32Bit = false)
        {
            try
            {
                var winDir = Environment.GetEnvironmentVariable(WinDirEnvVar);
                var locs = force32Bit ? FrameworkLocs32 : FrameworkLocs;
                foreach (var dir in locs)
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

        /// <summary>
        /// Gets the location of the latest vcVars on this machine.
        /// </summary>
        public static bool GetVCVarsBat(out FileInfo vcVars)
        {
            try
            {
                DirectoryInfo vsDir;
                if (!GetVSDir(out vsDir))
                {
                    vcVars = null;
                    return false;
                }

                vcVars = new FileInfo(Path.Combine(vsDir.FullName, VCVarsAll));
                return vcVars.Exists;
            }
            catch (Exception e)
            {
                vcVars = null;
                Program.WriteError("Could not find a Visual Studio component - {0}", e.Message);
                return false;
            }
        }

        public static bool GetVsixInstaller(out FileInfo vsixInstaller)
        {
            try
            {
                DirectoryInfo vsDir;
                if (!GetVSDir(out vsDir))
                {
                    vsixInstaller = null;
                    return false;
                }

                vsixInstaller = new FileInfo(Path.Combine(vsDir.FullName, VsixInstallerName));
                return vsixInstaller.Exists;
            }
            catch (Exception e)
            {
                vsixInstaller = null;
                Program.WriteError("Cannot get vsix installer - {0}", e.Message);
                return false;
            }
        }

        public static bool GetCsc(out FileInfo csc)
        {
            return GetFrameworkFile(CscName, out csc);
        }

        public static bool GetMsbuild(out FileInfo msbuild, bool force32Bit = false)
        {
            return GetFrameworkFile(MSbuildName, out msbuild, force32Bit);
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

                // Z3 cannot be downloaded from Codeplex anymore.
                if (dep == DependencyKind.Z3)
                {
                    //specify to use TLS 1.2 as default connection
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    client.DefaultRequestHeaders.Accept.Clear();
                    string Z3DownloadString = String.Format("https://github.com/Z3Prover/z3/archive/{0}.zip", Z3Version);
                    using (var strm = client.GetStreamAsync(Z3DownloadString).Result)
                    {
                        using (var sw = new System.IO.StreamWriter(outputFile.FullName))
                        {
                            strm.CopyTo(sw.BaseStream);
                        }
                    }
                }
                else
                {
                    client.DefaultRequestHeaders.Referrer = new Uri(string.Format(ReferrerString, projVersion.Item1));
                    using (var strm = client.GetStreamAsync(string.Format(DownloadString, projVersion.Item1, projVersion.Item2)).Result)
                    {
                        using (var sw = new System.IO.StreamWriter(outputFile.FullName))
                        {
                            strm.CopyTo(sw.BaseStream);
                        }
                    }
                }

                Program.WriteInfo("Extracting dependency {0} to {1}...", projVersion.Item1, outputDir.FullName);

                // Move z3 source files to directory one level higher
                if (dep == DependencyKind.Z3)
                {
                    ZipFile.ExtractToDirectory(outputFile.FullName, outputDir.Parent.FullName);
                    System.IO.Directory.Move(Path.Combine(outputDir.Parent.FullName, "z3-" + Z3Version), outputDir.FullName);
                }
                else
                {
                    ZipFile.ExtractToDirectory(outputFile.FullName, outputDir.FullName);
                }
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
        /// Must use a cached binary in order to bootstrap the build.
        /// </summary>
        /// <returns></returns>
        public static bool DownloadGardensPointBoot(out FileInfo gppg, out FileInfo gplex)
        {
            gppg = gplex = null;

            try
            {
                var runningLoc = new FileInfo(Assembly.GetExecutingAssembly().Location);
                var outputDir = new DirectoryInfo(Path.Combine(runningLoc.DirectoryName, GardensPointBootDir));
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

        private static bool GetVSDir(out DirectoryInfo vsDir)
        {
            try
            {
                var subKey = Environment.Is64BitOperatingSystem ? RegVS64SubKey : RegVS32SubKey;

                string installDir = null;
                //// Try to get version 14
                if (installDir == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(string.Format(subKey, 14)))
                    {
                        if (key != null)
                        {
                            installDir = key.GetValue("ProductDir") as string;
                        }
                    }
                }

                //// Try to get version 12
                if (installDir == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(string.Format(subKey, 12)))
                    {
                        if (key != null)
                        {
                            installDir = key.GetValue("ProductDir") as string;
                        }
                    }
                }

                //// Try to get version 11
                if (installDir == null)
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(string.Format(subKey, 11)))
                    {
                        if (key != null)
                        {
                            installDir = key.GetValue("ProductDir") as string;
                        }
                    }
                }

                if (installDir == null)
                {
                    vsDir = null;
                    return false;
                }

                vsDir = new DirectoryInfo(installDir);
                return vsDir.Exists;
            }
            catch (Exception e)
            {
                vsDir = null;
                Program.WriteError("ERROR: Could not find Visual Studio - {0}", e.Message);
                return false;
            }
        }

        private static bool GetFrameworkFile(string fileName, out FileInfo file, bool force32Bit = false)
        {
            try
            {
                DirectoryInfo framework;
                if (!GetFrameworkDir(out framework, force32Bit))
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
