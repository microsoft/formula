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
            startInfo.Arguments = binPath + "/CommandLine.dll --interactive: off";
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

            Assert.True(RunCommand("wait on").passed, "FormulaFixture: Interactive command failed.");
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
                Console.WriteLine(evt.Data);
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
            Assert.True(RunCommand("tunload *").passed, "FormulaFixture: tunload command failed.");
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
                try
                {
                    _p.StandardInput.WriteLine("unload *");
                }
                catch(System.IO.IOException e)
                {
                    Console.Error.WriteLine(e.Message);
                    _isPipeBroken = true;
                }
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

            try
            {
                TimeSpan tSpan = new TimeSpan(0,0,10);
                for(int i = 0;i < output.Length;++i)
                {
                    bool res = Regex.IsMatch(output[i], @"(Solve\s\|\s+Done\s+\|\s+true)", RegexOptions.Compiled, tSpan);
                    if(res)
                    {
                        return true;
                    }
                }
            }
            catch (RegexMatchTimeoutException e)
            {
                Console.Error.WriteLine(String.Format("FormulaFixture: Timeout after {0} matching '{1}' with '{2}'.", e.MatchTimeout, e.Input, e.Pattern));
                return false;
            }
            finally
            {
                ClearTasks();
                ClearOutput();
            }
            return false;
        }

        private bool HasCommandRun(string[] output, string[] command)
        {
            if(command.Length < 1)
                return false;

            string cmdRegex = null;
            switch (command[0])
            {
                case "int":
                case "interactive":
                    cmdRegex = @"(interactive\s(on|off))";
                    break;
                case "w":
                case "wait":
                    cmdRegex = @"(wait\s(on|off))";
                    break;
                case "v":
                case "verbose":
                    cmdRegex = @"(verbose\s(on|off))";
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
                    cmdRegex = @"(\/\/\/\/\sProgram)";
                    break;
                case "l":
                case "load":
                    cmdRegex = @"(\(Compiled\))";
                    break;
                case "ul":
                case "unload":
                    cmdRegex = @"(\(Uninstalled\))";
                    break;
                case "tul":
                case "tunload":
                    return true;
                case "sl":
                case "solve":
                    cmdRegex = @"(Started\ssolve\stask\swith\sId\s\d+\.)";
                    break;
                case "ls":
                case "list":
                    cmdRegex = @"(All\stasks)|(Programs\sin\s(file|env)\sroot)|(Environment\svariables)";
                    break;
                case "h":
                case "help":
                    cmdRegex = @"(apply\s\(ap\)\s+\-.+?(?=:):\sapply\stransformstep)";
                    break;
                case "s":
                case "set" when command.Length > 2:
                    cmdRegex = @"(" + command[1] + @"\s=\s" + command[2] + ")";
                    break;
                case "d":
                case "del" when command.Length > 1:
                    cmdRegex = @"Deleted\svariable\s'"+ command[1] + "'";
                    break;
                default:
                    Console.WriteLine("FormulaFixture: Command not found.");
                    return false;
            }
            TimeSpan tSpan = new TimeSpan(0,0,10);
            try
            {
                bool res = Regex.IsMatch(output[0], cmdRegex, RegexOptions.Compiled, tSpan);
                if(res)
                {
                    return true;
                }

                res = Regex.IsMatch(output[output.Length - 2], cmdRegex, RegexOptions.Compiled, tSpan);
                if(res)
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException e)
            {
                Console.Error.WriteLine(String.Format("FormulaFixture: Timeout after {0} matching '{1}' with '{2}'.", e.MatchTimeout, e.Input, e.Pattern));
                return false;
            }

            for(int i = 1;i < output.Length - 2;++i)
            {
                try
                {
                    bool res = Regex.IsMatch(output[i], cmdRegex, RegexOptions.Compiled, tSpan);
                    if(res)
                    {
                        return true;
                    }
                }
                catch (RegexMatchTimeoutException e)
                {
                    Console.Error.WriteLine(String.Format("FormulaFixture: Timeout after {0} matching '{1}' with '{2}'.", e.MatchTimeout, e.Input, e.Pattern));
                    return false;
                }
            }

            return false;
        }
    }
}