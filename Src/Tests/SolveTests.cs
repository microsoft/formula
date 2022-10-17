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

        public SolveTests(FormulaFixture fixture)
        {
            _ciFixture = fixture;
        }

        [Fact]
        public void TestSolvingMappingExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/MappingExample.4ml"), "SolveTests: Load command for MappingExample.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading MappingExample.4ml failed.");

            _ciFixture.RunCommand("solve pm 1 Mapping.conforms", "SolveTests: Solve command for MappingExample.4ml failed.");
            Assert.True(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm in MappingExample.4ml.");
        }
    
        [Fact]
        public void TestSolvingSendMoreMoneyExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SendMoreMoney.4ml"), "SolveTests: Load command for SendMoreMoney.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SendMoreMoney.4ml failed.");

            _ciFixture.RunCommand("solve pm 1 Money.conforms", "SolveTests: Solve command for SendMoreMoney.4ml failed.");
            Assert.True(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm in SendMoreMoney.4ml.");
        }

        [Fact]
        public void TestSolvingSymbolicAggregationExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicAggregation.4ml"), "SolveTests: Load command for SymbolicAggregation.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SymbolicAggregation.4ml failed.");

            _ciFixture.RunCommand("solve pm 1 SymbolicAggregation.conforms", "SolveTests: Solve command for SymbolicAggregation.4ml failed.");
            Assert.True(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm in SymbolicAggregation.4ml.");
        }

        [Fact]
        public void TestSolvingSymbolicMaxExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicMax.4ml"), "SolveTests: Load command for SymbolicMax.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SymbolicMax.4ml failed.");

            _ciFixture.RunCommand("solve pm 1 SymbolicMax.conforms", "SolveTests: Solve command for SymbolicMax.4ml failed.");
            Assert.True(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm in SymbolicMax.4ml.");
        }

        [Fact]
        public void TestSymbolicOLPExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SymbolicOLP.4ml"), "SolveTests: Load command for SymbolicOLP.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SymbolicOLP.4ml failed.");

            _ciFixture.RunCommand("solve pm 1 SymbolicOLP.conforms", "SolveTests: Solve command for SymbolicOLP.4ml failed.");
            Assert.False(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm in SymbolicOLP.4ml.");
        }

        [Fact]
        public void TestSimpleOLPExample()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SimpleOLP.4ml"), "SolveTests: Load command for SimpleOLP.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SimpleOLP.4ml failed.");

            _ciFixture.RunCommand("solve pm1 1 SimpleOLP.conforms", "SolveTests: Solve command for SimpleOLP.4ml failed.");
            Assert.False(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm1 in SimpleOLP.4ml.");
        }

        [Fact]
        public void TestSimpleOLP2Example()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../../../Tst/Tests/Symbolic/SimpleOLP2.4ml"), "SolveTests: Load command for SimpleOLP2.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "SolveTests: Loading SimpleOLP2.4ml failed.");

            _ciFixture.RunCommand("solve pm2 1 SimpleOLP2.conforms", "SolveTests: Solve command for SimpleOLP2.4ml failed.");
            Assert.False(_ciFixture.GetSolveResult(), "SolveTests: No solutions found for partial model pm2 in SimpleOLP2.4ml.");
        }
    }
}