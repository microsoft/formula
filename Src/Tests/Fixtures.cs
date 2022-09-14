using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;
using Microsoft.Formula.CommandLine;
using Microsoft.Formula.API;
using Xunit.Abstractions;

namespace Tests
{
    [CollectionDefinition("FormulaCollection")]
    public class FormulaCollection : ICollectionFixture<FormulaFixture> {}

    public class FormulaFixture : IDisposable
    {
        public TestChooser _chooser { get; private set; }
        public TestSink _sink { get; private set; }
        public CommandInterface _ci { get; private set; }

        public FormulaFixture()
        {
            _chooser = new TestChooser();
            _sink = new TestSink(_chooser);
            _ci = new CommandInterface(_sink, _chooser);

            RunCommand("interactive off", "FormulaFixture: Interactive off failed.");
            RunCommand("wait on", "FormulaFixture: Wait on failed.");
            RunCommand("verbose on", "FormulaFixture: Verbose on failed.");
        }

        public void RunCommand(string command = "", string assert_msg = "", bool assert_bool = true)
        {
            var args = command.Split(' ');
            switch(args[0])
            {
                case "load":
                    _sink.Command = command;

                    Assert.True(_ci.DoCommand("unload *"), "FormulaFixture: unload failed.");

                    _sink.ClearOutput();

                    Assert.True(_ci.DoCommand(command), assert_msg);
                    break;
                default:
                    _sink.Command = command;

                    Assert.True(_ci.DoCommand(command), assert_msg);
                    break;
            }
        }

        public void Dispose()
        {
            _ci.Cancel();
        }

        public bool GetLoadResult()
        {
            string[] output = _sink.Output;

            _sink.ClearOutput();

            foreach(var o in output)
            {
                var reg = new Regex(@"\(Compiled\)", RegexOptions.Compiled);
                if(reg.IsMatch(o))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetSetResult()
        {
            string[] output = _sink.Output;

            _sink.ClearOutput();

            foreach(var o in output)
            {
                if(o.Contains("="))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetDelResult()
        {
            string[] output = _sink.Output;

            _sink.ClearOutput();

            foreach(var o in output)
            {
                var reg = new Regex(@"Deleted\svariable", RegexOptions.Compiled);
                if(reg.IsMatch(o))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetListResult()
        {
            string[] output = _sink.Output;

            _sink.ClearOutput();

            foreach(var o in output)
            {
                var reg = new Regex(@"Environment\svariables", RegexOptions.Compiled);
                if(reg.IsMatch(o))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetHelpResult()
        {
            string[] output = _sink.Output;

            _sink.ClearOutput();

            foreach(var o in output)
            {
                var reg = new Regex(@"apply\s+\(ap\)\s+\-\s+Start\s+an\s+apply\s+task\.\s+Use:\s+apply\s+transformstep", RegexOptions.Compiled);
                if(reg.IsMatch(o))
                {
                    return true;
                }
            }
            return false;
        }

        public bool GetSolveResult()
        {
            _sink.ClearOutput();

            Assert.True(_ci.DoCommand("ls tasks"), "FormulaFixture: ls tasks failed.");

            string[] output = _sink.Output;

            _sink.ClearOutput();

            Assert.True(_ci.DoCommand("tunload *"), "FormulaFixture: tunload failed.");

            _sink.ClearOutput();

            foreach(var o in output)
            {
                var reg = new Regex(@"^\s+0\s+\|\s+Solve\s+\|\s+Done\s+\|\s+true", RegexOptions.Compiled);
                if(reg.IsMatch(o))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class TestChooser : Microsoft.Formula.CommandLine.IChooser
    {
        public bool Interactive { get; set; }

        public TestChooser()
        {
            Interactive = false;
        }

        public bool GetChoice(out DigitChoiceKind choice)
        {
            choice = DigitChoiceKind.Zero;
            return true;
        }
    }

    public class TestSink : Microsoft.Formula.CommandLine.IMessageSink
    {
        private StringBuilder _strBuilder = null;

        public string Command { get; set; }

        private TestChooser _chooser = null;

        public bool PrintedError
        {
            get
            {
                return false;
            }
        }

        public string[] Output {
            get {
                return _strBuilder.ToString().Split("\n");
            }
        }

        public TestSink(TestChooser chooser) 
        {
            _chooser = chooser;
            _strBuilder = new StringBuilder();
            Console.SetOut(TextWriter.Null);
        }

        public void ClearOutput()
        {
            _strBuilder.Clear();
        }

        private void AddMessage(SeverityKind severity = SeverityKind.Info, string msg = "", bool newline = false)
        {
            if(newline)
            {
                _strBuilder.AppendLine(msg);
                return;
            }
            _strBuilder.Append(msg);
        }

        public TextWriter Writer
        {
            get { return TextWriter.Null; }
        }

        public void WriteMessage(string msg)
        {
            AddMessage(SeverityKind.Info, msg);
        }

        public void WriteMessage(string msg, SeverityKind severity)
        {
            AddMessage(severity, msg);
        }

        public void WriteMessageLine(string msg)
        {
            AddMessage(SeverityKind.Info, msg, true);
        }

        public void WriteMessageLine(string msg, SeverityKind severity)
        {
            AddMessage(severity, msg, true);
        } 
    }
}