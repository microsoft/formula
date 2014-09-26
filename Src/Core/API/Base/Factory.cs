namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Nodes;
    using Common;

    public sealed class Factory
    {
        private static readonly Factory instance = new Factory();

        private static readonly char[] Whitespaces = new char[] { ' ', '\t', '\n' };

        public static Factory Instance
        {
            get { return instance; }
        }

        /***********************************************************/
        /****************        Mks             *******************/
        /***********************************************************/

        /// <summary>
        /// Builds a model containing the facts in ground terms. If a term is aliased, then
        /// its alias is introduced and every occurrence of the term is replaced by the alias.
        /// </summary>
        public bool MkModel(string modelName,
                            string domainName,
                            IEnumerable<Generators.ICSharpTerm> groundTerms,
                            out AST<Model> model,
                            Dictionary<Generators.ICSharpTerm, string> aliases = null,                            
                            string domainLocation = null,
                            ComposeKind composeKind = ComposeKind.None)
        {
            Contract.Requires(!string.IsNullOrEmpty(modelName));
            Contract.Requires(!string.IsNullOrEmpty(domainName));
            Contract.Requires(groundTerms != null);

            var bldr = new Builder();
            var factCount = 0;
            foreach (var t in groundTerms)
            {
                ++factCount;
                MkModelFact(bldr, t, aliases);
            }

            int onStack;
            if (bldr.GetStackCount(out onStack) != BuilderResultKind.Success || onStack != factCount)
            {
                model = null;
                return false;
            }

            bldr.PushModRef(domainName, null, domainLocation);
            bldr.PushModel(modelName, false, composeKind);           
            for (int i = 0; i < factCount; ++i)
            {
                bldr.AddModelFact();
            }

            bldr.GetStackCount(out onStack);
            bldr.Close();
            if (onStack != 1)
            {
                model = null;
                return false;
            }

            ImmutableArray<AST<Node>> asts;
            bldr.GetASTs(out asts);
            model = (AST<Model>)asts[0];
            return true;
        }

        /// <summary>
        /// Converts a C# embedding of a Formula term into a Formula AST.
        /// </summary>
        public bool MkGroundTerm(Generators.ICSharpTerm groundTerm, out AST<Node> ast)
        {
            Contract.Requires(groundTerm != null);
            var bldr = new Builder();
            var stack = new Stack<MutableTuple<Generators.ICSharpTerm, int, string>>();
            var opened = new HashSet<Generators.ICSharpTerm>();

            stack.Push(new MutableTuple<Generators.ICSharpTerm, int, string>(groundTerm, -1, string.Empty));
            string s, ns, rs;
            Generators.ICSharpTerm ct;
            MutableTuple<Generators.ICSharpTerm, int, string> top;
            while (stack.Count > 0)
            {
                top = stack.Peek();
                if (top.Item2 == -1)
                {
                    if (opened.Contains(top.Item1))
                    {
                        throw new Exception("Object graph is cyclic; this is not allowed");
                    }

                    opened.Add(top.Item1);
                }

                top.Item2++;
                ct = top.Item1;
                if (top.Item2 == ct.Arity)
                {
                    if (ct.Arity == 0)
                    {
                        if (ct.Symbol is string)
                        {
                            if (ct.GetType().Name == Generators.CSharpDataModelGen.QuotationClassName)
                            {
                                s = (string)ct.Symbol;
                                bldr.PushQuoteRun(s.Length == 2 ? string.Empty : s.Substring(1, s.Length - 2));
                                bldr.PushQuote();
                                bldr.AddQuoteItem();
                            }
                            else if (ct.GetType().Name == Generators.CSharpDataModelGen.UserCnstClassName)
                            {
                                GetResidualSymbol(top.Item3, (string)ct.Symbol, out rs, out ns);
                                bldr.PushId(rs);
                            }
                            else
                            {
                                bldr.PushCnst(ct.Symbol == null ? string.Empty : (string)ct.Symbol);
                            }
                        }
                        else if (ct.Symbol is Rational)
                        {
                            bldr.PushCnst((Rational)ct.Symbol);
                        }
                        else
                        {
                            throw new Exception(string.Format("Unexpected symbol {0}", ct.Symbol));
                        }
                    }
                    else
                    {
                        GetResidualSymbol(top.Item3, (string)ct.Symbol, out rs, out ns);
                        bldr.PushId(rs);
                        bldr.PushFuncTerm();
                        for (int i = 0; i < top.Item1.Arity; ++i)
                        {
                            bldr.AddFuncTermArg();
                        }
                    }

                    opened.Remove(top.Item1);
                    stack.Pop();
                }
                else
                {
                    GetResidualSymbol(string.Empty, (string)ct.Symbol, out rs, out ns);
                    stack.Push(new MutableTuple<Generators.ICSharpTerm, int, string>(ct[top.Item2], -1, ns));
                }
            }

            var n = bldr.Close();
            if (n != 1)
            {
                ast = null;
                return false;
            }

            ImmutableArray<AST<Node>> asts;
            if (!bldr.GetASTs(out asts))
            {
                ast = null;
                return false;
            }

            ast = asts[0];
            return true;
        }

        public AST<Cnst> MkCnst(int value, Span span = default(Span))
        {
            return new ASTConcr<Cnst>(new Cnst(span, new Rational(value)));
        }

        public AST<Cnst> MkCnst(double value, Span span = default(Span))
        {
            return new ASTConcr<Cnst>(new Cnst(span, new Rational(value)));
        }

        public AST<Cnst> MkCnst(Rational value, Span span = default(Span))
        {
            return new ASTConcr<Cnst>(new Cnst(span, value));
        }

        public AST<Cnst> MkCnst(string value, Span span = default(Span))
        {
            Contract.Requires(value != null);
            return new ASTConcr<Cnst>(new Cnst(span, value));
        }

        public AST<Id> MkId(string name, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<Id>(new Id(span, name));
        }

        public AST<FuncTerm> MkFuncTerm(AST<Id> id, Span span = default(Span))
        {
            Contract.Requires(id != null);
            return new ASTConcr<FuncTerm>(new FuncTerm(span, id.Node));
        }

        public AST<FuncTerm> MkFuncTerm(OpKind kind, Span span = default(Span))
        {
            return new ASTConcr<FuncTerm>(new FuncTerm(span, kind));
        }

        public AST<FuncTerm> MkFuncTerm(AST<Id> id, Span span, params AST<Node>[] args)
        {
            Contract.Requires(id != null);
            Contract.Requires(Contract.ForAll(args, x => x.Node.IsFuncOrAtom));

            var n = new FuncTerm(span, id.Node);
            for (int i = 0; i < args.Length; ++i)
            {
                n.AddArg(args[i].Node);
            }

            return new ASTConcr<FuncTerm>(n);
        }

        public AST<FuncTerm> MkFuncTerm(OpKind kind, Span span, params AST<Node>[] args)
        {
            Contract.Requires(Contract.ForAll(args, x => x.Node.IsFuncOrAtom));

            var n = new FuncTerm(span, kind);
            for (int i = 0; i < args.Length; ++i)
            {
                n.AddArg(args[i].Node);
            }

            return new ASTConcr<FuncTerm>(n);
        }

        public AST<Range> MkRange(Rational end1, Rational end2, Span span = default(Span))
        {
            Contract.Requires(end1.IsInteger && end2.IsInteger);
            return new ASTConcr<Range>(new Range(span, end1, end2));
        }

        public AST<ModApply> MkModApply(AST<ModRef> module, Span span = default(Span))
        {
            Contract.Requires(module != null);
            return new ASTConcr<ModApply>(new ModApply(span, module.Node));
        }

        public AST<RelConstr> MkRelConstr(RelKind op, AST<Node> arg1, AST<Node> arg2, Span span = default(Span))
        {
            Contract.Requires(arg1 != null && arg1.Node.IsFuncOrAtom);
            Contract.Requires(arg2 != null && arg2.Node.IsFuncOrAtom);
            return new ASTConcr<RelConstr>(new RelConstr(span, op, arg1.Node, arg2.Node));
        }

        public AST<RelConstr> MkNo(AST<Compr> compr, Span span = default(Span))
        {
            Contract.Requires(compr != null);
            return new ASTConcr<RelConstr>(new RelConstr(span, RelKind.No, compr.Node));
        }

        public AST<Find> MkFind(AST<Id> binding, AST<Node> match, Span span = default(Span))
        {
            Contract.Requires(match != null && match.Node.IsFuncOrAtom);
            return new ASTConcr<Find>(new Find(span, binding == null ? null : binding.Node, match.Node));
        }

        public AST<ModelFact> MkModelFact(AST<Id> binding, AST<Node> match, Span span = default(Span))
        {
            Contract.Requires(match != null && match.Node.IsFuncOrAtom);
            return new ASTConcr<ModelFact>(new ModelFact(span, binding == null ? null : binding.Node, match.Node));
        }

        public AST<Body> MkBody(Span span = default(Span))
        {
            return new ASTConcr<Body>(new Body(span));        
        }

        public AST<Rule> MkRule(Span span = default(Span))
        {
            return new ASTConcr<Rule>(new Rule(span));
        }

        public AST<Config> MkConfig(Span span = default(Span))
        {
            return new ASTConcr<Config>(new Config(span));
        }

        public AST<Compr> MkComprehension(Span span = default(Span))
        {
            return new ASTConcr<Compr>(new Compr(span));
        }

        public AST<Field> MkField(string name, AST<Node> type, bool isAny, Span span = default(Span))
        {
            Contract.Requires(type != null && type.Node.IsTypeTerm);
            return new ASTConcr<Field>(new Field(span, name, type.Node, isAny));
        }

        public AST<ConDecl> MkConDecl(string name, bool isNew, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<ConDecl>(new ConDecl(span, name, isNew, false));
        }

        public AST<ConDecl> MkSubDecl(string name, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<ConDecl>(new ConDecl(span, name, false, true));
        }

        public AST<MapDecl> MkMapDecl(string name, MapKind kind, bool isPartial, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<MapDecl>(new MapDecl(span, name, kind, isPartial));
        }

        public AST<ModRef> MkModRef(string name, string rename, string loc, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<ModRef>(new ModRef(span, name, rename, loc));
        }

        public AST<Nodes.Enum> MkEnum(Span span = default(Span))
        {
            return new ASTConcr<Nodes.Enum>(new Nodes.Enum(span));
        }

        public AST<Union> MkUnion(Span span = default(Span))
        {
            return new ASTConcr<Union>(new Union(span));
        }

        public AST<UnnDecl> MkUnnDecl(string name, AST<Node> body, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(body != null && body.Node.IsTypeTerm);
            return new ASTConcr<UnnDecl>(new UnnDecl(span, name, body.Node));
        }

        public AST<Domain> MkDomain(string name, ComposeKind kind, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<Domain>(new Domain(span, name, kind));
        }

        public AST<Param> MkParam(string name, AST<Node> type, Span span = default(Span))
        {
            Contract.Requires(type != null && type.Node.IsParamType);
            Contract.Requires((name == null && type.Node.NodeKind == NodeKind.ModRef) ||
                              (name != null && type.Node.NodeKind != API.NodeKind.ModRef));
            Contract.Requires(type.Node.NodeKind != API.NodeKind.ModRef || ((ModRef)type.Node).Rename != null);

            return new ASTConcr<Param>(new Param(span, name, type.Node));
        }

        public AST<Step> MkStep(AST<ModApply> rhs, Span span = default(Span))
        {
            Contract.Requires(rhs != null);
            return new ASTConcr<Step>(new Step(span, rhs.Node));
        }

        public AST<Transform> MkTransform(string name, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<Transform>(new Transform(span, name));
        }

        public AST<TSystem> MkTSystem(string name, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<TSystem>(new TSystem(span, name));
        }

        public AST<Model> MkModel(string name, bool isPartial, AST<ModRef> domain, ComposeKind kind, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Contract.Requires(domain != null);
            return new ASTConcr<Model>(new Model(span, name, isPartial, domain.Node, kind));
        }

        public AST<Machine> MkMachine(string name, Span span = default(Span))
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            return new ASTConcr<Machine>(new Machine(span, name));
        }

        public AST<ContractItem> MkContract(ContractKind kind, Span span = default(Span))
        {
            Contract.Requires(kind != ContractKind.RequiresSome && 
                              kind != ContractKind.RequiresAtLeast && 
                              kind != ContractKind.RequiresAtMost);
            return new ASTConcr<ContractItem>(new ContractItem(span, kind));
        }

        public AST<ContractItem> MkContract(ContractKind kind, AST<Id> typeId, int cardinality, Span span = default(Span))
        {
            Contract.Requires(kind == ContractKind.RequiresSome || kind == ContractKind.RequiresAtLeast || kind == ContractKind.RequiresAtMost);
            Contract.Requires(typeId != null);
            Contract.Requires(cardinality >= 0);
            var ci = new ContractItem(span, kind);
            ci.AddSpecification(new CardPair(span, typeId.Node, cardinality));
            return new ASTConcr<ContractItem>(ci);
        }

        public AST<Quote> MkQuote(Span span = default(Span))
        {
            return new ASTConcr<Quote>(new Quote(span));
        }

        public AST<QuoteRun> MkQuoteRun(string text, Span span = default(Span))
        {
            Contract.Requires(text != null);
            return new ASTConcr<QuoteRun>(new QuoteRun(span, text));
        }

        public AST<Update> MkUpdate(Span span = default(Span))
        {
            return new ASTConcr<Update>(new Update(span));
        }

        public AST<Program> MkProgram(ProgramName name)
        {
            Contract.Requires(name != null);
            return new ASTConcr<Program>(new Program(name));
        }
        
        internal AST<Folder> MkFolder(string name)
        {
            Contract.Requires(name != null);
            return new ASTConcr<Folder>(new Folder(name));
        }

        /// <summary>
        /// Constructs an AST from n and path by following the absolute positions in path.
        /// Ignores all the other information in path. If the path leads outside of the AST, 
        /// then null is returned. The path should start with the dummy absolute position -1.
        /// </summary>
        public AST<Node> FromAbsPositions(Node n, IEnumerable<ChildInfo> path)
        {
            Contract.Requires(n != null && path != null);
            Contract.Requires(path.First<ChildInfo>().AbsolutePos == -1);

            var newPath = new LinkedList<ChildInfo>();
            newPath.AddLast(new ChildInfo(n, ChildContextKind.AnyChildContext, -1, -1));
            bool found;
            ChildInfo cA, cB;
            using (var it = path.GetEnumerator())
            {
                it.MoveNext();
                while (it.MoveNext())
                {
                    cA = it.Current;
                    cB = newPath.Last.Value;
                    found = false;
                    foreach (var ci in cB.Node.ChildrenInfo)
                    {
                        if (ci.AbsolutePos == cA.AbsolutePos)
                        {
                            newPath.AddLast(ci);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        return null;
                    }
                }
            }  

            Action<ChildInfo, bool> extender;
            var newAST = Factory.Instance.MkEmptyAST(newPath.Last.Value.Node.NodeKind, out extender);
            var crnt = newPath.First;
            while (crnt != null)
            {
                extender(crnt.Value, crnt.Next == null);
                crnt = crnt.Next;
            }

            newAST.GetHashCode();
            return newAST;
        }

        public AST<Node> ToAST(Common.Terms.Term t)
        {
            Contract.Requires(t != null && t.Groundness != Common.Terms.Groundness.Type);
            var bldr = new Builder();
            var nsStack = new Stack<Common.Terms.Namespace>();
            Common.Terms.Namespace ns;
            t.Compute<Unit>(
                (x, s) => 
                {
                    if (x.Symbol.Kind == Common.Terms.SymbolKind.BaseCnstSymb)
                    {
                        nsStack.Push(null);
                    }
                    else
                    {
                        nsStack.Push(((Common.Terms.UserSymbol)x.Symbol).Namespace);
                    }

                    return x.Args;
                },
                (x, ch, s) =>
                {
                    ns = nsStack.Pop();
                    switch (x.Symbol.Kind)
                    {
                        case Common.Terms.SymbolKind.BaseCnstSymb:
                            {
                                var bc = (Common.Terms.BaseCnstSymb)x.Symbol;
                                switch (bc.CnstKind)
                                {
                                    case CnstKind.Numeric:
                                        bldr.PushCnst((Rational)bc.Raw);
                                        break;
                                    case CnstKind.String:
                                        bldr.PushCnst((string)bc.Raw);
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }                                
                            }

                            break;
                        case Common.Terms.SymbolKind.UserCnstSymb:
                            {
                                bldr.PushId(
                                    MkIdName(
                                        (Common.Terms.UserSymbol)x.Symbol,
                                        nsStack.Count == 0 ? null : nsStack.Peek()));
                            }

                            break;
                        case Common.Terms.SymbolKind.ConSymb:
                        case Common.Terms.SymbolKind.MapSymb:
                            {
                                bldr.PushId(
                                    MkIdName(
                                        (Common.Terms.UserSymbol)x.Symbol,
                                        nsStack.Count == 0 ? null : nsStack.Peek()));
                                bldr.PushFuncTerm();

                                for (int i = 0; i < x.Symbol.Arity; ++i)
                                {
                                    bldr.AddFuncTermArg();
                                }
                            }

                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    return default(Unit);
                });

            var n = bldr.Close();
            Contract.Assert(n == 1);
            ImmutableArray<AST<Node>> asts;
            bldr.GetASTs(out asts);
            Contract.Assert(asts != null && asts.Length == 1);
            return asts[0];
        }

        public AST<Node> ToAST(Node n)
        {
            Contract.Requires(n != null);
            switch (n.NodeKind)
            {
                case NodeKind.Cnst:
                    return new ASTConcr<Cnst>((Cnst)n);
                case NodeKind.Id:
                    return new ASTConcr<Id>((Id)n);
                case NodeKind.Range:
                    return new ASTConcr<Range>((Range)n);
                case NodeKind.QuoteRun:
                    return new ASTConcr<QuoteRun>((QuoteRun)n);
                case NodeKind.CardPair:
                    return new ASTConcr<CardPair>((CardPair)n);
                case NodeKind.Quote:
                    return new ASTConcr<Quote>((Quote)n);
                case NodeKind.FuncTerm:
                    return new ASTConcr<FuncTerm>((FuncTerm)n);
                case NodeKind.Compr:
                    return new ASTConcr<Compr>((Compr)n);
                case NodeKind.Find:
                    return new ASTConcr<Find>((Find)n);
                case NodeKind.ModelFact:
                    return new ASTConcr<ModelFact>((ModelFact)n);
                case NodeKind.RelConstr:
                    return new ASTConcr<RelConstr>((RelConstr)n);
                case NodeKind.Body:
                    return new ASTConcr<Body>((Body)n);
                case NodeKind.Rule:
                    return new ASTConcr<Rule>((Rule)n);
                case NodeKind.ContractItem:
                    return new ASTConcr<ContractItem>((ContractItem)n);
                case NodeKind.Config:
                    return new ASTConcr<Config>((Config)n);
                case NodeKind.Setting:
                    return new ASTConcr<Setting>((Setting)n);
                case NodeKind.Field:
                    return new ASTConcr<Field>((Field)n);
                case NodeKind.Enum:
                    return new ASTConcr<Nodes.Enum>((Nodes.Enum)n);
                case NodeKind.Union:
                    return new ASTConcr<Union>((Union)n);
                case NodeKind.ConDecl:
                    return new ASTConcr<ConDecl>((ConDecl)n);
                case NodeKind.MapDecl:
                    return new ASTConcr<MapDecl>((MapDecl)n);
                case NodeKind.UnnDecl:
                    return new ASTConcr<UnnDecl>((UnnDecl)n);
                case NodeKind.Step:
                    return new ASTConcr<Step>((Step)n);
                case NodeKind.ModRef:
                    return new ASTConcr<ModRef>((ModRef)n);
                case NodeKind.ModApply:
                    return new ASTConcr<ModApply>((ModApply)n);
                case NodeKind.Param:
                    return new ASTConcr<Param>((Param)n);
                case NodeKind.Domain:
                    return new ASTConcr<Domain>((Domain)n);
                case NodeKind.Transform:
                    return new ASTConcr<Transform>((Transform)n);
                case NodeKind.TSystem:
                    return new ASTConcr<TSystem>((TSystem)n);
                case NodeKind.Model:
                    return new ASTConcr<Model>((Model)n);
                case NodeKind.Machine:
                    return new ASTConcr<Machine>((Machine)n);
                case NodeKind.Property:
                    return new ASTConcr<Property>((Property)n);
                case NodeKind.Update:
                    return new ASTConcr<Update>((Update)n);
                case NodeKind.Program:
                    return new ASTConcr<Program>((Program)n);
                case NodeKind.Folder:
                    return new ASTConcr<Folder>((Folder)n);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Creates an empty AST object of the proper concrete type and returns the path extender function.
        /// </summary>
        internal AST<Node> MkEmptyAST(NodeKind kind, out Action<ChildInfo, bool> extender)
        {
            Contract.Requires(kind != NodeKind.AnyNodeKind);
            switch (kind)
            {
                case NodeKind.Cnst:
                    {
                        var ast = new ASTConcr<Cnst>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Id:
                    {
                        var ast = new ASTConcr<Id>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Range:
                    {
                        var ast = new ASTConcr<Range>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.QuoteRun:
                    {
                        var ast = new ASTConcr<QuoteRun>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.CardPair:
                    {
                        var ast = new ASTConcr<CardPair>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Quote:
                    {
                        var ast = new ASTConcr<Quote>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.FuncTerm:
                    {
                        var ast = new ASTConcr<FuncTerm>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Compr:
                    {
                        var ast = new ASTConcr<Compr>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Find:
                    {
                        var ast = new ASTConcr<Find>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.ModelFact:
                    {
                        var ast = new ASTConcr<ModelFact>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.RelConstr:
                    {
                        var ast = new ASTConcr<RelConstr>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Body:
                    {
                        var ast = new ASTConcr<Body>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Rule:
                    {
                        var ast = new ASTConcr<Rule>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.ContractItem:
                    {
                        var ast = new ASTConcr<ContractItem>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Config:
                    {
                        var ast = new ASTConcr<Config>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Setting:
                    {
                        var ast = new ASTConcr<Setting>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Field:
                    {
                        var ast = new ASTConcr<Field>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Enum:
                    {
                        var ast = new ASTConcr<Nodes.Enum>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Union:
                    {
                        var ast = new ASTConcr<Union>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.ConDecl:
                    {
                        var ast = new ASTConcr<ConDecl>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.MapDecl:
                    {
                        var ast = new ASTConcr<MapDecl>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.UnnDecl:
                    {
                        var ast = new ASTConcr<UnnDecl>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Step:
                    {
                        var ast = new ASTConcr<Step>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.ModRef:
                    {
                        var ast = new ASTConcr<ModRef>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.ModApply:
                    {
                        var ast = new ASTConcr<ModApply>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Param:
                    {
                        var ast = new ASTConcr<Param>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Domain:
                    {
                        var ast = new ASTConcr<Domain>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Transform:
                    {
                        var ast = new ASTConcr<Transform>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.TSystem:
                    {
                        var ast = new ASTConcr<TSystem>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Model:
                    {
                        var ast = new ASTConcr<Model>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Machine:
                    {
                        var ast = new ASTConcr<Machine>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Property:
                    {
                        var ast = new ASTConcr<Property>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Update:
                    {
                        var ast = new ASTConcr<Update>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Program:
                    {
                        var ast = new ASTConcr<Program>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                case NodeKind.Folder:
                    {
                        var ast = new ASTConcr<Folder>();
                        extender = ast.ExtendPath;
                        return ast;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        /***********************************************************/
        /****************       Adds             *******************/
        /***********************************************************/
        public AST<FuncTerm> AddArg(AST<FuncTerm> dataTerm, AST<Node> arg, bool addLast = true)
        {
            Contract.Requires(dataTerm != null);
            Contract.Requires(arg != null && arg.Node.IsFuncOrAtom);
            var clone = ((ASTConcr<FuncTerm>)dataTerm).ShallowClone();
            clone.Node.AddArg(arg.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<ModApply> AddArg(AST<ModApply> modApply, AST<Node> arg, bool addLast = true)
        {
            Contract.Requires(modApply != null);
            Contract.Requires(arg != null && arg.Node.IsModAppArg);
            var clone = ((ASTConcr<ModApply>)modApply).ShallowClone();
            clone.Node.AddArg(arg.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Body> AddConjunct(AST<Body> body, AST<Node> constraint, bool addLast = true)
        {
            Contract.Requires(body != null);
            Contract.Requires(constraint != null && constraint.Node.IsConstraint);
            var clone = ((ASTConcr<Body>)body).ShallowClone();
            clone.Node.AddConstr(constraint.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Rule> AddBody(AST<Rule> rule, AST<Body> body, bool addLast = true)
        {
            Contract.Requires(rule != null && body != null);
            var clone = ((ASTConcr<Rule>)rule).ShallowClone();
            clone.Node.AddBody(body.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Compr> AddBody(AST<Compr> compr, AST<Body> body, bool addLast = true)
        {
            Contract.Requires(compr != null && body != null);
            var clone = ((ASTConcr<Compr>)compr).ShallowClone();
            clone.Node.AddBody(body.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Rule> AddHead(AST<Rule> rule, AST<Node> term, bool addLast = true)
        {
            Contract.Requires(rule != null);
            Contract.Requires(term != null && term.Node.IsFuncOrAtom);
            var clone = ((ASTConcr<Rule>)rule).ShallowClone();
            clone.Node.AddHead(term.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Node> SetConfig(AST<Node> node, AST<Config> config)
        {
            Contract.Requires(node != null && config != null);
            Contract.Requires(node.Node.IsConfigSettable);
            switch (node.Node.NodeKind)
            {
                case NodeKind.Rule:
                    {
                        var clone = ((ASTConcr<Rule>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.Step:
                    {
                        var clone = ((ASTConcr<Step>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.Update:
                    {
                        var clone = ((ASTConcr<Update>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.Property:
                    {
                        var clone = ((ASTConcr<Property>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.ContractItem:
                    {
                        var clone = ((ASTConcr<ContractItem>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.ModelFact:
                    {
                        var clone = ((ASTConcr<ModelFact>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.UnnDecl:
                    {
                        var clone = ((ASTConcr<UnnDecl>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.ConDecl:
                    {
                        var clone = ((ASTConcr<ConDecl>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                case NodeKind.MapDecl:
                    {
                        var clone = ((ASTConcr<MapDecl>)node).ShallowClone();
                        clone.Node.SetConfig(config.Node);
                        clone.GetHashCode();
                        return clone;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public AST<Compr> AddHead(AST<Compr> compr, AST<Node> term, bool addLast = true)
        {
            Contract.Requires(compr != null);
            Contract.Requires(term != null && term.Node.IsFuncOrAtom);
            var clone = ((ASTConcr<Compr>)compr).ShallowClone();
            clone.Node.AddHead(term.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Domain> AddRule(AST<Domain> dom, AST<Rule> rule, bool addLast = true)
        {
            Contract.Requires(dom != null);
            Contract.Requires(rule != null);
            var clone = ((ASTConcr<Domain>)dom).ShallowClone();
            clone.Node.AddRule(rule.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Transform> AddRule(AST<Transform> trans, AST<Rule> rule, bool addLast = true)
        {
            Contract.Requires(trans != null);
            Contract.Requires(rule != null);
            var clone = ((ASTConcr<Transform>)trans).ShallowClone();
            clone.Node.AddRule(rule.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Domain> AddTypeDecl(AST<Domain> dom, AST<Node> typeDecl, bool addLast = true)
        {
            Contract.Requires(dom != null);
            Contract.Requires(typeDecl != null && typeDecl.Node.IsTypeDecl);
            var clone = ((ASTConcr<Domain>)dom).ShallowClone();
            clone.Node.AddTypeDecl(typeDecl.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Transform> AddTypeDecl(AST<Transform> trans, AST<Node> typeDecl, bool addLast = true)
        {
            Contract.Requires(trans != null);
            Contract.Requires(typeDecl != null && typeDecl.Node.IsTypeDecl);
            var clone = ((ASTConcr<Transform>)trans).ShallowClone();
            clone.Node.AddTypeDecl(typeDecl.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Node> AddContract(AST<Node> module, AST<ContractItem> item, bool addLast = true)
        {
            Contract.Requires(module != null);
            Contract.Requires(item != null);
            Contract.Requires(module.Node.CanHaveContract(item.Node.ContractKind));

            switch (module.Node.NodeKind)
            {
                case NodeKind.Domain:
                    var cloneD = ((ASTConcr<Domain>)module).ShallowClone();
                    cloneD.Node.AddConforms(item.Node, addLast);
                    cloneD.GetHashCode();
                    return cloneD;
                case NodeKind.Transform:
                    var cloneT = ((ASTConcr<Transform>)module).ShallowClone();
                    cloneT.Node.AddContract(item.Node, addLast);
                    cloneT.GetHashCode();
                    return cloneT;
                case NodeKind.Model:
                    var cloneM = ((ASTConcr<Model>)module).ShallowClone();
                    cloneM.Node.AddContract(item.Node, addLast);
                    cloneM.GetHashCode();
                    return cloneM;
                default:
                    throw new NotImplementedException();
            }
        }

        public AST<ConDecl> AddField(AST<ConDecl> decl, AST<Field> field, bool addLast = true)
        {
            Contract.Requires(decl != null && field != null);
            var clone = ((ASTConcr<ConDecl>)decl).ShallowClone();
            clone.Node.AddField(field.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Nodes.Enum> AddElement(AST<Nodes.Enum> enm, AST<Node> element, bool addLast = true)
        {
            Contract.Requires(enm != null && element != null);
            Contract.Requires(element.Node.IsEnumElement);
            var clone = ((ASTConcr<Nodes.Enum>)enm).ShallowClone();
            clone.Node.AddElement(element.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<MapDecl> AddMapDom(AST<MapDecl> decl, AST<Field> field, bool addLast = true)
        {
            Contract.Requires(decl != null && field != null);
            var clone = ((ASTConcr<MapDecl>)decl).ShallowClone();
            clone.Node.AddDomField(field.Node, addLast);
            clone.GetHashCode();
            return clone;
        }


        public AST<MapDecl> AddMapCod(AST<MapDecl> decl, AST<Field> field, bool addLast = true)
        {
            Contract.Requires(decl != null && field != null);
            var clone = ((ASTConcr<MapDecl>)decl).ShallowClone();
            clone.Node.AddCodField(field.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Union> AddUnnCmp(AST<Union> unn, AST<Node> cmp, bool addLast = true)
        {
            Contract.Requires(unn != null);
            Contract.Requires(cmp != null && cmp.Node.IsUnionComponent);
            var clone = ((ASTConcr<Union>)unn).ShallowClone();
            clone.Node.AddComponent(cmp.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Step> AddLhs(AST<Step> step, AST<Id> lhs, bool addLast = true)
        {
            Contract.Requires(step != null && lhs != null);
            var clone = ((ASTConcr<Step>)step).ShallowClone();
            clone.Node.AddLhs(lhs.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Update> AddState(AST<Update> update, AST<Id> lhs, bool addLast = true)
        {
            Contract.Requires(update != null && lhs != null);
            var clone = ((ASTConcr<Update>)update).ShallowClone();
            clone.Node.AddState(lhs.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<TSystem> AddStep(AST<TSystem> t, AST<Step> step, bool addLast = true)
        {
            Contract.Requires(t != null && step != null);
            var clone = ((ASTConcr<TSystem>)t).ShallowClone();
            clone.Node.AddStep(step.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Domain> AddDomainCompose(AST<Domain> domain, AST<ModRef> modRef, bool addLast = true)
        {
            Contract.Requires(domain != null);
            Contract.Requires(modRef != null);
            var clone = ((ASTConcr<Domain>)domain).ShallowClone();
            clone.Node.AddCompose(modRef.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Transform> AddTransInput(AST<Transform> t, AST<Param> p, bool addLast = true)
        {
            Contract.Requires(t != null && p != null);
            var clone = ((ASTConcr<Transform>)t).ShallowClone();
            clone.Node.AddInput(p.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<TSystem> AddTransInput(AST<TSystem> t, AST<Param> p, bool addLast = true)
        {
            Contract.Requires(t != null && p != null);
            var clone = ((ASTConcr<TSystem>)t).ShallowClone();
            clone.Node.AddInput(p.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Transform> AddTransOutput(AST<Transform> t, AST<Param> p, bool addLast = true)
        {
            Contract.Requires(t != null && p != null);
            var clone = ((ASTConcr<Transform>)t).ShallowClone();
            clone.Node.AddOutput(p.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<TSystem> AddTransOutput(AST<TSystem> t, AST<Param> p, bool addLast = true)
        {
            Contract.Requires(t != null && p != null);
            var clone = ((ASTConcr<TSystem>)t).ShallowClone();
            clone.Node.AddOutput(p.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<ContractItem> AddContractSpec(AST<ContractItem> c, AST<Body> n, bool addLast = true)
        {
            Contract.Requires(c != null);
            Contract.Requires(n != null);
            Contract.Requires(c.Node.ContractKind != ContractKind.RequiresSome &&
                              c.Node.ContractKind != ContractKind.RequiresAtLeast &&
                              c.Node.ContractKind != ContractKind.RequiresAtMost);

            var clone = ((ASTConcr<ContractItem>)c).ShallowClone();
            clone.Node.AddSpecification(n.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Model> AddModelCompose(AST<Model> m, AST<ModRef> modRef, bool addLast = true)
        {
            Contract.Requires(m != null);
            Contract.Requires(modRef != null);
            var clone = ((ASTConcr<Model>)m).ShallowClone();
            clone.Node.AddCompose(modRef.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Model> AddFact(AST<Model> m, AST<ModelFact> f, bool addLast = true)
        {
            Contract.Requires(m != null);
            Contract.Requires(f != null);
            var clone = ((ASTConcr<Model>)m).ShallowClone();
            clone.Node.AddFact(f.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Quote> AddQuoteItem(AST<Quote> quote, AST<Node> item, bool addLast = true)
        {
            Contract.Requires(quote != null);
            Contract.Requires(item != null && item.Node.IsQuoteItem);
            var clone = ((ASTConcr<Quote>)quote).ShallowClone();
            clone.Node.AddItem(item.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Machine> AddMachInput(AST<Machine> mach, AST<Param> param, bool addLast = true)
        {
            Contract.Requires(mach != null);
            Contract.Requires(param != null);
            var clone = ((ASTConcr<Machine>)mach).ShallowClone();
            clone.Node.AddInput(param.Node, addLast);
            clone.GetHashCode();
            return clone;               
        }

        public AST<Machine> AddMachStateDom(AST<Machine> mach, AST<ModRef> domain, bool addLast = true)
        {
            Contract.Requires(mach != null);
            Contract.Requires(domain != null);
            var clone = ((ASTConcr<Machine>)mach).ShallowClone();
            clone.Node.AddStateDomain(domain.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Machine> AddBootStep(AST<Machine> mach, AST<Step> step, bool addLast = true)
        {
            Contract.Requires(mach != null);
            Contract.Requires(step != null);
            var clone = ((ASTConcr<Machine>)mach).ShallowClone();
            clone.Node.AddBootStep(step.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Machine> AddMachUpdate(AST<Machine> mach, AST<Update> update, bool isInitUpdate, bool addLast = true)
        {
            Contract.Requires(mach != null);
            Contract.Requires(update != null);
            var clone = ((ASTConcr<Machine>)mach).ShallowClone();
            clone.Node.AddUpdate(update.Node, isInitUpdate, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Machine> AddProperty(AST<Machine> mach, AST<Property> prop, bool addLast = true)
        {
            Contract.Requires(mach != null);
            Contract.Requires(prop != null);
            var clone = ((ASTConcr<Machine>)mach).ShallowClone();
            clone.Node.AddProperty(prop.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Update> AddChoice(AST<Update> update, AST<ModApply> choice, bool addLast = true)
        {
            Contract.Requires(update != null);
            Contract.Requires(choice != null);
            var clone = ((ASTConcr<Update>)update).ShallowClone();
            clone.Node.AddChoice(choice.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Program> AddModule(AST<Program> program, AST<Node> module, bool addLast = true)
        {
            Contract.Requires(program != null);
            Contract.Requires(module != null && module.Node.IsModule);
            var clone = ((ASTConcr<Program>)program).ShallowClone();
            clone.Node.AddModule(module.Node, addLast);
            clone.GetHashCode();
            return clone;
        }

        public AST<Config> AddSetting(AST<Config> config, AST<Id> key, AST<Cnst> value, bool addLast = true)
        {
            Contract.Requires(config != null);
            Contract.Requires(key != null);
            Contract.Requires(value != null);
            var clone = ((ASTConcr<Config>)config).ShallowClone();
            clone.Node.AddSetting(new Setting(key.Node.Span, key.Node, value.Node), addLast);
            clone.GetHashCode();
            return clone;
        }
        
        internal AST<Folder> AddSubFolder(AST<Folder> folder, AST<Folder> subFolder)
        {
            Contract.Requires(folder != null && subFolder != null);
            var clone = ((ASTConcr<Folder>)folder).ShallowClone();
            clone.Node.AddSubFolder(subFolder.Node);
            clone.GetHashCode();
            return clone;
        }

        internal AST<Folder> AddProgram(AST<Folder> folder, AST<Program> program)
        {
            Contract.Requires(folder != null && program != null);
            var clone = ((ASTConcr<Folder>)folder).ShallowClone();
            clone.Node.AddProgram(program.Node);
            clone.GetHashCode();
            return clone;
        }

        /***********************************************************/
        /****************       Parsing          *******************/
        /***********************************************************/
        /// <summary>
        /// Attempts to load and parse the file in name.
        /// </summary>
        public Task<ParseResult> ParseFile(ProgramName name, CancellationToken cancelToken = default(CancellationToken), EnvParams envParams = null)
        {
            Contract.Requires(name != null && name.Uri.IsFile);
            return Task.Factory.StartNew<ParseResult>(() =>
            {
                ParseResult pr;
                var parser = new Parser(envParams);
                parser.ParseFile(name, null, default(Span), cancelToken, out pr);
                return pr;
            });
        }

        /// <summary>
        /// Attempts to parse the program in programText, and returns a program named name.
        /// </summary>
        public Task<ParseResult> ParseText(ProgramName name, string programText, CancellationToken cancelToken = default(CancellationToken), EnvParams envParams = null)
        {
            Contract.Requires(name != null);
            Contract.Requires(programText != null);
            return Task.Factory.StartNew<ParseResult>(() =>
            {
                ParseResult pr;
                var parser = new Parser(envParams);
                parser.ParseText(name, programText, default(Span), cancelToken, out pr);
                return pr;
            });
        }

        /// <summary>
        /// Tries to parse the file in name. Referrer (can be null) is the description of the entity
        /// which requested this parse. This is used only to provide better error messages
        /// </summary>
        internal Task<ParseResult> ParseFile(ProgramName name, string referrer, Span location, CancellationToken cancelToken = default(CancellationToken), EnvParams envParams = null)
        {
            Contract.Requires(name != null && name.Uri.IsFile);
            return Task.Factory.StartNew<ParseResult>(() =>
            {
                ParseResult pr;
                var parser = new Parser(envParams);
                parser.ParseFile(name, referrer, location, cancelToken, out pr);
                return pr;
            });
        }

        public AST<Node> ParseDataTerm(string text, out ImmutableCollection<Flag> flags, EnvParams envParams = null)
        {
            Contract.Requires(text != null);
            ParseResult pr;
            var parser = new Parser(envParams);
            var result = parser.ParseFuncTerm(text, out pr);
            pr.Program.Root.GetNodeHash();
            flags = pr.Flags;
            return result;
        }

        /// <summary>
        /// A reference string can have the form [prefix::] name [at location]
        /// where [X] indicates an optional component.
        /// </summary>
        public bool TryParseReference(string refString, out AST<ModRef> modRef, Span span = default(Span))
        {
            Contract.Requires(refString != null);
            refString = refString.Trim();
            modRef = null;
            string rename = null, name = null, location = null;
            
            //// First check for a renaming prefix
            var renameIndex = refString.IndexOf("::");
            if (renameIndex == 0)
            {
                return false;
            }
            else if (renameIndex > 0)
            {
                if (renameIndex + 2 == refString.Length)
                {
                    return false;
                }

                rename = refString.Substring(0, renameIndex).Trim();
                refString = refString.Substring(renameIndex + 2).Trim();
                if (refString.Length == 0 || rename.Length == 0 || rename.IndexOfAny(Whitespaces) > 0)
                {
                    return false;
                }
            }

            var atIndex = refString.IndexOf(" at ");
            if (atIndex == 0)
            {
                return false;
            }
            else if (atIndex + 4 == refString.Length)
            {
                return false;
            }
            else if (atIndex > 0)
            {
                name = refString.Substring(0, atIndex).Trim();
                location = refString.Substring(atIndex + 4).Trim();
                if (location.Length == 0)
                {
                    return false;
                }
            }
            else
            {
                name = refString;
            }

            if (name.Length == 0 || name == "at" || name.IndexOfAny(Whitespaces) >= 0)
            {
                return false;
            }

            modRef = new ASTConcr<ModRef>(new ModRef(span, name, rename, location));
            return true;
        }

        public Task<ReloadResult> Reload(AST<Folder> folder, CancellationToken cancelToken = default(CancellationToken))
        {
            Contract.Requires(folder != null);
            throw new NotImplementedException();
        }

        /***********************************************************/
        /****************       Comparison       *******************/
        /***********************************************************/
        /// <summary>
        /// Returns true if the ASTs rooted by rootA and rootB are structurally
        /// identical.
        /// </summary>
        public bool IsEqualRoots(Node rootA, Node rootB, CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(rootA != null && rootB != null);
            if (rootA == rootB)
            {
                return true;
            }

            var cntrl = cancel == default(CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, Node.CancelCheckFreq);            
            return new ASTComputation2<bool>(
                     rootA,
                     rootB,
                     (a, b) =>
                     {
                         if (a == b || a.GetNodeHash() != b.GetNodeHash() || !a.IsLocallyEquivalent(b))
                         {
                             return null;
                         }

                         return new Tuple<IEnumerable<Node>, IEnumerable<Node>>(a.Children, b.Children);
                     },
                     (a, b, childEqs) =>
                     {
                         if (a == b)
                         {
                             return true;
                         }
                         else if (a.GetNodeHash() != b.GetNodeHash() || !a.IsLocallyEquivalent(b))
                         {
                             return false;
                         }

                         foreach (var eq in childEqs)
                         {
                             if (!eq)
                             {
                                 return false;
                             }
                         }

                         return true;
                     },
                     cntrl).Compute();
        }

        /// <summary>
        /// Returns the full namespace of the fully qualified symbol, and the residual symbol name given that the symbol
        /// is used under an application of namespace.F()
        /// </summary>
        /// <param name="namespace"></param>
        /// <param name="fullSymbolName"></param>
        /// <param name="residualSymbol"></param>
        /// <param name="fullSymbolSpace"></param>
        private void GetResidualSymbol(
            string @namespace, 
            string fullSymbolName, 
            out string residualSymbol,
            out string fullSymbolSpace)
        {
            var lastSplit = fullSymbolName.LastIndexOf('.');
            if (lastSplit < 0)
            {
                residualSymbol = fullSymbolName;
                fullSymbolSpace = string.Empty;
                return;
            }
            else if (string.IsNullOrEmpty(@namespace))
            {
                residualSymbol = fullSymbolName;
                fullSymbolSpace = fullSymbolName.Substring(0, lastSplit);
                return;
            }

            fullSymbolSpace = fullSymbolName.Substring(0, lastSplit);
            var dottedNamespace = fullSymbolSpace + ".";
            //// Keep the dot.
            if (dottedNamespace.StartsWith(@namespace + "."))
            {
                if (dottedNamespace.Length == @namespace.Length + 1)
                {
                    residualSymbol = fullSymbolName.Substring(lastSplit + 1);
                }
                else
                {
                    residualSymbol = fullSymbolName.Substring(@namespace.Length + 1);
                }
            }
            else
            {
                residualSymbol = fullSymbolName;
            }                                                
        }

        /// <summary>
        /// Makes a string identifier taking into account the namespace of outer function symbol.
        /// Parent can be null if us does not appear under a function application.
        /// </summary>
        private string MkIdName(Common.Terms.UserSymbol us, Common.Terms.Namespace parent)
        {
            if (parent == null)
            {
                return us.FullName;
            }

            var suffix = us.Namespace.Split(parent);
            if (suffix == null)
            {
                return us.FullName;
            }

            var name = string.Empty;
            for (int i = 0; i < suffix.Length; ++i)
            {
                name += string.Format("{0}.", suffix[i]);
            }

            return name + us.Name;
        }

        /// <summary>
        /// Converts a C# embedding of a Formula term into a model fact.
        /// </summary>
        private void MkModelFact(
            Builder bldr,
            Generators.ICSharpTerm groundTerm, 
            Dictionary<Generators.ICSharpTerm, string> aliases)
        {
            var stack = new Stack<MutableTuple<Generators.ICSharpTerm, int, string>>();
            var opened = new HashSet<Generators.ICSharpTerm>();

            stack.Push(new MutableTuple<Generators.ICSharpTerm, int, string>(groundTerm, -1, string.Empty));

            string s, ns, rs;
            string alias = null;
            Generators.ICSharpTerm ct, nt;
            MutableTuple<Generators.ICSharpTerm, int, string> top;

            bool isAliased = groundTerm.Arity > 0 && aliases != null && aliases.TryGetValue(groundTerm, out alias);
            if (isAliased)
            {
                bldr.PushId(alias, groundTerm.Span);
            }
            
            while (stack.Count > 0)
            {
                top = stack.Peek();
                if (top.Item2 == -1)
                {
                    if (opened.Contains(top.Item1))
                    {
                        throw new Exception("Object graph is cyclic; this is not allowed");
                    }

                    opened.Add(top.Item1);
                }

                top.Item2++;
                ct = top.Item1;
                if (top.Item2 == ct.Arity)
                {
                    if (ct.Arity == 0)
                    {
                        if (ct.Symbol is string)
                        {
                            if (ct.GetType().Name == Generators.CSharpDataModelGen.QuotationClassName)
                            {
                                s = (string)ct.Symbol;
                                bldr.PushQuoteRun(s.Length == 2 ? string.Empty : s.Substring(1, s.Length - 2), ct.Span);
                                bldr.PushQuote(ct.Span);
                                bldr.AddQuoteItem();
                            }
                            else if (ct.GetType().Name == Generators.CSharpDataModelGen.UserCnstClassName)
                            {
                                GetResidualSymbol(top.Item3, (string)ct.Symbol, out rs, out ns);
                                bldr.PushId(rs, ct.Span);
                            }
                            else if (ct is Generators.CSharpAlias)
                            {
                                bldr.PushId((string)ct.Symbol, ct.Span);
                            }
                            else
                            {
                                bldr.PushCnst(ct.Symbol == null ? string.Empty : (string)ct.Symbol, ct.Span);
                            }
                        }
                        else if (ct.Symbol is Rational)
                        {
                            bldr.PushCnst((Rational)ct.Symbol, ct.Span);
                        }
                        else
                        {
                            throw new Exception(string.Format("Unexpected symbol {0}", ct.Symbol));
                        }
                    }
                    else
                    {
                        GetResidualSymbol(top.Item3, (string)ct.Symbol, out rs, out ns);
                        bldr.PushId(rs, ct.Span);
                        bldr.PushFuncTerm(ct.Span);
                        for (int i = 0; i < top.Item1.Arity; ++i)
                        {
                            bldr.AddFuncTermArg();
                        }
                    }

                    opened.Remove(top.Item1);
                    stack.Pop();
                }
                else
                {
                    nt = ct[top.Item2];
                    GetResidualSymbol(string.Empty, (string)ct.Symbol, out rs, out ns);
                    if (nt.Arity > 0 && aliases != null && aliases.TryGetValue(nt, out alias))
                    {
                        var ca = new Generators.CSharpAlias((string)alias);
                        ca.Span = nt.Span;
                        stack.Push(new MutableTuple<Generators.ICSharpTerm, int, string>(ca, -1, ns));
                    }
                    else
                    {
                        stack.Push(new MutableTuple<Generators.ICSharpTerm, int, string>(ct[top.Item2], -1, ns));
                    }
                }
            }

            if (isAliased)
            {
                bldr.PushModelFact(groundTerm.Span);
            }
            else
            {
                bldr.PushAnonModelFact(groundTerm.Span);
            }
        }
    }
}
