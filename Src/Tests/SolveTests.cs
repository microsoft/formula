using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Xunit;
using Microsoft.Formula.CommandLine;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("FormulaCollection")]
    public class SolveTests : IClassFixture<FormulaFixture>
    {
        private readonly FormulaFixture _ciFixture;

        private readonly ITestOutputHelper _output;

        public SolveTests(FormulaFixture fixture, ITestOutputHelper output)
        {
            _output = output;
            _ciFixture = fixture;
        }

        [Fact]
        public void TestSolvingMappingExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/MappingExample.4ml")).passed, "SolveTests: Loading MappingExample.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm 1 Mapping.conforms").passed, "SolveTests: Solve command for MappingExample.4ml failed.");
            Assert.True(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm in MappingExample.4ml.");
        }
    
        [Fact]
        public void TestSolvingSendMoreMoneyExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SendMoreMoney.4ml")).passed, "SolveTests: Loading SendMoreMoney.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm 1 Money.conforms").passed, "SolveTests: Solve command for SendMoreMoney.4ml failed.");
            Assert.True(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm in SendMoreMoney.4ml.");
        }

        [Fact]
        public void TestSolvingSymbolicAggregationExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicAggregation.4ml")).passed, "SolveTests: Loading SymbolicAggregation.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm 1 SymbolicAggregation.conforms").passed, "SolveTests: Solve command for SymbolicAggregation.4ml failed.");
            Assert.True(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm in SymbolicAggregation.4ml.");
        }

        [Fact]
        public void TestSolvingSymbolicMaxExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicMax.4ml")).passed, "SolveTests: Loading SymbolicMax.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm 1 SymbolicMax.conforms").passed, "SolveTests: Solve command for SymbolicMax.4ml failed.");
            Assert.True(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm in SymbolicMax.4ml.");
        }

        [Fact]
        public void TestSymbolicOLPExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicOLP.4ml")).passed, "SolveTests: Loading SymbolicOLP.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm 1 SymbolicOLP.conforms").passed, "SolveTests: Solve command for SymbolicOLP.4ml failed.");
            Assert.False(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm in SymbolicOLP.4ml.");
        }

        [Fact]
        public void TestSimpleOLPExample()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SimpleOLP.4ml")).passed, "SolveTests: Loading SimpleOLP.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm1 1 SimpleOLP.conforms").passed, "SolveTests: Solve command for SimpleOLP.4ml failed.");
            Assert.False(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm1 in SimpleOLP.4ml.");
        }

        [Fact]
        public void TestSimpleOLP2Example()
        {
            Assert.True(_ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SimpleOLP2.4ml")).passed, "SolveTests: Loading SimpleOLP2.4ml failed.");
            Assert.True(_ciFixture.RunCommand("solve pm2 1 SimpleOLP2.conforms").passed, "SolveTests: Solve command for SimpleOLP2.4ml failed.");
            Assert.False(_ciFixture.GetResult(), "SolveTests: No solutions found for partial model pm2 in SimpleOLP2.4ml.");
        }
    }
}