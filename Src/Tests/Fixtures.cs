using System;
using System.IO;
using Xunit;
using Microsoft.Formula.CommandLine;
using Xunit.Abstractions;

namespace Tests
{
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