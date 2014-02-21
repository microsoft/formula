namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;
    using Nodes;
    using QUT.Gppg;

    using Microsoft.Formula.Common;
    internal partial class Parser : ShiftReduceParser<LexValue, LexLocation>
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

        /******* State for building types and type declarations ********/
        private string crntTypeDeclName = null;
        private Span crntTypeDeclSpan = default(Span);
        private Node crntTypeDecl = null;
        private Node crntTypeTerm = null;
        private Nodes.Enum currentEnum = null;
        /*****************************************/

        /******* State for ModRefs, steps, and updates ********/
        private ModRef crntModRef = null;
        private Step crntStep = null;
        private Update crntUpdate = null;
        private ModRefState crntModRefState = ModRefState.None;
        /*************************************/

        /******* State for sentence configs ********/
        private Config crntSentConf = null;
        /*************************************/

        /****** Additional parameters *************/
        private EnvParams envParams;
        /*************************************/

        /****** Additional parameters *************/
        private System.Text.StringBuilder stringBuffer = new System.Text.StringBuilder();
        private Span stringStart;
        private Span stringEnd;
        /*************************************/

        private bool IsBuildingNext
        {
            get;
            set;
        }

        private bool IsBuildingUpdate
        {
            get;
            set;
        }

        private bool IsBuildingCod
        {
            get;
            set;
        }

        public Parser(EnvParams envParams = null)
            : base(new Scanner())
        {
            this.envParams = envParams;
        }

        internal AST<Node> ParseFuncTerm(string text, out ParseResult pr)
        {
            parseResult = new ParseResult();
            pr = parseResult;
            text = string.Format("domain Dummy {{ dummy({0}). }}", text);

            var str = new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(text));
            ((Scanner)Scanner).SetSource(str);
            ((Scanner)Scanner).ParseResult = parseResult;
            ResetState();
            var result = Parse(default(System.Threading.CancellationToken));
            str.Close();

            if (!result)
            {
                parseResult.Program.Node.GetNodeHash();
                return null;
            }

            parseResult.Program.Node.GetNodeHash();
            return Factory.Instance.ToAST(((FuncTerm)((Domain)parseResult.Program.Node.Modules.First<Node>()).Rules.First<Nodes.Rule>().Heads.First<Node>()).Args.First<Node>());
        }

        internal bool ParseFile(ProgramName name, string referrer, Span location, System.Threading.CancellationToken ctok, out ParseResult pr)
        {
            parseResult = new ParseResult(new Program(name));
            pr = parseResult;
            bool result;

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
                ((Scanner)Scanner).SetSource(str);
                ((Scanner)Scanner).ParseResult = parseResult;
                ResetState();

                result = Parse(ctok);
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

            if (ctok.IsCancellationRequested)
            {
                var badFile = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    referrer == null ?
                       Constants.OpCancelled.ToString(string.Format("Cancelled parsing of {0}", name.ToString(envParams))) :
                       Constants.OpCancelled.ToString(string.Format("Cancelled parsing of {0} referred to in {1} ({2}, {3})", name.ToString(envParams), referrer, location.StartLine, location.StartCol)),
                    Constants.OpCancelled.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(badFile);
                parseResult.Program.Node.GetNodeHash();
                return false;
            }

            parseResult.Program.Node.GetNodeHash();
            return result;
        }

        internal bool ParseText(ProgramName name, string programText, Span location, System.Threading.CancellationToken ctok, out ParseResult pr)
        {
            parseResult = new ParseResult(new Program(name));
            pr = parseResult;
            bool result;

            try
            {
                var str = new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(programText));
                ((Scanner)Scanner).SetSource(str);
                ((Scanner)Scanner).ParseResult = parseResult;
                ResetState();

                result = Parse(ctok);
                str.Close();
            }
            catch (Exception e)
            {
                var badFile = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.BadFile.ToString(e.Message),
                    Constants.BadFile.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(badFile);
                parseResult.Program.Node.GetNodeHash();
                return false;
            }

            if (ctok.IsCancellationRequested)
            {
                var badFile = new Flag(
                    SeverityKind.Error,
                    default(Span),
                    Constants.OpCancelled.ToString(string.Format("Cancelled parsing of {0}", name.ToString(envParams))),
                    Constants.OpCancelled.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(badFile);
                parseResult.Program.Node.GetNodeHash();
                return false;
            }

            parseResult.Program.Node.GetNodeHash();
            return result;
        }

        #region Helpers
        private Span ToSpan(LexLocation loc)
        {
            return new Span(loc.StartLine, loc.StartColumn + 1, loc.EndLine, loc.EndColumn + 1);
        }

        private void ResetState()
        {
            currentModule = null;
            parseResult.ClearFlags();
            /******* State for building terms ********/
            appStack.Clear();
            argStack.Clear();
            quoteStack.Clear();
            /*****************************************/

            /******* State for building rules, contracts, and comprehensions ********/
            crntRule = null;
            crntContract = null;
            crntBody = null;
            /*****************************************/

            /******* State for building types and type declarations ********/
            crntTypeDeclName = null;
            crntTypeDeclSpan = default(Span);
            crntTypeDecl = null;
            crntTypeTerm = null;
            currentEnum = null;
            /*****************************************/

            /******* State for ModRefs, steps, and updates ********/
            crntModRef = null;
            crntStep = null;
            crntUpdate = null;
            crntModRefState = ModRefState.None;
            /*************************************/

            /******* State for sentence configs ********/
            crntSentConf = null;
            /*************************************/

            IsBuildingNext = false;
            IsBuildingUpdate = false;
            IsBuildingCod = false;
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

        private int ParseInt(string str, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(str));
            int numVal;
            if (!int.TryParse(str, out numVal))
            {
                var dummy = new Cnst(span, Rational.Zero);
                var flag = new Flag(
                    SeverityKind.Error,
                    span,
                    Constants.BadNumeric.ToString(str),
                    Constants.BadNumeric.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(flag);
                return 0;
            }

            return numVal;
        }

        private Cnst GetString()
        {
            return new Cnst(
                new Span(stringStart.StartLine, stringStart.StartCol, stringEnd.EndLine, stringEnd.EndCol),
                stringBuffer.ToString());

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

        private string GetStringValue()
        {
            return stringBuffer.ToString();
        }       
        #endregion

        #region Term and Constraint Building
        private void StartString(Span span)
        {
            stringBuffer.Clear();
            stringStart = span;
        }

        private void EndString(Span span)
        {
            stringEnd = span;
        }

        private void AppendString(string s)
        {
            stringBuffer.Append(s);
        }

        private void AppendSingleEscape(string s)
        {
            var c = s[1];
            switch (c)
            {
                case 'r':
                    stringBuffer.Append('\r');
                    break;
                case 'n':
                    stringBuffer.Append('\n');
                    break;
                case 't':
                    stringBuffer.Append('\t');
                    break;
                default:
                    stringBuffer.Append(c);
                    break;
            }
        }

        private void AppendMultiEscape(string s)
        {
            if (s == "\'\'\"\"")
            {
                stringBuffer.Append("\'\"");
            }
            else if (s == "\"\"\'\'")
            {
                stringBuffer.Append("\"\'");
            }
            else
            {
                throw new NotImplementedException();
            }
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

        private void AppendQuoteRun(string s, Span span)
        {
            Contract.Requires(quoteStack.Count > 0);
            quoteStack.Peek().AddItem(new QuoteRun(span, s));
        }

        private void AppendQuoteEscape(string s, Span span)
        {
            Contract.Requires(quoteStack.Count > 0 && s.Length == 2);
            quoteStack.Peek().AddItem(new QuoteRun(span, new string(new char[] { s[1] })));
        }

        private void AppendUnquote()
        {
            Contract.Requires(quoteStack.Count > 0);
            Contract.Requires(argStack.Count > 0 && argStack.Peek().IsFuncOrAtom);
            quoteStack.Peek().AddItem(argStack.Pop());
        }

        private void PushQuote(Span span)
        {
            quoteStack.Push(new Quote(span));
        }

        private void EndComprHeads()
        {
            Contract.Requires(appStack.Count > 0 && appStack.Peek() is ComprApplyInfo);
            Contract.Requires(argStack.Count > 0);

            var comprInfo = (ComprApplyInfo)appStack.Peek();
            Contract.Assert(argStack.Count >= comprInfo.Arity);
            for (int i = 0; i < comprInfo.Arity; ++i)
            {
                comprInfo.Comprehension.AddHead(argStack.Pop(), false);
            }
        }

        private Quote PopQuote()
        {
            Contract.Requires(quoteStack.Count > 0);
            return quoteStack.Pop();
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

        private Compr MkCompr()
        {
            Contract.Requires(appStack.Count > 0 && appStack.Peek() is ComprApplyInfo);
            return ((ComprApplyInfo)appStack.Pop()).Comprehension;
        }

        private ModApply MkModApply()
        {
            Contract.Requires(appStack.Count > 0 && appStack.Peek() is ModApplyInfo);
            var modInfo = (ModApplyInfo)appStack.Pop();
            var modApp = new ModApply(modInfo.Span, modInfo.ModRef);
            for (int i = 0; i < modInfo.Arity; ++i)
            {
                modApp.AddArg(argStack.Pop(), false);
            }

            return modApp;
        }
        #endregion

        #region Type Term and Declaration Building
        private void StartEnum(Span span)
        {
            Contract.Requires(currentEnum == null);
            currentEnum = new Nodes.Enum(span);
        }

        private void AppendEnum(Node n)
        {
            Contract.Requires(currentEnum != null);
            Contract.Requires(n != null);
            currentEnum.AddElement(n);
        }

        private void AppendUnion(Node n)
        {
            Contract.Requires(n != null);
            if (crntTypeTerm == null)
            {
                crntTypeTerm = n;
            }
            else if (crntTypeTerm.NodeKind == NodeKind.Union)
            {
                ((Union)crntTypeTerm).AddComponent(n);
            }
            else
            {
                var unn = new Union(crntTypeTerm.Span);
                unn.AddComponent(crntTypeTerm);
                unn.AddComponent(n);
                crntTypeTerm = unn;
            }
        }

        private void EndEnum()
        {
            Contract.Requires(currentEnum != null);
            Contract.Ensures(currentEnum == null);

            if (crntTypeTerm == null)
            {
                crntTypeTerm = currentEnum;
            }
            else if (crntTypeTerm.NodeKind == NodeKind.Union)
            {
                ((Union)crntTypeTerm).AddComponent(currentEnum);
            }
            else
            {
                var unn = new Union(crntTypeTerm.Span);
                unn.AddComponent(crntTypeTerm);
                unn.AddComponent(currentEnum);
                crntTypeTerm = unn;
            }

            currentEnum = null;
        }

        private void SaveTypeDeclName(string name, Span span)
        {
            crntTypeDeclName = name;
            crntTypeDeclSpan = span;
        }

        private void EndUnnDecl()
        {
            Contract.Requires(currentModule != null && currentModule.IsDomOrTrans);
            var unnDecl = new UnnDecl(crntTypeDeclSpan, crntTypeDeclName, crntTypeTerm);
            crntTypeTerm = null;
            crntTypeDeclName = null;
            crntTypeDeclSpan = default(Span);

            switch (currentModule.NodeKind)
            {
                case NodeKind.Domain:
                    ((Domain)currentModule).AddTypeDecl(unnDecl);
                    break;
                case NodeKind.Transform:
                    ((Transform)currentModule).AddTypeDecl(unnDecl);
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (crntSentConf != null)
            {
                unnDecl.SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void StartConDecl(bool isNew, bool isSub)
        {
            Contract.Requires(currentModule != null && currentModule.IsDomOrTrans);
            Contract.Requires(crntTypeDecl == null);
            crntTypeDecl = new ConDecl(crntTypeDeclSpan, crntTypeDeclName, isNew, isSub);
            if (crntSentConf != null)
            {
                ((ConDecl)crntTypeDecl).SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void StartMapDecl(MapKind kind)
        {
            Contract.Requires(currentModule != null && currentModule.IsDomOrTrans);
            Contract.Requires(crntTypeDecl == null);
            crntTypeDecl = new MapDecl(crntTypeDeclSpan, crntTypeDeclName, kind, true);
            if (crntSentConf != null)
            {
                ((MapDecl)crntTypeDecl).SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void EndTypeDecl()
        {
            Contract.Requires(currentModule != null && currentModule.IsDomOrTrans);
            Contract.Requires(crntTypeDecl != null);
            switch (currentModule.NodeKind)
            {
                case NodeKind.Domain:
                    ((Domain)currentModule).AddTypeDecl(crntTypeDecl);
                    break;
                case NodeKind.Transform:
                    ((Transform)currentModule).AddTypeDecl(crntTypeDecl);
                    break;
                default:
                    throw new NotImplementedException();
            }

            IsBuildingCod = false;
            crntTypeDecl = null;
        }

        private void SaveMapPartiality(bool isPartial)
        {
            Contract.Requires(crntTypeDecl != null && crntTypeDecl.NodeKind == NodeKind.MapDecl);
            ((MapDecl)crntTypeDecl).ChangePartiality(isPartial);
            IsBuildingCod = true;
        }

        private void SetModRefState(ModRefState state)
        {
            crntModRefState = state;
        }
       
        private void AppendField(string name, bool isAny, Span span)
        {
            Contract.Requires(crntTypeDecl != null);
            Contract.Requires(crntTypeTerm != null);
            var fld = new Field(span, name, crntTypeTerm, isAny);
            crntTypeTerm = null;
            switch (crntTypeDecl.NodeKind)
            {
                case NodeKind.ConDecl:
                    ((ConDecl)crntTypeDecl).AddField(fld);
                    break;
                case NodeKind.MapDecl:
                    if (IsBuildingCod)
                    {
                        ((MapDecl)crntTypeDecl).AddCodField(fld);
                    }
                    else
                    {
                        ((MapDecl)crntTypeDecl).AddDomField(fld);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        
        private static ContractKind ToContractKind(string s)
        {
            switch (s)
            {
                case "some":
                    return ContractKind.RequiresSome;
                case "atleast":
                    return ContractKind.RequiresAtLeast;
                case "atmost":
                    return ContractKind.RequiresAtMost;
                default:
                    throw new NotImplementedException();
            }
        }
        #endregion

        #region Rule, Contract, Step, and Update Building
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

        private void AppendCardContract(string kind, int cardinality, Span span)
        {
            Contract.Requires(currentModule != null && currentModule.NodeKind == NodeKind.Model);
            Contract.Requires(argStack.Count > 0 && argStack.Peek().NodeKind == NodeKind.Id);

            if (cardinality < 0)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    span,
                    Constants.BadNumeric.ToString(cardinality),
                    Constants.BadNumeric.Code,
                    parseResult.Program.Node.Name);
                parseResult.AddFlag(flag);
                cardinality = 0;
            }

            var ci = new ContractItem(span, ToContractKind(kind));
            ci.AddSpecification(new CardPair(span, (Id)argStack.Pop(), cardinality));

            switch (currentModule.NodeKind)
            {
                case NodeKind.Model:
                    ((Model)currentModule).AddContract(ci);
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (crntSentConf != null)
            {
                ci.SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void AppendProperty(string name, Span span)
        {
            Contract.Requires(currentModule.NodeKind == NodeKind.Machine);
            Contract.Requires(argStack.Count > 0 && argStack.Peek().IsFuncOrAtom);

            var prop = new Property(span, name, argStack.Pop());
            ((Machine)currentModule).AddProperty(prop);
            if (crntSentConf != null)
            {
                prop.SetConfig(crntSentConf);
                crntSentConf = null;
            }
        }

        private void AppendFact(ModelFact p)
        {
            Contract.Requires(currentModule is Model);
            ((Model)currentModule).AddFact(p);
        }

        private void AppendUpdate()
        {
            Contract.Requires(crntUpdate != null);
            Contract.Requires(currentModule != null && currentModule.NodeKind == NodeKind.Machine);
            if (IsBuildingNext)
            {
                ((Machine)currentModule).AddUpdate(crntUpdate, false);
            }
            else
            {
                ((Machine)currentModule).AddUpdate(crntUpdate, true);
            }

            crntUpdate = null;
        }

        private void AppendStep()
        {
            Contract.Requires(currentModule != null);
            Contract.Requires(argStack.Count > 0 && argStack.Peek().NodeKind == NodeKind.ModApply);
            crntStep.SetRhs((ModApply)argStack.Pop());
            switch (currentModule.NodeKind)
            {
                case NodeKind.TSystem:
                    ((TSystem)currentModule).AddStep(crntStep);
                    break;
                case NodeKind.Machine:
                    ((Machine)currentModule).AddBootStep(crntStep);
                    break;
                default:
                    throw new NotImplementedException();
            }

            crntStep = null;
        }

        private void AppendChoice()
        {
            Contract.Requires(crntUpdate != null);
            Contract.Requires(argStack.Count > 0 && argStack.Peek().NodeKind == NodeKind.ModApply);
            crntUpdate.AddChoice((ModApply)argStack.Pop());
        }

        private void AppendLHS()
        {
            Contract.Requires(argStack.Count > 0 && argStack.Peek().NodeKind == NodeKind.Id);
            var id = (Id)argStack.Pop();

            if (IsBuildingUpdate)
            {
                if (crntUpdate == null)
                {
                    crntUpdate = new Update(id.Span);
                    if (crntSentConf != null)
                    {
                        crntUpdate.SetConfig(crntSentConf);
                        crntSentConf = null;
                    }
                }

                crntUpdate.AddState(id);
            }
            else
            {
                if (crntStep == null)
                {
                    crntStep = new Step(id.Span);
                    if (crntSentConf != null)
                    {
                        crntStep.SetConfig(crntSentConf);
                        crntSentConf = null;
                    }
                }

                crntStep.AddLhs(id);
            }
        }
        #endregion

        #region Module Building
        private void SetCompose(ComposeKind kind)
        {
            Contract.Requires(currentModule != null);
            Contract.Requires(currentModule.NodeKind == NodeKind.Model);
            ((Model)currentModule).SetCompose(kind);
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

        private void AppendParam(string name, Span span)
        {
            Contract.Requires(currentModule != null);
            Contract.Requires(crntTypeTerm != null);
            Contract.Requires(crntModRefState == ModRefState.Input || crntModRefState == ModRefState.Output);
            switch (crntModRefState)
            {
                case ModRefState.Input:
                    switch (currentModule.NodeKind)
                    {
                        case NodeKind.Transform:
                            ((Transform)currentModule).AddInput(new Param(span, name, crntTypeTerm));
                            break;
                        case NodeKind.TSystem:
                            ((TSystem)currentModule).AddInput(new Param(span, name, crntTypeTerm));
                            break;
                        case NodeKind.Machine:
                            ((Machine)currentModule).AddInput(new Param(span, name, crntTypeTerm));
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case ModRefState.Output:
                    switch (currentModule.NodeKind)
                    {
                        case NodeKind.Transform:
                            ((Transform)currentModule).AddOutput(new Param(span, name, crntTypeTerm));
                            break;
                        case NodeKind.TSystem:
                            ((TSystem)currentModule).AddOutput(new Param(span, name, crntTypeTerm));
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }

            crntTypeTerm = null;
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

        private void StartTransform(string name, Span span)
        {
            Contract.Requires(currentModule == null);
            var trans = new Transform(span, name);
            parseResult.Program.Node.AddModule(trans);
            crntModRefState = ModRefState.Input;
            currentModule = trans;
        }

        private void StartTSystem(string name, Span span)
        {
            Contract.Requires(currentModule == null);
            var tsys = new TSystem(span, name);
            parseResult.Program.Node.AddModule(tsys);
            crntModRefState = ModRefState.Input;
            currentModule = tsys;
        }

        private void StartModel(string name, bool isPartial, Span span)
        {
            Contract.Requires(currentModule == null);
            currentModule = new Model(span, name, isPartial);
            parseResult.Program.Node.AddModule(currentModule);
            crntModRefState = ModRefState.Other;
        }

        private void StartMachine(string name, Span span)
        {
            Contract.Requires(currentModule == null);
            var mach = new Machine(span, name);
            parseResult.Program.Node.AddModule(mach);
            currentModule = mach;
            crntModRefState = ModRefState.Input;
        }
        #endregion

        #region Configs and Settings
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
        #endregion

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
