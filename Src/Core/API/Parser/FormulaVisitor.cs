namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Text;
    using Antlr4.Runtime;
    using Antlr4.Runtime.Misc;
    using Microsoft.Formula.API.Nodes;
    using Microsoft.Formula.Common;

    public class FormulaVisitor : FormulaParserBaseVisitor<object>
    {
        private enum ModRefState
        {
            None, ModApply, Input, Output, Other
        };

        private ParseResult parseResult;
        private Node currentModule = null;

        /******* State for building terms ********/
        private Stack<ApplyInfo> appStack = new Stack<ApplyInfo>();
        private Stack<Node> argStack = new Stack<Node>();
        private Stack<Quote> quoteStack = new Stack<Quote>();
        /*****************************************/

        /******* State for building rules, contracts, and comprehensions ********/
        private Nodes.Rule crntRule = null;
        private ContractItem crntContract = null;
        private Body crntBody = null;
        /*****************************************/

        // Additional parameters
        private EnvParams envParams;

        /****** Additional parameters *************/
        private System.Text.StringBuilder stringBuffer = new System.Text.StringBuilder();
        private Span stringStart;
        private Span stringEnd;
        /*************************************/

        /******* State for ModRefs, steps, and updates ********/
        private ModRef crntModRef = null;
        private Step crntStep = null;
        private Update crntUpdate = null;
        private ModRefState crntModRefState = ModRefState.None;
        /*************************************/

        /******* State for sentence configs ********/
        private Config crntSentConf = null;
        /*************************************/

        internal bool ParseFile(ProgramName name, string referrer, Span location, System.Threading.CancellationToken ctok, out ParseResult pr)
        {
            parseResult = new ParseResult(new Program(name));
            pr = parseResult;
            bool result = true;

            try
            {
                var fi = new System.IO.FileInfo(name.Uri.AbsolutePath);
                if (!fi.Exists)
                {
                    var badFile = new Flag(
                        SeverityKind.Error,
                        default(Span),
                        referrer == null ?
                           Constants.BadFile.ToString(string.Format("The file {0} does not exist", name.ToString(envParams))) :
                           Constants.BadFile.ToString(string.Format("The file {0} referred to in {1} ({2}, {3}) does not exist", name.ToString(envParams), referrer, location.StartLine, location.StartCol)),
                        Constants.BadFile.Code,
                        parseResult.Program.Node.Name);
                    parseResult.AddFlag(badFile);
                    parseResult.Program.Node.GetNodeHash();
                    return false;
                }

                var str = new System.IO.FileStream(name.Uri.AbsolutePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                ICharStream charStream = Antlr4.Runtime.CharStreams.fromStream(str);
                FormulaLexer lexer = new FormulaLexer(charStream);
                CommonTokenStream tokens = new CommonTokenStream(lexer);
                FormulaParser parser = new FormulaParser(tokens);
                FormulaParser.ProgramContext programContext = parser.program();
                this.VisitProgram(programContext);

                str.Close();
            }
            catch (Exception e)
            {
                var badFile = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    referrer == null ?
                       Constants.BadFile.ToString(e.Message) :
                       Constants.BadFile.ToString(string.Format("{0} referred to in {1} ({2}, {3})", e.Message, referrer, location.StartLine, location.StartCol)),
                    Constants.BadFile.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(badFile);
                parseResult.Program.Node.GetNodeHash();
                return false;
            }

            return result;
        }

        private Span ToSpan(IToken loc)
        {
            return new Span(loc.Line, loc.Column, loc.Line, loc.StopIndex, this.parseResult.Name);
        }

        /***********************************************************/
        /****************       Parse            *******************/
        /***********************************************************/
        private Rational ParseNumeric(string str, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(str));
            Rational numVal;
            if (!Rational.TryParseDecimal(str, out numVal))
            {
                var dummy = new Cnst(span, Rational.Zero);
                var flag = new Flag(
                    SeverityKind.Error,
                    span,
                    Constants.BadNumeric.ToString(str),
                    Constants.BadNumeric.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(flag);
                return Rational.Zero;
            }

            return numVal;
        }

        private Cnst ParseNumeric(string str, bool isFraction, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(str));
            Rational numVal;
            bool result;
            if (isFraction)
            {
                result = Rational.TryParseFraction(str, out numVal);
            }
            else
            {
                result = Rational.TryParseDecimal(str, out numVal);
            }

            Contract.Assert(result);
            return new Cnst(span, numVal);
        }

        private Cnst GetString(Span span)
        {
            return new Cnst(span, stringBuffer.ToString());
        }

        private void PushSymbol()
        {
            Contract.Requires(argStack.Count > 0);

            var funcName = argStack.Pop();

            Contract.Assert(funcName.NodeKind == NodeKind.Id);

            appStack.Push(new FuncApplyInfo((Id)funcName));
        }

        private void PushSymbol(OpKind opcode, Span span)
        {
            appStack.Push(new FuncApplyInfo(opcode, span));
        }

        private void PushSymbol(RelKind opcode, Span span)
        {
            appStack.Push(new RelApplyInfo(opcode, span));
        }

        private void PushComprSymbol(Span span)
        {
            appStack.Push(new ComprApplyInfo(new Compr(span)));
        }

        private void PushArg(Node n)
        {
            argStack.Push(n);
        }

        private void IncArity()
        {
            if (appStack.Count == 0)
            {
                return;
            }

            var peek = appStack.Peek();
            peek.IncArity();
        }

        private Node MkTerm(int arity = -1)
        {
            Contract.Requires(appStack.Count > 0);
            var funcInfo = appStack.Pop() as FuncApplyInfo;
            arity = arity < 0 ? funcInfo.Arity : arity;

            Contract.Assert(funcInfo != null);
            Contract.Assert(argStack.Count >= arity);
            FuncTerm data;

            if (funcInfo.FuncName is OpKind)
            {
                data = new FuncTerm(funcInfo.Span, (OpKind)funcInfo.FuncName);
            }
            else
            {
                data = new FuncTerm(funcInfo.Span, (Id)funcInfo.FuncName);
            }

            for (int i = 0; i < arity; ++i)
            {
                data.AddArg(argStack.Pop(), false);
            }

            return data;
        }

        private void AppendModRef(ModRef modRef)
        {
            Contract.Requires(currentModule != null);
            Contract.Requires(modRef != null);
            Contract.Requires(crntModRefState != ModRefState.None);
            crntModRef = modRef;
            switch (crntModRefState)
            {
                case ModRefState.Input:
                    switch (currentModule.NodeKind)
                    {
                        case NodeKind.Transform:
                            ((Transform)currentModule).AddInput(new Param(modRef.Span, null, modRef));
                            break;
                        case NodeKind.TSystem:
                            ((TSystem)currentModule).AddInput(new Param(modRef.Span, null, modRef));
                            break;
                        case NodeKind.Machine:
                            ((Machine)currentModule).AddInput(new Param(modRef.Span, null, modRef));
                            break;
                        case NodeKind.Model:
                            ((Model)currentModule).AddCompose(modRef);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case ModRefState.Output:
                    switch (currentModule.NodeKind)
                    {
                        case NodeKind.Transform:
                            ((Transform)currentModule).AddOutput(new Param(modRef.Span, null, modRef));
                            break;
                        case NodeKind.TSystem:
                            ((TSystem)currentModule).AddOutput(new Param(modRef.Span, null, modRef));
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case ModRefState.Other:
                    switch (currentModule.NodeKind)
                    {
                        case NodeKind.Domain:
                            ((Domain)currentModule).AddCompose(modRef);
                            break;
                        case NodeKind.Model:
                            ((Model)currentModule).SetDomain(modRef);
                            break;
                        case NodeKind.Machine:
                            ((Machine)currentModule).AddStateDomain(modRef);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case ModRefState.ModApply:
                    appStack.Push(new ModApplyInfo(modRef));
                    break;
            }
        }

        private void StartDomain(string name, ComposeKind kind, Span span)
        {
            Contract.Requires(currentModule == null);
            var dom = new Domain(span, name, kind);
            parseResult.Program.Node.AddModule(dom);
            currentModule = dom;
            crntModRefState = ModRefState.Other;
        }

        private void EndModule()
        {
            Contract.Requires(currentModule != null);
            currentModule = null;
            crntModRefState = ModRefState.None;
        }

        private void StartSentenceConfig(Span span)
        {
            Contract.Requires(crntSentConf == null);
            crntSentConf = new Config(span);
        }

        private void AppendSetting()
        {
            Contract.Requires(argStack.Count >= 2 && argStack.Peek().NodeKind == NodeKind.Cnst);
            var value = (Cnst)argStack.Pop();
            Contract.Assert(argStack.Peek().NodeKind == NodeKind.Id);
            var setting = (Id)argStack.Pop();

            if (currentModule == null)
            {
                Contract.Assert(crntSentConf == null);
                parseResult.Program.Node.Config.AddSetting(new Setting(setting.Span, setting, value));
                return;
            }
            else if (crntSentConf != null)
            {
                crntSentConf.AddSetting(new Setting(setting.Span, setting, value));
                return;
            }

            switch (currentModule.NodeKind)
            {
                case NodeKind.Model:
                    ((Model)currentModule).Config.AddSetting(new Setting(setting.Span, setting, value));
                    break;
                case NodeKind.Domain:
                    ((Domain)currentModule).Config.AddSetting(new Setting(setting.Span, setting, value));
                    break;
                case NodeKind.Transform:
                    ((Transform)currentModule).Config.AddSetting(new Setting(setting.Span, setting, value));
                    break;
                case NodeKind.TSystem:
                    ((TSystem)currentModule).Config.AddSetting(new Setting(setting.Span, setting, value));
                    break;
                case NodeKind.Machine:
                    ((Machine)currentModule).Config.AddSetting(new Setting(setting.Span, setting, value));
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void StartPropContract(ContractKind kind, Span span)
        {
            Contract.Requires(currentModule != null);
            Contract.Requires(currentModule.CanHaveContract(kind));
            Contract.Requires(kind != ContractKind.RequiresSome && kind != ContractKind.RequiresAtLeast && kind != ContractKind.RequiresAtMost);

            crntContract = new ContractItem(span, kind);

            switch (currentModule.NodeKind)
            {
                case NodeKind.Model:
                    ((Model)currentModule).AddContract(crntContract);
                    break;
                case NodeKind.Transform:
                    ((Transform)currentModule).AddContract(crntContract);
                    break;
                case NodeKind.Domain:
                    ((Domain)currentModule).AddConforms(crntContract);
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (crntSentConf != null)
            {
                crntContract.SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void AppendConstraint(Node n)
        {
            Contract.Requires(n != null && n.IsConstraint);
            if (appStack.Count > 0 && appStack.Peek() is ComprApplyInfo)
            {
                var cmprInfo = (ComprApplyInfo)appStack.Peek();
                if (cmprInfo.CurrentBody == null)
                {
                    cmprInfo.CurrentBody = new Body(n.Span);
                }

                cmprInfo.CurrentBody.AddConstr(n);
            }
            else
            {
                if (crntBody == null)
                {
                    crntBody = new Body(n.Span);
                }

                crntBody.AddConstr(n);
            }
        }

        private Find MkFind(bool isBound, Span span)
        {
            Contract.Requires(isBound ? argStack.Count > 1 : argStack.Count >= 1);
            var match = argStack.Pop();
            var binding = isBound ? (Id)argStack.Pop() : null;
            return new Find(span, binding, match);
        }

        private ModelFact MkFact(bool isBound, Span span)
        {
            Contract.Requires(isBound ? argStack.Count > 1 : argStack.Count >= 1);
            var match = argStack.Pop();
            var binding = isBound ? (Id)argStack.Pop() : null;
            var mf = new ModelFact(span, binding, match);
            if (crntSentConf != null)
            {
                mf.SetConfig(crntSentConf);
                crntSentConf = null;
            }

            return mf;
        }

        private RelConstr MkRelConstr(bool isNo = false)
        {
            Contract.Requires(argStack.Count > 1 && appStack.Count > 0);
            var app = appStack.Pop() as RelApplyInfo;
            Contract.Assert(app != null);
            var arg2 = argStack.Pop();
            var arg1 = argStack.Pop();
            return new RelConstr(app.Span, app.Opcode, arg1, arg2);
        }

        private RelConstr MkNoConstr(Span span)
        {
            Contract.Requires(argStack.Count > 0);
            return new RelConstr(span, RelKind.No, argStack.Pop());
        }

        private RelConstr MkNoConstr(Span span, bool hasBinding)
        {
            Contract.Requires(hasBinding ? argStack.Count > 1 : argStack.Count > 0);
            var compr = new Compr(span);
            var body = new Body(span);
            Node arg;
            Id binding;
            if (hasBinding)
            {
                arg = argStack.Pop();
                binding = (Id)argStack.Pop();
            }
            else
            {
                binding = null;
                arg = argStack.Pop();
            }

            body.AddConstr(new Find(span, binding, arg));
            compr.AddBody(body);
            compr.AddHead(new Id(span, ASTQueries.ASTSchema.Instance.ConstNameTrue));
            return new RelConstr(span, RelKind.No, compr);
        }

        private void EndHeads(Span span)
        {
            Contract.Requires(argStack.Count > 0);
            Contract.Requires(crntRule == null);
            crntRule = new Nodes.Rule(span);
            while (argStack.Count > 0)
            {
                crntRule.AddHead(argStack.Pop(), false);
            }

            if (crntSentConf != null)
            {
                crntRule.SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void AppendBody()
        {
            if (appStack.Count > 0 && appStack.Peek() is ComprApplyInfo)
            {
                var appInfo = (ComprApplyInfo)appStack.Peek();
                Contract.Assert(appInfo.CurrentBody != null);
                appInfo.Comprehension.AddBody(appInfo.CurrentBody);
                appInfo.CurrentBody = null;
            }
            else if (crntRule != null)
            {
                Contract.Assert(crntBody != null);
                crntRule.AddBody(crntBody);
                crntBody = null;
            }
            else if (crntContract != null)
            {
                Contract.Assert(crntBody != null);
                crntContract.AddSpecification(crntBody);
                crntBody = null;
            }
        }

        private void AppendRule()
        {
            Contract.Requires(currentModule != null && currentModule.IsDomOrTrans);
            switch (currentModule.NodeKind)
            {
                case NodeKind.Domain:
                    ((Domain)currentModule).AddRule(crntRule);
                    break;
                case NodeKind.Transform:
                    ((Transform)currentModule).AddRule(crntRule);
                    break;
                default:
                    throw new NotImplementedException();
            }

            crntRule = null;
        }

        public override object VisitProgram([NotNull] FormulaParser.ProgramContext context)
        {
            if (context.config() != null)
            {
                VisitConfig(context.config());
            }

            if (context.moduleList() != null)
            {
                VisitModuleList(context.moduleList());
            }

            return null;
        }

        public override object VisitConfig([NotNull] FormulaParser.ConfigContext context)
        {
            VisitSettingList(context.settingList());

            return null;
        }

        public override object VisitSettingList([NotNull] FormulaParser.SettingListContext context)
        {
            VisitSetting(context.setting());

            if (context.settingList() != null)
            {
                VisitSettingList(context.settingList());
            }

            return null;
        }

        public override object VisitSetting([NotNull] FormulaParser.SettingContext context)
        {
            VisitId(context.id());
            VisitConstant(context.constant());
            AppendSetting();

            return null;
        }

        public override object VisitModuleList([NotNull] FormulaParser.ModuleListContext context)
        {
            VisitModule(context.module());
            EndModule();

            if (context.moduleList() != null)
            {
                VisitModuleList(context.moduleList());
            }

            return null;
        }

        public override object VisitModule([NotNull] FormulaParser.ModuleContext context)
        {
            if (context.domain() != null)
            {
                VisitDomain(context.domain());
            }
            else if (context.model() != null)
            {
                VisitModel(context.model());
            }
            else if (context.transform() != null)
            {
                VisitTransform(context.transform());
            }
            else if (context.tSystem() != null)
            {
                VisitTSystem(context.tSystem());
            }
            else
            {
                VisitMachine(context.machine());
            }

            return null;
        }

        public override object VisitModel([NotNull] FormulaParser.ModelContext context)
        {
            VisitModelSigConfig(context.modelSigConfig());

            if (context.modelBody() != null)
            {
                VisitModelBody(context.modelBody());
            }

            return null;
        }

        public override object VisitModelBody([NotNull] FormulaParser.ModelBodyContext context)
        {
            VisitModelSentence(context.modelSentence());

            if (context.modelBody() != null)
            {
                VisitModelBody(context.modelBody());
            }

            return null;
        }

        public override object VisitDomain([NotNull] FormulaParser.DomainContext context)
        {
            VisitDomainSigConfig(context.domainSigConfig());

            if (context.domSentences() != null)
            {
                VisitDomSentences(context.domSentences());
            }
            
            return null;
        }

        public override object VisitDomSentences([NotNull] FormulaParser.DomSentencesContext context)
        {
            VisitDomSentenceConfig(context.domSentenceConfig());

            if (context.domSentences() != null)
            {
                VisitDomSentences(context.domSentences());
            }

            return null;
        }

        public override object VisitDomSentenceConfig([NotNull] FormulaParser.DomSentenceConfigContext context)
        {
            if (context.sentenceConfig() != null)
            {
                VisitSentenceConfig(context.sentenceConfig());
            }

            VisitDomSentence(context.domSentence());

            return null;
        }

        public override object VisitDomSentence([NotNull] FormulaParser.DomSentenceContext context)
        {
            if (context.ruleItem() != null)
            {
                VisitRuleItem(context.ruleItem());
            }
            else if (context.typeDecl() != null)
            {
                VisitTypeDecl(context.typeDecl());
            }
            else
            {
                StartPropContract(ContractKind.ConformsProp, ToSpan(context.Start));
                VisitBodyList(context.bodyList());
            }

            return null;
        }

        public override object VisitRuleItem([NotNull] FormulaParser.RuleItemContext context)
        {
            VisitFuncTermList(context.funcTermList());
            EndHeads(ToSpan(context.Start));

            if (context.bodyList() != null)
            {
                VisitBodyList(context.bodyList());
            }

            AppendRule();
            return null;
        }

        public override object VisitBodyList([NotNull] FormulaParser.BodyListContext context)
        {
            VisitBody(context.body());
            AppendBody();

            if (context.bodyList() != null)
            {
                VisitBodyList(context.bodyList());
            }

            return null;
        }

        public override object VisitBody([NotNull] FormulaParser.BodyContext context)
        {
            VisitConstraint(context.constraint());

            if (context.body() != null)
            {
                VisitBody(context.body());
            }

            return null;
        }

        public override object VisitConstraint([NotNull] FormulaParser.ConstraintContext context)
        {
            if (context.NO() != null)
            {
                if (context.compr() != null)
                {
                    VisitCompr(context.compr());
                    AppendConstraint(MkNoConstr(ToSpan(context.Start)));
                }
                else if (context.IS() != null)
                {
                    VisitId(context.id());
                    VisitFuncTerm(context.funcTerm(0));
                    AppendConstraint(MkNoConstr(ToSpan(context.Start), true));
                }
                else
                {
                    VisitFuncTerm(context.funcTerm(0));
                    AppendConstraint(MkNoConstr(ToSpan(context.Start), false));
                }
            }
            else if (context.IS() != null)
            {
                VisitId(context.id());
                VisitFuncTerm(context.funcTerm(0));
                AppendConstraint(MkFind(true, ToSpan(context.Start)));
            }
            else if (context.relOp() != null)
            {
                VisitFuncTerm(context.funcTerm(0));
                VisitRelOp(context.relOp());
                VisitFuncTerm(context.funcTerm(1));
                AppendConstraint(MkRelConstr());
            }
            else
            {
                VisitFuncTerm(context.funcTerm(0));
                AppendConstraint(MkFind(false, ToSpan(context.Start)));
            }

            return null;
        }

        public override object VisitFuncTermList([NotNull] FormulaParser.FuncTermListContext context)
        {
            VisitFuncOrCompr(context.funcOrCompr());

            IncArity();

            if (context.funcTermList() != null)
            {
                VisitFuncTermList(context.funcTermList());
            }

            return null;
        }

        public override object VisitFuncOrCompr([NotNull] FormulaParser.FuncOrComprContext context)
        {
            if (context.funcTerm() != null)
            {
                VisitFuncTerm(context.funcTerm());
            }
            else
            {
                VisitCompr(context.compr());
            }

            return null;
        }

        public override object VisitFuncTerm([NotNull] FormulaParser.FuncTermContext context)
        {
            if (context.atom() != null)
            {
                VisitAtom(context.atom());
            }
            else if (context.unOp() != null)
            {
                VisitUnOp(context.unOp());
                VisitFuncTerm(context.funcTerm(0));
                PushArg(MkTerm(1));
            }
            else if (context.binOp() != null)
            {
                VisitFuncTerm(context.funcTerm(0));
                VisitBinOp(context.binOp());
                VisitFuncTerm(context.funcTerm(1));
                PushArg(MkTerm(2));
            }
            else if (context.funcTermList() != null)
            {
                VisitId(context.id());
                PushSymbol();
                VisitFuncTermList(context.funcTermList());
                PushArg(MkTerm());
            }
            else if (context.quoteList() != null)
            {
                // TODO: implement quotes
            }
            else
            {
                VisitFuncTerm(context.funcTerm(0));
            }

            return null;
        }

        public override object VisitAtom([NotNull] FormulaParser.AtomContext context)
        {
            if (context.id() != null)
            {
                VisitId(context.id());
            }
            else
            {
                VisitConstant(context.constant());
            }

            return null;
        }

        public override object VisitId([NotNull] FormulaParser.IdContext context)
        {
            string id = context.BAREID() == null 
                ? context.QUALID().GetText()
                : context.BAREID().GetText();

            PushArg(new Nodes.Id(ToSpan(context.Start), id));

            return null;
        }

        public override object VisitConstant([NotNull] FormulaParser.ConstantContext context)
        {
            if (context.str() != null)
            {
                VisitStr(context.str());
                PushArg(GetString(ToSpan(context.Start)));
            }
            else
            {
                bool isFraction = (context.FRAC() != null);
                PushArg(ParseNumeric(context.GetText(), isFraction, ToSpan(context.Start)));
            }

            return null;
        }

        public override object VisitUnOp([NotNull] FormulaParser.UnOpContext context)
        {
            PushSymbol(OpKind.Neg, ToSpan(context.Start));

            return null;
        }

        public override object VisitBinOp([NotNull] FormulaParser.BinOpContext context)
        {
            Span span = ToSpan(context.Start);
            OpKind kind = OpKind.Mul;

            if (context.MUL() != null)
            {
                kind = OpKind.Mul;
            }
            else if (context.DIV() != null)
            {
                kind = OpKind.Div;
            }
            else if (context.MOD() != null)
            {
                kind = OpKind.Mod;
            }
            else if (context.PLUS() != null)
            {
                kind = OpKind.Add;
            }
            else if (context.MINUS() != null)
            {
                kind = OpKind.Sub;
            }

            PushSymbol(kind, span);

            return null;
        }

        public override object VisitRelOp([NotNull] FormulaParser.RelOpContext context)
        {
            Span span = ToSpan(context.Start);
            RelKind kind = RelKind.Eq;

            if (context.EQ() != null)
            {
                kind = RelKind.Eq;
            }
            else if (context.NE() != null)
            {
                kind = RelKind.Neq;
            }
            else if (context.LT() != null)
            {
                kind = RelKind.Lt;
            }
            else if (context.LE() != null)
            {
                kind = RelKind.Le;
            }
            else if (context.GT() != null)
            {
                kind = RelKind.Gt;
            }
            else if (context.GE() != null)
            {
                kind = RelKind.Ge;
            }
            else if (context.COLON() != null)
            {
                kind = RelKind.Typ;
            }

            PushSymbol(kind, span);

            return null;
        }

        public override object VisitStr([NotNull] FormulaParser.StrContext context)
        {
            // TODO: reimplement this with escapes
            string str = null;
            if (context.STRING() != null)
            {
                str = context.STRING().GetText();
                str = str.Substring(1, str.Length - 2);
            }
            else
            {
                str = context.STRINGMUL().GetText();
                str = str.Substring(2, str.Length - 4);
            }

            stringBuffer.Append(str);

            return null;
        }

        public override object VisitSentenceConfig([NotNull] FormulaParser.SentenceConfigContext context)
        {
            StartSentenceConfig(ToSpan(context.Start));
            VisitSettingList(context.settingList());

            return null;
        }

        public override object VisitDomainSigConfig([NotNull] FormulaParser.DomainSigConfigContext context)
        {
            VisitDomainSig(context.domainSig());

            if (context.config() != null)
            {
                VisitConfig(context.config());
            }

            return null;
        }

        public override object VisitDomainSig([NotNull] FormulaParser.DomainSigContext context)
        {
            string domName = context.BAREID().GetText();
            ComposeKind composeKind = context.EXTENDS() != null ? ComposeKind.Extends :
                                      context.INCLUDES() != null ? ComposeKind.Includes :
                                      ComposeKind.None;

            StartDomain(domName, composeKind, ToSpan(context.Start));

            if (context.modRefs() != null)
            {
                VisitModRefs(context.modRefs());
            }

            return null;
        }

        public override object VisitModRefs([NotNull] FormulaParser.ModRefsContext context)
        {
            VisitModRef(context.modRef());

            if (context.modRefs() != null)
            {
                VisitModRefs(context.modRefs());
            }

            return null;
        }

        public override object VisitModRef([NotNull] FormulaParser.ModRefContext context)
        {
            if (context.modRefRename() != null)
            {
                VisitModRefRename(context.modRefRename());
            }
            else
            {
                VisitModRefNoRename(context.modRefNoRename());
            }

            return null;
        }

        public override object VisitModRefRename([NotNull] FormulaParser.ModRefRenameContext context)
        {
            string rename = context.BAREID(0).GetText();
            string name = context.BAREID(1).GetText();
            string loc = context.AT() == null ? null : context.str().GetText();

            AppendModRef(new Nodes.ModRef(ToSpan(context.BAREID(0).Symbol), name, rename, loc));

            return null;
        }

        public override object VisitModRefNoRename([NotNull] FormulaParser.ModRefNoRenameContext context)
        {
            string name = context.BAREID().GetText();
            string loc = context.AT() == null ? null : context.str().GetText();

            AppendModRef(new Nodes.ModRef(ToSpan(context.BAREID().Symbol), name, null, loc));

            return null;
        }

        #region Apply Info classes
        private abstract class ApplyInfo
        {
            public NodeKind AppKind
            {
                get;
                private set;
            }

            public Span Span
            {
                get;
                private set;
            }

            public abstract int Arity
            {
                get;
            }

            public abstract void IncArity();

            public ApplyInfo(NodeKind appKind, Span span)
            {
                AppKind = appKind;
                Span = span;
            }
        }

        private class FuncApplyInfo : ApplyInfo
        {
            private int arity = 0;

            public override int Arity
            {
                get { return arity; }
            }

            public object FuncName
            {
                get;
                private set;
            }

            public override void IncArity()
            {
                ++arity;
            }

            public FuncApplyInfo(Id id)
                : base(NodeKind.FuncTerm, id.Span)
            {
                FuncName = id;
            }

            public FuncApplyInfo(OpKind kind, Span span)
                : base(NodeKind.FuncTerm, span)
            {
                FuncName = kind;
            }
        }

        private class RelApplyInfo : ApplyInfo
        {
            public RelKind Opcode
            {
                get;
                private set;
            }

            public override int Arity
            {
                get { return 2; }
            }

            public override void IncArity()
            {
                throw new InvalidOperationException();
            }

            public RelApplyInfo(RelKind opcode, Span span)
                : base(NodeKind.RelConstr, span)
            {
                Opcode = opcode;
            }
        }

        private class ComprApplyInfo : ApplyInfo
        {
            private int arity = 0;

            public override int Arity
            {
                get { return arity; }
            }

            public Compr Comprehension
            {
                get;
                private set;
            }

            public Body CurrentBody
            {
                get;
                set;
            }

            public override void IncArity()
            {
                ++arity;
            }

            public ComprApplyInfo(Compr compr)
                : base(NodeKind.Compr, compr.Span)
            {
                Comprehension = compr;
            }
        }

        private class ModApplyInfo : ApplyInfo
        {
            private int arity = 0;

            public override int Arity
            {
                get { return arity; }
            }

            public override void IncArity()
            {
                ++arity;
            }

            public ModRef ModRef
            {
                get;
                private set;
            }

            public ModApplyInfo(ModRef modRef)
                : base(NodeKind.ModApply, modRef.Span)
            {
                ModRef = modRef;
            }
        }

        #endregion
    }
}
