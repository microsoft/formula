namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.ASTQueries;
    using API.Plugins;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Common.Rules;

    /// <summary>
    /// An action set creates a set of actions from a rule
    /// or a comprehension. It can inherit a constraint system
    /// from a lexically higher scope. If rule / compr has the form:
    ///      h_1, ..., h_n OP B_1; ...; B_m
    /// Then the action set contains the actions:
    /// (h_1, B_1), ..., (h_1, B_m),
    ///             ...
    /// (h_n, B_1), ..., (h_n, B_m)
    /// </summary>
    internal class ActionSet
    {
        private static Id[] EmptyHeadVars = new Id[0];
        private static NodePred[] IdQueryRule = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Id) &
                NodePredFactory.Instance.MkPredicate(ChildContextKind.Args)
            };

        /// <summary>
        /// This is the deepest scope we will handle.
        /// </summary>
        private static int MaxDepth = 255;

        private LinkedList<Action> actions = new LinkedList<Action>();

        private ComprehensionData myComprData;

        public AST<Node> AST
        {
            get;
            private set;
        }

        public TermIndex Index
        {
            get;
            private set;
        }

        public TypeEnvironment TypeEnvironment
        {
            get;
            private set;
        }

        /// <summary>
        /// True if the action set has been successfully compiled.
        /// False if the action set failed compilation or validation.
        /// Unknown is the action set has not been compiled, but passed validation.
        /// </summary>
        public LiftedBool IsCompiled
        {
            get;
            private set;
        }

        public ActionSet(AST<Node> ast, TermIndex index)
        {
            Contract.Requires(index != null && ast != null);
            Contract.Requires(ast.Node.NodeKind == NodeKind.Rule || ast.Node.NodeKind == NodeKind.ContractItem);
            //// TODO: Accept contract specifications too.

            AST = ast;
            Index = index;
            myComprData = null;
            TypeEnvironment = new TypeEnvironment(ast.Node, index);
            IsCompiled = LiftedBool.Unknown;
        }

        public ActionSet(ComprehensionData comprData)
        {
            Contract.Requires(comprData != null);
            AST = Factory.Instance.ToAST(comprData.Node);
            Index = comprData.Owner.Index;
            myComprData = comprData;
            TypeEnvironment = comprData.Owner.TypeEnvironment.AddChild(comprData.Node);
            IsCompiled = LiftedBool.Unknown;
        }

        public bool Validate(List<Flag> flags, CancellationToken cancel, bool isCompilerAction = false)
        {
            if (myComprData != null && myComprData.Depth > MaxDepth)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    AST.Node,
                    Constants.BadSyntax.ToString(
                        string.Format("Comprehension nesting too deep. Maximum nesting depth is {0}.", MaxDepth)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return RecordValidationResult(false);
            }

            IEnumerable<Node> heads = null;
            IEnumerable<Body> bodies = null;
            switch (AST.Node.NodeKind)
            {
                case NodeKind.Compr:
                    heads = ((Compr)AST.Node).Heads;
                    bodies = ((Compr)AST.Node).Bodies;
                    break;
                case NodeKind.Rule:
                    heads = ((Rule)AST.Node).Heads;
                    bodies = ((Rule)AST.Node).Bodies;
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (heads.IsEmpty<Node>())
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    AST.Node,
                    Constants.BadSyntax.ToString("The expression has no heads."),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                return RecordValidationResult(false);
            }

            //// Step 1.a. If the body is empty, treat it like a TRUE body.
            var result = true;
            if (bodies.IsEmpty<Body>())
            {
                bodies = EnumerableMethods.GetEnumerable<Body>(MkTrueBody(heads.First<Node>().Span));
            }

            //// Step 1.b. Otherwise, find all the variables (possibly) with selectors
            //// occurring in all the heads. These will be registered with the bodies.
            var varList = new LinkedList<Id>();
            foreach (var h in heads)
            {
                FindVarLikeIds(h, varList);
            }

            //// Step 2. Expand heads / bodies.
            Term type;
            TypeEnvironment bodyEnv;
            RuleLinter.SymTable = TypeEnvironment.Index.SymbolTable;
            foreach (var b in bodies)
            {
                List<string> varNames = null;
                if(!RuleLinter.ValidateBodyQualifiedIds(b, out varNames))
                {
                    var flag = new Flag(
                    SeverityKind.Error,
                    AST.Node,
                    Constants.NoBindingTypeError.ToString(String.Join(",", varNames)),
                    Constants.NoBindingTypeError.Code);
                    flags.Add(flag);
                    return RecordValidationResult(false);
                }

                bodyEnv = TypeEnvironment.AddChild(b);
                var cs = new ConstraintSystem(Index, b, bodyEnv, myComprData);
                if (!cs.Validate(flags, varList, cancel))
                {
                    result = false;
                    continue;
                }

                foreach (var v in cs.Variables)
                {
                    if (cs.TryGetType(v, out type))
                    {
                        bodyEnv.SetType(v, type);
                    }
                }
                
                foreach (var h in heads)
                {
                    var act = new Action(h, cs, TypeEnvironment, myComprData, AST.Node);
                    if (act.Validate(flags, cancel, isCompilerAction))
                    {
                        actions.AddLast(act);
                    }
                    else
                    {
                        result = false;
                    }

                    if (cancel.IsCancellationRequested)
                    {
                        return RecordValidationResult(false);
                    }
                }
            }

            if (result && !cancel.IsCancellationRequested)
            {
                TypeEnvironment.JoinTypes();
                Contract.Assert(AST.Node.CompilerData == null);
                AST.Node.CompilerData = TypeEnvironment;
                return RecordValidationResult(true);
            }
            else
            {
                return RecordValidationResult(false);
            }
        }

        /// <summary>
        /// Compiles the action set into a set of rules. Should not be called if validation failed.
        /// </summary>
        public bool Compile(RuleTable rules, List<Flag> flags, CancellationToken cancel)
        {
            bool result = true;
            if (IsCompiled != LiftedBool.Unknown)
            {
                return (bool)IsCompiled;
            }
            else if (myComprData == null)
            {
                foreach (var a in actions)
                {
                    result = a.Compile(rules, flags, cancel) && result;
                }

                return (bool)(IsCompiled = result);
            }

            //// For a comprehension need to compile the bodies of the actions
            //// before compiling the actions, so a representation for the comprehension is known.
            FindData[] parts;
            var bodies = new LinkedList<Term>();
            foreach (var a in actions)
            {
                result = a.Body.Compile(rules, out parts, flags, cancel) && result;
                if (result)
                {
                    bodies.AddLast(rules.MkBodyTerm(parts));
                }
            }

            if (!result)
            {
                return (bool)(IsCompiled = result);
            }

            bool wasAdded;
            Term reads = Index.TrueValue;
            var comprSymbol = Index.SymbolTable.GetOpSymbol(ReservedOpKind.Compr);
            var conjSymbol = Index.SymbolTable.GetOpSymbol(ReservedOpKind.Conj);
            foreach (var kv in myComprData.ReadVars.Reverse)
            {
                reads = Index.MkApply(conjSymbol, new Term[] { kv.Key, reads }, out wasAdded);
            }

            var headSet = new Set<Term>(Term.Compare);
            foreach (var a in actions)
            {
                headSet.Add(a.HeadTerm);
            }

            Term heads = Index.TrueValue;
            foreach (var h in headSet.Reverse)
            {
                heads = Index.MkApply(conjSymbol, new Term[] { h, heads }, out wasAdded);
            }
            
            myComprData.Representation = Index.MkApply(comprSymbol, new Term[] { heads, reads, rules.MkBodiesTerm(bodies) }, out wasAdded);
            foreach (var a in actions)
            {
                result = a.Compile(rules, flags, cancel) && result;
            }

            return (bool)(IsCompiled = result);
        }

        public int GetNextVarId(FreshVarKind kind)
        {
            var id = 0;
            foreach (var a in actions)
            {
                id = Math.Max(id, a.GetNextVarId(kind));
            }

            return id;
        }

        private bool RecordValidationResult(bool result)
        {
            if (!result)
            {
                IsCompiled = LiftedBool.False;
            }

            return result;
        }

        /// <summary>
        /// Makes a body containing the tautology TRUE = TRUE.
        /// </summary>
        /// <returns></returns>
        private Body MkTrueBody(Span span = default(Span))
        {
            //// An action with an empty body is treated as the tautology
            //// TRUE = TRUE.
            var tEqt = Factory.Instance.MkRelConstr(
                        RelKind.Eq,
                        Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span),
                        Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span));
            var body = Factory.Instance.MkBody(span);
            return Factory.Instance.AddConjunct(body, tEqt).Node;
        }

        /// <summary>
        /// Returns ids in the head that are (1) variables with selectors, or (2) fully qualified symbolic constants.
        /// </summary>
        /// <returns></returns>
        private void FindVarLikeIds(Node head, LinkedList<Id> ids)
        {
            UserSymbol us, other;
            Stack<Namespace> nsStack = new Stack<Namespace>();
            nsStack.Push(null);
            int firstAcc;
            Factory.Instance.ToAST(head).Compute<Unit>(
                (n) =>
                {
                    var space = nsStack.Peek();
                    switch (n.NodeKind)
                    {
                        case NodeKind.Id:
                            {
                                var id = (Id)n;
                                if (Index.SymbolTable.HasRenamingPrefix(id))
                                {
                                    if ((us = Index.SymbolTable.Resolve(ASTSchema.Instance.StripAccessors(id, true, out firstAcc), out other, space, x => x.IsNonVarConstant)) == null || other != null)
                                    {
                                        nsStack.Push(null);
                                        return null;
                                    }
                                }
                                else if ((us = Index.SymbolTable.Resolve(ASTSchema.Instance.StripAccessors(id, false, out firstAcc), out other, space, x => x.Kind == SymbolKind.UserCnstSymb)) == null || other != null)
                                {
                                    nsStack.Push(null);
                                    return null;
                                }

                                if (us.IsVariable || ((UserCnstSymb)us).IsSymbolicConstant)
                                {
                                    ids.AddLast((Id)n);
                                }

                                nsStack.Push(us.Namespace);
                                return null;
                            }
                        case NodeKind.FuncTerm:
                            {
                                var ft = (FuncTerm)n;
                                if (ft.Function is Id)
                                {
                                    var ftid = (Id)ft.Function;
                                    if (ASTSchema.Instance.IsId(ftid.Name, true, false, false, true) &&
                                        ftid.Fragments[ftid.Fragments.Length - 1] == ASTSchema.Instance.DontCareName)
                                    {
                                        var nsName = ftid.Fragments.Length == 1
                                                     ? string.Empty
                                                     : ftid.Name.Substring(0, ftid.Name.Length - 2);
                                        Namespace ns, ons;
                                        ns = Index.SymbolTable.Resolve(nsName, out ons, space);
                                        if (ns == null || ons != null)
                                        {
                                            nsStack.Push(null);
                                            return null;
                                        }
                                        else
                                        {
                                            nsStack.Push(ns);
                                        }
                                    }
                                    else if ((us = Index.SymbolTable.Resolve(ftid.Name, out other, space, x => x.IsDataConstructor)) == null || other != null)
                                    {
                                        nsStack.Push(null);
                                        return null;
                                    }
                                    else
                                    {
                                        nsStack.Push(us.Namespace);
                                    }

                                    return ft.Args;
                                }
                                else
                                {
                                    nsStack.Push(null);
                                    return null;
                                }
                            }
                        default:
                            return null;
                    }
                },
                (n, ch) =>
                {
                    switch (n.NodeKind)
                    {
                        case NodeKind.Id:
                        case NodeKind.FuncTerm:
                            nsStack.Pop();
                            break;
                    }

                    return default(Unit);
                });
        }
    }
}
