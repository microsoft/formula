using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("FormulaCollection")]
    public class CommandLineTests : IClassFixture<FormulaFixture>
    {
        private readonly FormulaFixture _ciFixture;

        public CommandLineTests(FormulaFixture fixture)
        {
            _ciFixture = fixture;
        }
        
        [Fact]
        public void TestHelp()
        {
            _ciFixture.RunCommand("help", "CommandLineTests: help command failed.");
            Assert.True(_ciFixture.GetHelpResult(), "CommandLineTests: help result failed.");

            _ciFixture.RunCommand("h", "CommandLineTests: h command failed.");
            Assert.True(_ciFixture.GetHelpResult(), "CommandLineTests: h result failed.");
        }
        
        [Fact]
        public void TestSet()
        {
            _ciFixture.RunCommand("set A test", "CommandLineTests: set command failed.");
            Assert.True(_ciFixture.GetSetResult(), "CommandLineTests: set result failed.");

            _ciFixture.RunCommand("s A test", "CommandLineTests: s command failed.");
            Assert.True(_ciFixture.GetSetResult(), "CommandLineTests: s result failed.");
        }
        
        [Fact]
        public void TestDel()
        {
            _ciFixture.RunCommand("set B test2", "CommandLineTests: set command failed.");
            Assert.True(_ciFixture.GetSetResult(), "CommandLineTests: s result failed.");

            _ciFixture.RunCommand("del B", "CommandLineTests: del command failed.");
            Assert.True(_ciFixture.GetDelResult(), "CommandLineTests: del result failed.");

            _ciFixture.RunCommand("s B test2", "CommandLineTests: s command failed.");
            Assert.True(_ciFixture.GetSetResult(), "CommandLineTests: s result failed.");

            _ciFixture.RunCommand("d B", "CommandLineTests: d command failed.");
            Assert.True(_ciFixture.GetDelResult(), "CommandLineTests: d result failed.");
        }
        
        [Fact]
        public void TestList()
        {
            _ciFixture.RunCommand("list", "CommandLineTests: list command failed.");
            Assert.True(_ciFixture.GetListResult(), "CommandLineTests: list result failed.");

            _ciFixture.RunCommand("ls", "CommandLineTests: ls command failed.");
            Assert.True(_ciFixture.GetListResult(), "CommandLineTests: ls result failed.");
        }
    }
}