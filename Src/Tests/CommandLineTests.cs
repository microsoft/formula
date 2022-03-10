using System;
using System.IO;
using Xunit;
using Microsoft.Formula.CommandLine;
using Xunit.Abstractions;

namespace Tests
{
    public class CommandLineTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly CommandLineProgram.ConsoleChooser _chooser;
        private readonly CommandLineProgram.ConsoleSink _sink;
        private readonly CommandInterface _ci;
        private readonly string _fullPath;

        public CommandLineTests(ITestOutputHelper output)
        {
            _output = output;
            _chooser = new CommandLineProgram.ConsoleChooser();
            _sink = new CommandLineProgram.ConsoleSink();
            _ci = new CommandInterface(_sink, _chooser);
            _fullPath = Path.GetFullPath("../../../../models/graphs.4ml");
        }

        public void Dispose()
        {
            _ci.Cancel();
        }

        [Fact]
        public void TestExit()
        {
            using (_ci)
            {
                Assert.True(_ci.DoCommand("exit"));
                Assert.False(_sink.PrintedError);
                
                Assert.True(_ci.DoCommand("x"));
                Assert.False(_sink.PrintedError);
            }
        }
        
        [Fact]
        public void TestHelp()
        {
            using (_ci)
            {
                Assert.True(_ci.DoCommand("help"));
                Assert.False(_sink.PrintedError);
                
                Assert.True(_ci.DoCommand("h"));
                Assert.False(_sink.PrintedError);
            }
        }
        
        [Fact]
        public void TestSet()
        {
            using (_ci)
            {
                Assert.True(_ci.DoCommand("set A test"));
                Assert.False(_sink.PrintedError);
                
                Assert.True(_ci.DoCommand("s A test"));
                Assert.False(_sink.PrintedError);
            }
        }
        
        [Fact]
        public void TestDel()
        {
            using (_ci)
            {
                Assert.True(_ci.DoCommand("set B test2"));
                Assert.False(_sink.PrintedError);
                Assert.True(_ci.DoCommand("del B"));
                Assert.False(_sink.PrintedError);
                
                Assert.True(_ci.DoCommand("s B test2"));
                Assert.False(_sink.PrintedError);
                Assert.True(_ci.DoCommand("d B"));
                Assert.False(_sink.PrintedError);
            }
        }
        
        [Fact]
        public void TestList()
        {
            using (_ci)
            {
                Assert.True(_ci.DoCommand("list"));
                Assert.False(_sink.PrintedError);
                
                Assert.True(_ci.DoCommand("ls"));
                Assert.False(_sink.PrintedError);
            }
        }
    }
}