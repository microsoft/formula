using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Tests
{
    [Collection("FormulaCollection")]
    public class FormulaFixture : IDisposable
    {
        private readonly Process _p = new Process();

        private static StringBuilder _stdOutBr = new StringBuilder();

        private readonly Task _waitForExitTask = null;

        private bool _isPipeBroken = false;

        public FormulaFixture()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.FileName = "dotnet";
            startInfo.UseShellExecute = false;
            var binPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            startInfo.Arguments = binPath + "/CommandLine.dll";
            startInfo.ErrorDialog = false;

            _p.StartInfo = startInfo;
            _p.EnableRaisingEvents = true;
            _p.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);

            _p.ErrorDataReceived += new DataReceivedEventHandler(ErrorHandler);

            _p.Exited += (sender, evt) => 
            {
                Console.WriteLine("FormulaFixture: Process CommandLine has exited.");
            };

            _p.Start();
            _p.BeginOutputReadLine();
            _p.BeginErrorReadLine();
            _waitForExitTask = _p.WaitForExitAsync();

            Assert.True(RunCommand("interactive on").passed, "FormulaFixture: Interactive command failed.");

            Assert.True(RunCommand("wait on").passed, "FormulaFixture: Interactive command failed.");

            Assert.True(RunCommand("verbose on").passed, "FormulaFixture: Verbose command failed.");
        }

        public void Dispose()
        {
            if(!_p.HasExited)
            {
                _p.CancelOutputRead();
                _p.CancelErrorRead();
                if(!_waitForExitTask.Wait(5000))
                    _p.Kill();
            }
        }

        private void OutputHandler(object sender, DataReceivedEventArgs evt)
        {
            if(!String.IsNullOrEmpty(evt.Data))
            {
                lock(_stdOutBr)
                {
                    _stdOutBr.AppendLine(evt.Data);
                }
            }
        }

        private void ErrorHandler(object sender, DataReceivedEventArgs evt)
        {
            if (!String.IsNullOrEmpty(evt.Data))
            {
                Console.WriteLine("FormulaFixture.Process Error: " + evt.Data);
            }
        }

        public void ClearOutput()
        {
            lock(_stdOutBr)
            {
                _stdOutBr.Clear();
            }
        }

        public void ClearTasks()
        {
            Assert.True(RunCommand("tunload 0").passed, "FormulaFixture: tunload command failed.");
        }

        private string[] GetOutput()
        {
            lock(_stdOutBr)
            {
                return _stdOutBr.ToString().Split(Environment.NewLine);
            }
        }

        public (bool passed, string[] output) RunCommand(string command)
        {
            if(_isPipeBroken)
                return (false, null);

            string[] splitCommand = command.Split(" ");
            if(splitCommand[0].Equals("load") ||
               splitCommand[0].Equals("l"))
            {
                CheckIfFileLoaded();
            }

            try
            {
                _p.StandardInput.WriteLine(command);
            }
            catch(System.IO.IOException e)
            {
                Console.Error.WriteLine(e.Message);
                _isPipeBroken = true;
                return (false, null);
            }

            Thread.Sleep(500);
            string[] output = GetOutput();
            ClearOutput();
            return (HasCommandRun(output, splitCommand), output);
        }

        public bool GetResult()
        {
            (bool hasRan, string[] output) = RunCommand("ls tasks");
            if(!hasRan)
            {
                return false;
            }

            string o = String.Join(" ", output);
            bool res = false;
            try
            {
                TimeSpan tSpan = new TimeSpan(0,0,10);
                bool patMatched = Regex.IsMatch(o, @"(Solve\s\|\s+Done\s+\|\s+false)", RegexOptions.Compiled | RegexOptions.Singleline, tSpan);
                if(patMatched)
                {
                    res = false;
                }
                patMatched = Regex.IsMatch(o, @"(Solve\s\|\s+Done\s+\|\s+true)", RegexOptions.Compiled | RegexOptions.Singleline, tSpan);
                if(patMatched)
                {
                    res = true;
                }
            }
            catch (RegexMatchTimeoutException e)
            {
                string msg = String.Format("FormulaFixture: Timeout after {0} matching '{1}' with '{2}'.", e.MatchTimeout, e.Input, e.Pattern);
                Console.Error.WriteLine(msg);
                return false;
            }
            ClearTasks();
            ClearOutput();
            return res;
        }

        private void CheckIfFileLoaded()
        {
            if(RunCommand("print").passed)
            {
                try
                {
                    _p.StandardInput.WriteLine("unload");
                }
                catch(System.IO.IOException e)
                {
                    Console.Error.WriteLine(e.Message);
                    _isPipeBroken = true;
                }

                Thread.Sleep(500);
            }
        }

        public void SendChoice(string choice)
        {
            try
            {
                _p.StandardInput.WriteLine(choice);
            }
            catch(System.IO.IOException e)
            {
                Console.Error.WriteLine(e.Message);
                _isPipeBroken = true;
            }

            Thread.Sleep(500);
        }

        private bool HasCommandRun(string[] output, string[] command)
        {
            if(command.Length < 1)
                return false;

            string[] cmdRegex = null;
            string lineToValidate = null;
            switch (command[0])
            {
                case "int":
                case "interactive":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(interactive\son)";
                    cmdRegex[1] = @"(interactive\soff)";
                    break;
                case "w":
                case "wait":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(wait\son)";
                    cmdRegex[1] = @"(wait\soff)";
                    break;
                case "v":
                case "verbose":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(verbose\son)";
                    cmdRegex[1] = @"(verbose\soff)";
                    break;
                case "x":
                case "exit":
                    if(_waitForExitTask.Wait(5000))
                    {
                        _p.CancelOutputRead();
                        _p.CancelErrorRead();
                        return true;
                    }
                    return false;
                case "p":
                case "print":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(\/\/\/\/\sProgram\s.+?(?=\.)\.4ml)";
                    cmdRegex[1] = @"(No\sfile\swith\sthat\sname)";
                    break;
                case "l":
                case "load":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(\(Compiled\)\s.+?(?=\.)\.4ml)";
                    cmdRegex[1] = @"(The\sinstall\soperation\sfailed)|(\(Failed\)\s.+?(?=\.)\.4ml)";
                    break;
                case "ul":
                case "unload":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(\(Uninstalled\)\s\w+\.4ml)";
                    cmdRegex[1] = @"(No\sfile\swith\sthat\sname)|(\(Failed\)\s.+?(?=\.)\.4ml)";
                    break;
                case "tul":
                case "tunload":
                    cmdRegex = new string[1];
                    cmdRegex[0] = @"(\(Unloaded\s\d+\stasks)|(No\stask\swith\sID\s\d+)|(Unloaded\stask\s\d+)";
                    break;
                case "sl":
                case "solve":
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(Started\ssolve\stask\swith\sId\s\d+\.)|(Choose:)";
                    cmdRegex[1] = @"(No\smodule\swith\sthat\sname)|(Failed\sto\sstart\ssolved\stask\.)|(Expected\sa\spositive\snumber\sof\ssolutions)";
                    break;
                case "ls" when command.Length > 1:
                    cmdRegex = new string[1];
                    cmdRegex[0] = @"(All\stasks)";
                    break;
                case "ls":
                case "list":
                    cmdRegex = new string[1];
                    cmdRegex[0] = @"(All\stasks)|(Programs\sin\s(file|env)\sroot)|(Environment\svariables)";
                    break;
                case "h":
                case "help":
                    cmdRegex = new string[1];
                    cmdRegex[0] = @"(apply\s\(ap\)\s+\-.+?(?=:):\sapply\stransformstep)";
                    break;
                case "s":
                case "set" when command.Length > 2:
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"(" + command[1] + @"\s=\s" + command[2] + ")";
                    cmdRegex[1] = @"(The\svariable\s'.+?(?=')'\sis\snot\sdefined)";
                    break;
                case "d":
                case "del" when command.Length > 1:
                    cmdRegex = new string[2];
                    cmdRegex[0] = @"Deleted\svariable\s'"+ command[1] + "'";
                    cmdRegex[1] = @"(The\svariable\s'.+?(?=')'\sis\snot\sdefined)";
                    break;
                default:
                    Console.WriteLine("FormulaFixture: Command not found.");
                    return false;
            }
            lineToValidate = String.Join(" ", output);
            HashSet<string> simpleCmds = new HashSet<string>() { "int", "interactive", "w", "wait", "v", "verbose", "tul", "tunload", "ls tasks", "ls", "list", "h", "help" };
            for(int idx = 0;idx < cmdRegex.Length;++idx)
            {
                if(!String.IsNullOrEmpty(lineToValidate))
                {
                    try
                    {
                        TimeSpan tSpan = new TimeSpan(0,0,10);
                        bool patMatched = Regex.IsMatch(lineToValidate, cmdRegex[idx], RegexOptions.Compiled | RegexOptions.Singleline, tSpan);
                        if(simpleCmds.Contains(command[0]))
                        {
                            return patMatched;
                        }

                        if(idx == 0 && patMatched)
                        {
                            return true;
                        }
                        else if(idx == 1 && patMatched)
                        {
                            return false;
                        }
                    }
                    catch (RegexMatchTimeoutException e)
                    {
                        string msg = String.Format("FormulaFixture: Timeout after {0} matching '{1}' with '{2}'.", e.MatchTimeout, e.Input, e.Pattern);
                        Console.Error.WriteLine(msg);
                        return false;
                    }
                }
            }
            return false;
        }
    }
}