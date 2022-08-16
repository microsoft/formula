using System;
using System.IO;
using Xunit;
using Microsoft.Formula.CommandLine;
using Microsoft.Formula.Compiler;
using Microsoft.Formula.API.Nodes;
using Microsoft.Formula.API;
using Xunit.Abstractions;

namespace Tests
{
    public class LintingTests : IClassFixture<CommandInterfaceFixture>
    {
        private readonly CommandInterfaceFixture _ciFixture;

        public LintingTests(CommandInterfaceFixture fixture)
        {
            _ciFixture = fixture;
        }

        private void ResetStandardOutput()
        {
            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
        }

        private void CheckIfProgramLoaded()
        {
            _ciFixture._sink.ResetPrintedError();
            using(StringWriter sw = new StringWriter())
            using (_ciFixture._ci)
            {
                Console.SetOut(sw);

                Assert.True(_ciFixture._ci.DoCommand("print"));

                string output = sw.ToString();

                ResetStandardOutput();

                bool ctns = output.Contains("No file with that name");
                if(!ctns)
                {
                    Assert.True(_ciFixture._ci.DoCommand("unload"));
                }
            }
        }

        [Fact]
        public void TestRuleLinterFixedLoading()
        {
            using (_ciFixture._ci)
            {
                CheckIfProgramLoaded();
                
                string[] cmdPath = { "load", Path.GetFullPath("../../../models/weird_domain_fixed.4ml") };
                Assert.True(_ciFixture._ci.DoCommand(String.Join(" ", cmdPath)));
                Assert.False(_ciFixture._sink.PrintedError);
            }
        }

        [Fact]
        public void TestRuleLinterBrokenLoading()
        {
            using (_ciFixture._ci)
            {
                CheckIfProgramLoaded();
                
                string[] cmdPath = { "load", Path.GetFullPath("../../../models/weird_domain_broken.4ml") };
                Assert.True(_ciFixture._ci.DoCommand(String.Join(" ", cmdPath)));
                Assert.True(_ciFixture._sink.PrintedError);
            }
        }

        [Fact]
        public void TestValidRuleLinterValidateBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var typeTerm = Factory.Instance.MkId("Node");
            var varId = Factory.Instance.MkId("x");
            var find = Factory.Instance.MkFind(varId, typeTerm);
            body.Node.AddConstr(find.Node);

            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            string varName = "theta";
            Assert.True(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varName));
            Assert.Null(varName);
        }

        [Fact]
        public void TestValidRuleLinterValidateBodyNonQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x");
            Assert.False(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            string varName = "theta";
            Assert.True(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varName));
            Assert.Null(varName);
        }

        [Fact]
        public void TestInvalidRuleLinterValidateBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x.right");
            Assert.True(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            string varName = "theta";
            Assert.False(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varName));
            Assert.Equal("x", varName);
        }
    }

    public class CommandInterfaceFixture : IDisposable
    {
        private readonly CommandLineProgram.ConsoleChooser _chooser;
        public CommandLineProgram.ConsoleSink _sink;
        public CommandInterface _ci { get; private set; }

        public CommandInterfaceFixture()
        {
            _chooser = new CommandLineProgram.ConsoleChooser();
            _sink = new CommandLineProgram.ConsoleSink();
            _ci = new CommandInterface(_sink, _chooser);
        }

        public void Dispose()
        {
            _ci.Cancel();
        }
    }
}