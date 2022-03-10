using Microsoft.Formula.API;
using Xunit;
using System.IO;
using Xunit.Abstractions;

namespace Tests
{
    public class CoreTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _fullPath;

        public CoreTests(ITestOutputHelper output)
        {
            _output = output;
            _fullPath = Path.GetFullPath("../../../models/graphs.4ml");
        }
        [Fact]
        public void TestProgramName()
        {
            var progName  = new ProgramName("../../../models/graphs.4ml");
            Assert.Contains(progName.ToString(), _fullPath);
        }
    }
}