using System;
using System.IO;
using Xunit;
using Microsoft.Formula.CommandLine;
using Xunit.Abstractions;

namespace Tests
{
    public class SolveTests : IClassFixture<CommandInterfaceFixture>
    {
        private readonly CommandInterfaceFixture _ciFixture;

        public SolveTests(CommandInterfaceFixture fixture)
        {
            _ciFixture = fixture;
        }

        private void ResetStandardOutput()
        {
            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
        }

        private bool SolvePartialModel(string partial_model)
        {
            using(StringWriter sw = new StringWriter())
            using (_ciFixture._ci)
            {
                Console.SetOut(sw);

                if(!_ciFixture._ci.DoCommand("solve x = " + partial_model))
                {
                    return false;
                }

                string output = sw.ToString();

                ResetStandardOutput();

                bool ctns = output.Contains("Model solvable.");
                if(ctns)
                {
                    Console.WriteLine("Model Solvable");
                    return true;
                }
                
                Console.WriteLine("Model Not Solvable");
                return false;
            }
        }

        private bool loadExample(string examplePath)
        {
            using (_ciFixture._ci)
            {
                if(!_ciFixture._ci.DoCommand("unload"))
                {
                    return false;
                }
                
                string[] cmdPath = { "load", examplePath };
                if(!_ciFixture._ci.DoCommand(String.Join(" ", cmdPath)))
                {
                    return false;
                }

                return true;
            }
        }

        [Fact]
        public void TestLoadMappingExample()
        {
            Assert.True(loadExample(Path.GetFullPath("../../../../../Tst/Tests/Symbolic/MappingExample.4ml")));
        }

        [Fact]
        public void TestSolveMappingExample()
        {
            Assert.True(SolvePartialModel("pm"));
        }

        [Fact]
        public void TestLoadSendMoreMoneyExample()
        {
            Assert.True(loadExample(Path.GetFullPath("../../../../../Tst/Tests/Symbolic/SendMoreMoney.4ml")));
        }

        [Fact]
        public void TestSolveSendMoreMoneyExample()
        {
            Assert.True(SolvePartialModel("pm"));
        }

        [Fact]
        public void TestLoadSymbolicAggregationExample()
        {
            Assert.True(loadExample(Path.GetFullPath("../../../../../Tst/Tests/Symbolic/SymbolicAggregation.4ml")));
        }

        [Fact]
        public void TestSolveSymbolicAggregationExample()
        {
            Assert.True(SolvePartialModel("pm"));
        }

        [Fact]
        public void TestLoadSymbolicMaxExample()
        {
            Assert.True(loadExample(Path.GetFullPath("../../../../../Tst/Tests/Symbolic/SymbolicMax.4ml")));
        }

        [Fact]
        public void TestSolveSymbolicMaxExample()
        {
            Assert.True(SolvePartialModel("pm"));
        }
    }
}