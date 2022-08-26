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
            Assert.True(_ciFixture.RunCommand("help").passed);
            Assert.True(_ciFixture.RunCommand("h").passed);
        }
        
        [Fact]
        public void TestSet()
        {
            Assert.True(_ciFixture.RunCommand("set A test").passed);
            Assert.True(_ciFixture.RunCommand("s A test").passed);
        }
        
        [Fact]
        public void TestDel()
        {
            Assert.True(_ciFixture.RunCommand("set B test2").passed);
            Assert.True(_ciFixture.RunCommand("del B").passed);
            
            Assert.True(_ciFixture.RunCommand("s B test2").passed);
            Assert.True(_ciFixture.RunCommand("d B").passed);
        }
        
        [Fact]
        public void TestList()
        {
            Assert.True(_ciFixture.RunCommand("list").passed);
            Assert.True(_ciFixture.RunCommand("ls").passed);
        }
    }
}