using System;
using System.IO;
using System.Collections.Generic;
using Xunit;
using Microsoft.Formula.Compiler;
using Microsoft.Formula.API.Nodes;
using Microsoft.Formula.API;
using Xunit.Abstractions;

namespace Tests
{
    [Collection("FormulaCollection")]
    public class LintingTests : IClassFixture<FormulaFixture>
    {
        private readonly FormulaFixture _ciFixture;

        public LintingTests(FormulaFixture fixture)
        {
            _ciFixture = fixture;
        }

        [Fact]
        public void TestRuleLinterFixedLoading()
        {           
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../models/weird_domain_fixed.4ml"), "LintingTests: Load command for weird_domain_fixed.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "LintingTests: Loading weird_domain_fixed.4ml failed.");
        }

        [Fact]
        public void TestRuleLinterBrokenLoading()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../models/weird_domain_broken.4ml"), "LintingTests: Load command for weird_domain_broken.4ml failed.");
            Assert.False(_ciFixture.GetLoadResult(), "LintingTests: Finding errors in weird_domain_broken.4ml failed.");
        }

        [Fact]
        public void TestRuleLinterArmNoMemLoading()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../models/arm_no_mem.4ml"), "LintingTests: Load command for arm_no_mem.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "LintingTests: Finding errors in arm_no_mem.4ml failed.");
        }

        [Fact]
        public void TestRuleLinterArmLoading()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../models/arm.4ml"), "LintingTests: Load command for arm.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "LintingTests: Finding errors in arm.4ml failed.");
        }

        [Fact]
        public void TestRuleLinterGraphsLoading()
        {
            _ciFixture.RunCommand("load " + Path.GetFullPath("../../../../../models/graphs.4ml"), "LintingTests: Load command for graphs.4ml failed.");
            Assert.True(_ciFixture.GetLoadResult(), "LintingTests: Finding errors in graphs.4ml failed.");
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
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Eq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.True(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Empty(varNames);
        }

        [Fact]
        public void TestValidRuleLinterValidateBodyNonQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x");
            Assert.False(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Eq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.True(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Empty(varNames);
        }

        [Fact]
        public void TestInvalidRuleLinterValidateBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x.right");
            Assert.True(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Eq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.False(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Single(varNames);
            Assert.Collection(varNames,
                item => Assert.Equal("x", item)
            );
        }

        [Fact]
        public void TestInvalidRuleLinterValidateMultiVarMultiBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x.right");
            Assert.True(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("x.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.right");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("c");
            nil = Factory.Instance.MkId("0");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.False(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Equal(2, varNames.Count);
            Assert.Collection(varNames,
                item => Assert.Equal("x", item),
                item => Assert.Equal("y", item)
            );
        }

        [Fact]
        public void TestValidRuleLinterValidateMuliVarMultiBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x.right");
            Assert.True(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("x.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.right");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            var typeTerm = Factory.Instance.MkId("Node");
            varId = Factory.Instance.MkId("x");
            var find = Factory.Instance.MkFind(varId, typeTerm);
            body.Node.AddConstr(find.Node);

            varId = Factory.Instance.MkId("y");
            find = Factory.Instance.MkFind(varId, typeTerm);
            body.Node.AddConstr(find.Node);

            varId = Factory.Instance.MkId("c");
            nil = Factory.Instance.MkId("0");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.True(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Empty(varNames);
        }

        [Fact]
        public void TestValidRuleLinterValidateSingleVarMultiBodyQualifiedIds()
        {
            var body = Factory.Instance.MkBody();
            var varId = Factory.Instance.MkId("x.right");
            Assert.True(varId.Node.IsQualified);
            var nil = Factory.Instance.MkId("NIL");
            var relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("x.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.left");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            varId = Factory.Instance.MkId("y.right");
            Assert.True(varId.Node.IsQualified);
            nil = Factory.Instance.MkId("NIL");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            var typeTerm = Factory.Instance.MkId("Node");
            varId = Factory.Instance.MkId("x");
            var find = Factory.Instance.MkFind(varId, typeTerm);
            body.Node.AddConstr(find.Node);

            varId = Factory.Instance.MkId("c");
            nil = Factory.Instance.MkId("0");
            relConstr = Factory.Instance.MkRelConstr(RelKind.Neq, varId, nil);
            body.Node.AddConstr(relConstr.Node);

            List<string> varNames;
            Assert.False(RuleLinter.ValidateBodyQualifiedIds(body.Node, out varNames));
            Assert.Single(varNames);
            Assert.Collection(varNames,
                item => Assert.Equal("y", item)
            );
        }
    }
}