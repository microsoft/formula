namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;

    using Nodes;
    using Common;

    public sealed class Builder
    {
        private bool isClosed = false;

        private SpinLock builderLock = new SpinLock();

        private Stack<Node> stack = new Stack<Node>();

        private Map<BuilderRef, Node> heap = new Map<BuilderRef, Node>(BuilderRef.Compare);

        private ImmutableArray<AST<Node>> finalNodes = null;

        private ulong nextRefId = 1;

        public bool IsClosed
        {
            get
            {
                bool gotLock = false;
                try
                {
                    builderLock.Enter(ref gotLock);
                    return isClosed;
                }
                finally
                {
                    if (gotLock)
                    {
                        builderLock.Exit();
                    }
                }
            }
        }

        /***********************************************************/
        /****************       Memory             *****************/
        /***********************************************************/
        /// <summary>
        /// Requires stack to be non-empty.
        /// Pops the top of the stack; it is lost unless already stored.
        /// </summary>
        public BuilderResultKind Pop()
        {
            return Modify(() =>
                    {
                        if (stack.Count == 0)
                        {
                            return BuilderResultKind.Fail_BadArgs;
                        }

                        stack.Pop();
                        return BuilderResultKind.Success;
                    });
        }

        /// <summary>
        /// Requires stack to be non-empty.
        /// Pops the element and stores is in a fresh memory location.
        /// </summary>
        public BuilderResultKind Store(out BuilderRef bref)
        {
            BuilderRef lclBref = default(BuilderRef);
            var result = 
                Modify(() =>
                {
                    if (stack.Count == 0)
                    {
                        return BuilderResultKind.Fail_BadArgs;
                    }

                    lclBref = new BuilderRef(nextRefId++);
                    heap[lclBref] = stack.Pop();
                    return BuilderResultKind.Success;
                });

            bref = lclBref;
            return result;
        }

        /// <summary>
        /// Requires an element to be stored at bref.
        /// Pushes that element on the stack.
        /// </summary>
        public BuilderResultKind Load(BuilderRef bref)
        {
            return Modify(() =>
                {
                    Node node;
                    if (!heap.TryFindValue(bref, out node))
                    {
                        return BuilderResultKind.Fail_BadArgs;
                    }

                    stack.Push(node);
                    return BuilderResultKind.Success;
                });
        }

        /// <summary>
        /// Clears the memory location if it is occupied by a node
        /// </summary>
        public BuilderResultKind Clear(BuilderRef bref)
        {
            return Modify(() =>
            {
                heap.Remove(bref);
                return BuilderResultKind.Success;
            });
        }

        /***********************************************************/
        /****************       Peeks               *****************/
        /***********************************************************/
        /// <summary>
        /// Returns the number of elements currently on the stack.
        /// </summary>
        public BuilderResultKind GetStackCount(out int count)
        {
            int lclCount = 0;
            var result = Modify(() =>
            {
                lclCount = stack.Count;
                return BuilderResultKind.Success;
            });

            count = lclCount;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Returns the NodeKind of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekNodeKind(out NodeKind kind)
        {
            NodeKind lclKind = NodeKind.AnyNodeKind;
            var result = Modify(() =>
            {
                if (stack.Count == 0)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                lclKind = stack.Peek().NodeKind;
                return BuilderResultKind.Success;
            });

            kind = lclKind;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Returns the ChildCount of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekChildCount(out int childCount)
        {
            int lclCount = 0;
            var result = Modify(() =>
            {
                if (stack.Count == 0)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                lclCount = stack.Peek().ChildCount;
                return BuilderResultKind.Success;
            });

            childCount = lclCount;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Tries to returns a string attribute of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekStringAttribute(AttributeKind attribute, out string value)
        {
            string lclString = null;
            var result = Modify(() =>
            {
                if (stack.Count == 0 ||
                    !stack.Peek().TryGetStringAttribute(attribute, out lclString))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                return BuilderResultKind.Success;
            });

            value = lclString;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Tries to returns a numeric attribute of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekNumericAttribute(AttributeKind attribute, out Rational value)
        {
            Rational lclRat = Rational.Zero;
            var result = Modify(() =>
            {
                if (stack.Count == 0 ||
                    !stack.Peek().TryGetNumericAttribute(attribute, out lclRat))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                return BuilderResultKind.Success;
            });

            value = lclRat;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Tries to returns a boolean attribute of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekBooleanAttribute(AttributeKind attribute, out bool value)
        {
            bool lclBool = false;
            var result = Modify(() =>
            {
                if (stack.Count == 0 ||
                    !stack.Peek().TryGetBooleanAttribute(attribute, out lclBool))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                return BuilderResultKind.Success;
            });

            value = lclBool;
            return result;
        }

        /// <summary>
        /// The stack should be non-empty. Tries to returns a kind attribute of
        /// the stack top.
        /// </summary>
        public BuilderResultKind PeekKindAttribute(AttributeKind attribute, out object value)
        {
            object lclKind = null;
            var result = Modify(() =>
            {
                if (stack.Count == 0 ||
                    !stack.Peek().TryGetKindAttribute(attribute, out lclKind))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                return BuilderResultKind.Success;
            });

            value = lclKind;
            return result;
        }
        
        /***********************************************************/
        /****************       Pushes             *****************/
        /***********************************************************/
        public BuilderResultKind PushConfig(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Config(span));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushCnst(string val, Span span = default(Span))
        {
            return Modify(() =>
                {
                    if (val == null)
                    {
                        return BuilderResultKind.Fail_BadArgs;
                    }

                    stack.Push(new Cnst(span, val));
                    return BuilderResultKind.Success;
                });
        }

        public BuilderResultKind PushCnst(int val, Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Cnst(span, new Rational(val)));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushCnst(double val, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (double.IsInfinity(val) || double.IsNaN(val))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }
                
                stack.Push(new Cnst(span, new Rational(val)));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushCnst(Rational val, Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Cnst(span, val));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushId(string name, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (name == null)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Id(span, name));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkFuncTerm(x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushFuncTerm(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new FuncTerm(span, (Id)stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushFuncTerm(OpKind opKind, Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new FuncTerm(span, opKind));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsFuncOrAtom
        /// 1: x1, s.t. x1.IsFuncOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 1. Pops x1
        /// 2. t = MkRelConstr(x1, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushRelConstr(RelKind relKind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncOrAtom, IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var arg2 = stack.Pop();
                var arg1 = stack.Pop();
               
                stack.Push(new RelConstr(span, relKind, arg1, arg2));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsInteger
        /// 1: x1, s.t. x1.IsInteger
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 1. Pops x1
        /// 2. t = MkRange(x1, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushRange(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsInteger, IsInteger))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var arg2 = ((Cnst)stack.Pop()).GetNumericValue();
                var arg1 = ((Cnst)stack.Pop()).GetNumericValue();
                
                stack.Push(new Range(span, arg1, arg2));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushModRef(string name, string rename, string loc, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ModRef(span, name, rename, loc));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ModRef
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkModApply(x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushModApply(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ModApply(span, (ModRef)stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsFuncOrAtom
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 1. Pops x1
        /// 2. t = MkFind(x1, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushFind(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncOrAtom, IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var arg2 = stack.Pop();
                var arg1 = (Id)stack.Pop();

                stack.Push(new Find(span, arg1, arg2));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsFuncOrAtom
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 1. Pops x1
        /// 2. t = MkModelFact(x1, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushModelFact(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncOrAtom, IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var arg2 = stack.Pop();
                var arg1 = (Id)stack.Pop();

                stack.Push(new ModelFact(span, arg1, arg2));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsFuncOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkFind(null, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushAnonFind(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Find(span, null, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsFuncOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkModelFact(null, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushAnonModelFact(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ModelFact(span, null, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushBody(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Body(span));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushRule(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Rule(span));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushCompr(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Compr(span));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// Push a non-cardinality contract.
        /// </summary>
        public BuilderResultKind PushContract(ContractKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (kind == ContractKind.RequiresSome ||
                    kind == ContractKind.RequiresAtLeast ||
                    kind == ContractKind.RequiresAtMost)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ContractItem(span, kind));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsId
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkContract(kind, x0, cardinality)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushContract(ContractKind kind, int cardinality, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (kind != ContractKind.RequiresSome ||
                    kind != ContractKind.RequiresAtLeast ||
                    kind != ContractKind.RequiresAtMost)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }
                else if (cardinality < 0)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }
                else if (!VerifyStack(IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var ci = new ContractItem(span, kind);
                ci.AddSpecification(new CardPair(span, (Id)stack.Pop(), cardinality));
                stack.Push(ci);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Compr
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkNo(x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushNo(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsCompr))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new RelConstr(span, RelKind.No, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushEnum(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Nodes.Enum(span));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushUnion(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Union(span));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsTypeTerm
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkField(name, x0, isAny)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushField(string name, bool isAny, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTypeTerm))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Field(span, name, stack.Pop(), isAny));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushConDecl(string name, bool isNew, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ConDecl(span, name, isNew, false));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushSubDecl(string name, bool isNew, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new ConDecl(span, name, false, true));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushMapDecl(string name, MapKind kind, bool isPartial, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new MapDecl(span, name, kind, isPartial));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsTypeTerm
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkUnnDecl(name, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushUnnDecl(string name, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name) || !VerifyStack(IsTypeTerm))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new UnnDecl(span, name, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushDomain(string name, ComposeKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Domain(span, name, kind));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsTypeTerm
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkParam(name, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushValueParam(string name, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name) || !VerifyStack(IsTypeTerm))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Param(span, name, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ModRef && x0.Rename != null
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkParam(null, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushModelParam(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsRenamedModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Param(span, null, stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ModApply
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkStep(x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushStep(Span span = default(Span))
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModApply))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Step(span, (ModApply)stack.Pop()));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushTransform(string name, ComposeKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Transform(span, name));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushTSystem(string name, ComposeKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new TSystem(span, name));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushMachine(string name, ComposeKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Machine(span, name));
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.IsModRef
        /// 
        /// Performs the following operations
        /// 1. Pops x0
        /// 2. t = MkModel(name, isPartial, x0)
        /// 3. Pushes t 
        /// </summary>
        public BuilderResultKind PushModel(string name, bool isPartial, ComposeKind kind, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (string.IsNullOrEmpty(name) || !VerifyStack(IsModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new Model(span, name, isPartial, (ModRef)stack.Pop(), kind));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushQuote(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Quote(span));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushQuoteRun(string text, Span span = default(Span))
        {
            return Modify(() =>
            {
                if (text == null)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                stack.Push(new QuoteRun(span, text));
                return BuilderResultKind.Success;
            });
        }

        public BuilderResultKind PushUpdate(Span span = default(Span))
        {
            return Modify(() =>
            {
                stack.Push(new Update(span));
                return BuilderResultKind.Success;
            });
        }

        /***********************************************************/
        /******************       Adds             *****************/
        /***********************************************************/

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.FuncTerm
        /// 1: x1, s.t. x1.IsFuncTermOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddArg(x1, addlast)
        /// 3. Pushes x0
        /// To create f(t_1, ..., t_n) do 
        /// Push t_1, ..., t_n
        /// Push f()
        /// AddArg() ... ArgArg()
        /// </summary>
        public BuilderResultKind AddFuncTermArg(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsFuncTerm, IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var func = (FuncTerm)stack.Pop();
                var arg = stack.Pop();
                func.AddArg(arg, addLast);
                stack.Push(func);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ModApply
        /// 1: x1, s.t. x1.IsModAppArg
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddArg(x1, addlast)
        /// 3. Pushes x0
        /// To create M(t_1, ..., t_n) do 
        /// Push t_1, ..., t_n
        /// Push M()
        /// AddArg() ... ArgArg()
        /// </summary>
        public BuilderResultKind AddModAppArg(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModApply, IsModAppArg))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var modapp = (ModApply)stack.Pop();
                var arg = stack.Pop();
                modapp.AddArg(arg, addLast);
                stack.Push(modapp);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Body
        /// 1: x1, s.t. x1.IsConstraint
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddConjunct(x1, addlast)
        /// 3. Pushes x0
        /// To create c_1, ..., c_n
        /// Push c_1, ..., c_n
        /// Push Body
        /// AddConjunct() ... AddConjunct()
        /// </summary>
        public BuilderResultKind AddConjunct(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsBody, IsConstraint))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var body = (Body)stack.Pop();
                var con = stack.Pop();
                body.AddConstr(con, addLast);
                stack.Push(body);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Rule
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Body
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddBody(x1, addLast)
        /// 3. Pushes x0
        /// To create :- b_1, ..., b_n
        /// Push b_1, ..., b_n
        /// Push Rule
        /// AddRuleBody() ... AddRuleBody()
        /// </summary>
        public BuilderResultKind AddRuleBody(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsRule, IsBody))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var rule = (Rule)stack.Pop();
                var body = (Body)stack.Pop();
                rule.AddBody(body, addLast);
                stack.Push(rule);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Rule
        /// 1: x1, s.t. x1.IsFuncOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddHead(x1, addlast)
        /// 3. Pushes x0
        /// To create h_1, ..., h_n :- 
        /// Push h_1, ..., h_n
        /// Push Rule
        /// AddRuleHead() ... AddRuleHead()
        /// </summary>
        public BuilderResultKind AddRuleHead(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsRule, IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var rule = (Rule)stack.Pop();
                var head = stack.Pop();
                rule.AddHead(head, addLast);
                stack.Push(rule);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Compr
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Body
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddBody(x1, addlast)
        /// 3. Pushes x0
        /// To create :- b_1, ..., b_n
        /// Push b_1, ..., b_n
        /// Push Compr
        /// AddComprBody() ... AddComprBody()
        /// </summary>
        public BuilderResultKind AddComprBody(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsCompr, IsBody))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var compr = (Compr)stack.Pop();
                var body = (Body)stack.Pop();
                compr.AddBody(body, addLast);
                stack.Push(compr);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Rule
        /// 1: x1, s.t. x1.IsFuncOrAtom
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddHead(x1, addLast)
        /// 3. Pushes x0
        /// To create { h_1, ..., h_n | }
        /// Push h_1, ..., h_n
        /// Push Compr
        /// AddComprHead() ... AddComprHead()
        /// </summary>
        public BuilderResultKind AddComprHead(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsCompr, IsFuncOrAtom))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var compr = (Compr)stack.Pop();
                var head = stack.Pop();
                compr.AddHead(head, addLast);
                stack.Push(compr);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Domain
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Rule
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddRule(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddDomainRule(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsDomain, IsRule))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var domain = (Domain)stack.Pop();
                var rule = (Rule)stack.Pop();
                domain.AddRule(rule, addLast);
                stack.Push(domain);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Transform
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Rule
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddRule(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTransformRule(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTransform, IsRule))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var transform = (Transform)stack.Pop();
                var rule = (Rule)stack.Pop();
                transform.AddRule(rule, addLast);
                stack.Push(transform);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Domain
        /// 1: x1, s.t. x1.IsTypeDecl
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddTypeDecl(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddDomainTypeDecl(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsDomain, IsTypeDecl))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var domain = (Domain)stack.Pop();
                var typeDecl = stack.Pop();
                domain.AddTypeDecl(typeDecl, addLast);
                stack.Push(domain);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Transform
        /// 1: x1, s.t. x1.IsTypeDecl
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddTypeDecl(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTransformTypeDecl(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTransform, IsTypeDecl))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var transform = (Transform)stack.Pop();
                var typeDecl = stack.Pop();
                transform.AddTypeDecl(typeDecl, addLast);
                stack.Push(transform);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Model
        /// 1: x1, s.t. x0.CanHaveContract(x1)
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddContract(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddModelContract(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModel, IsModelContract))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var model = (Model)stack.Pop();
                var ci = (ContractItem)stack.Pop();
                model.AddContract(ci, addLast);
                stack.Push(model);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Domain
        /// 1: x1, s.t. x0.CanHaveContract(x1)
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddContract(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddDomainContract(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsDomain, IsDomainContract))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var domain = (Domain)stack.Pop();
                var ci = (ContractItem)stack.Pop();
                domain.AddConforms(ci, addLast);
                stack.Push(domain);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Transform
        /// 1: x1, s.t. x0.CanHaveContract(x1)
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddContract(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTransformContract(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTransform, IsTransformContract))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var transform = (Transform)stack.Pop();
                var ci = (ContractItem)stack.Pop();
                transform.AddContract(ci, addLast);
                stack.Push(transform);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Model
        /// 1: x1, s.t. x1.NodeKind == NodeKind.ModelFact
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddFact(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddModelFact(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModel, IsModelFact))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var model = (Model)stack.Pop();
                var fact = (ModelFact)stack.Pop();
                model.AddFact(fact, addLast);
                stack.Push(model);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Enum
        /// 1: x1, s.t. x1.IsEnumElement
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddElement(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddEnumElement(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsEnum, IsEnumElement))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var enm = (Nodes.Enum)stack.Pop();
                var element = stack.Pop();
                enm.AddElement(element, addLast);
                stack.Push(enm);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.MapDecl
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Field
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddDomField(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMapDom(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMapDecl, IsField))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var decl = (MapDecl)stack.Pop();
                var field = (Field)stack.Pop();
                decl.AddDomField(field, addLast);
                stack.Push(decl);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.MapDecl
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Field
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddCodField(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMapCod(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMapDecl, IsField))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var decl = (MapDecl)stack.Pop();
                var field = (Field)stack.Pop();
                decl.AddCodField(field, addLast);
                stack.Push(decl);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ConDecl
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Field
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddField(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddField(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsConDecl, IsField))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var decl = (ConDecl)stack.Pop();
                var field = (Field)stack.Pop();
                decl.AddField(field, addLast);
                stack.Push(decl);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Union
        /// 1: x1, s.t. x1.IsUnionComponent
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddComponent(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddUnnCmp(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsUnion, IsUnionComponent))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var unn = (Union)stack.Pop();
                var cmp = stack.Pop();
                unn.AddComponent(cmp, addLast);
                stack.Push(unn);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Step
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddLhs(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddStepLhs(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsStep, IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var step = (Step)stack.Pop();
                var id = (Id)stack.Pop();
                step.AddLhs(id, addLast);
                stack.Push(step);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Update
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddState(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddUpdateState(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsUpdate, IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var upd = (Update)stack.Pop();
                var id = (Id)stack.Pop();
                upd.AddState(id, addLast);
                stack.Push(upd);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Update
        /// 1: x1, s.t. x1.NodeKind == NodeKind.ModApply
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddChoice(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddUpdateChoice(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsUpdate, IsModApply))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var upd = (Update)stack.Pop();
                var ma = (ModApply)stack.Pop();
                upd.AddChoice(ma, addLast);
                stack.Push(upd);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Transform
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Param
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddInput(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTransInput(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTransform, IsParam))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var trns = (Transform)stack.Pop();
                var pr = (Param)stack.Pop();
                trns.AddInput(pr, addLast);
                stack.Push(trns);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Transform
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Param
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddOutput(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTransOutput(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTransform, IsParam))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var trns = (Transform)stack.Pop();
                var pr = (Param)stack.Pop();
                trns.AddOutput(pr, addLast);
                stack.Push(trns);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.TSystem
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Param
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddInput(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTSysInput(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTSystem, IsParam))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var tsys = (TSystem)stack.Pop();
                var pr = (Param)stack.Pop();
                tsys.AddInput(pr, addLast);
                stack.Push(tsys);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.TSystem
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Param
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddOutput(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTSysOutput(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTSystem, IsParam))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var tsys = (TSystem)stack.Pop();
                var pr = (Param)stack.Pop();
                tsys.AddOutput(pr, addLast);
                stack.Push(tsys);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Machine
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Param
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddInput(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMachineInput(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMachine, IsParam))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var mach = (Machine)stack.Pop();
                var pr = (Param)stack.Pop();
                mach.AddInput(pr, addLast);
                stack.Push(mach);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Machine
        /// 1: x1, s.t. x1.NodeKind == NodeKind.ModRef
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddStateDomain(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMachineStateDom(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMachine, IsModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var mach = (Machine)stack.Pop();
                var mr = (ModRef)stack.Pop();
                mach.AddStateDomain(mr, addLast);
                stack.Push(mach);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Machine
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Step
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddBootStep(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMachineBoot(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMachine, IsStep))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var mach = (Machine)stack.Pop();
                var stp = (Step)stack.Pop();
                mach.AddBootStep(stp, addLast);
                stack.Push(mach);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Machine
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Update
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddUpdate(x1, isInit, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMachineUpdate(bool isInit, bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMachine, IsUpdate))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var mach = (Machine)stack.Pop();
                var upd = (Update)stack.Pop();
                mach.AddUpdate(upd, isInit, addLast);
                stack.Push(mach);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Machine
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Property
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddProperty(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddMachineProperty(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsMachine, IsUpdate))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var mach = (Machine)stack.Pop();
                var prp = (Property)stack.Pop();
                mach.AddProperty(prp, addLast);
                stack.Push(mach);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.TSystem
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Step
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddStep(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddTSysStep(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsTSystem, IsStep))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var tsys = (TSystem)stack.Pop();
                var stp = (Step)stack.Pop();
                tsys.AddStep(stp, addLast);
                stack.Push(tsys);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.ContractItem
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Body
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddSpecification(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddContractSpec(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsContract, IsContractSpec))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var ci = (ContractItem)stack.Peek();
                if (ci.ContractKind == ContractKind.RequiresSome ||
                    ci.ContractKind == ContractKind.RequiresAtLeast ||
                    ci.ContractKind == ContractKind.RequiresAtMost)
                {
                    return BuilderResultKind.Fail_BadArgs;
                }         

                stack.Pop();
                var spec = stack.Pop();
                ci.AddSpecification(spec, addLast);
                stack.Push(ci);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Model
        /// 1: x1, s.t. x1.NodeKind == NodeKind.ModRef
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddInclude(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddModelCompose(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsModel, IsModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var model = (Model)stack.Pop();
                var mr = (ModRef)stack.Pop();
                model.AddCompose(mr, addLast);
                stack.Push(model);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Domain
        /// 1: x1, s.t. x1.NodeKind == NodeKind.ModRef
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddCompose(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddDomainCompose(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsDomain, IsModRef))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var domain = (Domain)stack.Pop();
                var mr = (ModRef)stack.Pop();
                domain.AddCompose(mr, addLast);
                stack.Push(domain);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Quote
        /// 1: x1, s.t. x1.IsQuoteItem
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddItem(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddQuoteItem(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsQuote, IsQuoteItem))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var quote = (Quote)stack.Pop();
                var qi = stack.Pop();
                quote.AddItem(qi, addLast);
                stack.Push(quote);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Program
        /// 1: x1, s.t. x1.IsModule
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddModule(x1, addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddModule(bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsProgram, IsModule))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var prog = (Program)stack.Pop();
                var md = stack.Pop();
                prog.AddModule(md, addLast);
                stack.Push(prog);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.IsConfigSettable
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Config
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.SetConfig(x1)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind SetConfig()
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsConfigSettable, IsConfig))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var node = stack.Pop();
                var conf = (Config)stack.Pop();
                switch (node.NodeKind)
                {
                    case NodeKind.Rule:
                            ((Rule)node).SetConfig(conf);
                            break;
                    case NodeKind.Step:
                            ((Step)node).SetConfig(conf);
                            break;
                    case NodeKind.Update:
                            ((Update)node).SetConfig(conf);
                            break;
                    case NodeKind.Property:
                            ((Property)node).SetConfig(conf);
                            break;
                    case NodeKind.ContractItem:
                            ((ContractItem)node).SetConfig(conf);
                            break;
                    case NodeKind.ModelFact:
                            ((ModelFact)node).SetConfig(conf);
                            break;
                    case NodeKind.UnnDecl:
                            ((UnnDecl)node).SetConfig(conf);
                            break;
                    case NodeKind.ConDecl:
                            ((ConDecl)node).SetConfig(conf);
                            break;
                    case NodeKind.MapDecl:
                            ((MapDecl)node).SetConfig(conf);
                            break;
                    default:
                        throw new NotImplementedException();
                }

                stack.Push(node);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// The stack should be in the configuration:
        /// 0: x0, s.t. x0.NodeKind == NodeKind.Config
        /// 1: x1, s.t. x1.NodeKind == NodeKind.Cnst
        /// 2: x2, s.t. x2.NodeKind == NodeKind.Id
        /// 
        /// Performs the following operations
        /// 1. Pops x0, x1
        /// 2. Applies x0.AddSetting(Setting(x2, x1), addLast)
        /// 3. Pushes x0
        /// </summary>
        public BuilderResultKind AddSetting(Span span = default(Span), bool addLast = false)
        {
            return Modify(() =>
            {
                if (!VerifyStack(IsConfig, IsCnst, IsId))
                {
                    return BuilderResultKind.Fail_BadArgs;
                }

                var config = (Config)stack.Pop();
                var cnst = (Cnst)stack.Pop();
                var key = (Id)stack.Pop();
                config.AddSetting(new Setting(span, key, cnst), addLast);
                stack.Push(config);
                return BuilderResultKind.Success;
            });
        }

        /// <summary>
        /// Closes the builder and interprets the nodes on the stack as roots of that many ASTs.
        /// Returns the number of nodes left on the stack.
        /// </summary>
        public int Close()
        {
            Modify(() =>
            {
                isClosed = true;
                int i = 0;
                var asts = new AST<Node>[stack.Count];
                foreach (var n in stack)
                {
                    asts[i++] = Factory.Instance.ToAST(n);
                }

                finalNodes = new ImmutableArray<AST<Node>>(asts);
                return BuilderResultKind.Success;
            });

            return stack.Count;
        }

        /// <summary>
        /// Returns the ASTs that have been built. Succeeds and returns true if the builder
        /// has been closed; otherwise it returns false.
        /// </summary>
        public bool GetASTs(out ImmutableArray<AST<Node>> asts)
        {
            if (IsClosed)
            {
                asts = finalNodes;
                return true;
            }
            else
            {
                asts = null;
                return false;
            }
        }

        private bool VerifyStack(Predicate<Node> predicate)
        {
            if (stack.Count < 1)
            {
                return false;
            }

            return predicate(stack.Peek());
        }

        private bool VerifyStack(Predicate<Node> predicate1, Predicate<Node> predicate2)
        {
            if (stack.Count < 2)
            {
                return false;
            }

            using (var it = stack.GetEnumerator())
            {
                it.MoveNext();
                if (!predicate1(it.Current))
                {
                    return false;
                }

                it.MoveNext();
                if (!predicate2(it.Current))
                {
                    return false;
                }
            }

            return true;
        }

        private bool VerifyStack(Predicate<Node> predicate1, Predicate<Node> predicate2, Predicate<Node> predicate3)
        {
            if (stack.Count < 3)
            {
                return false;
            }

            using (var it = stack.GetEnumerator())
            {
                it.MoveNext();
                if (!predicate1(it.Current))
                {
                    return false;
                }

                it.MoveNext();
                if (!predicate2(it.Current))
                {
                    return false;
                }

                it.MoveNext();
                if (!predicate3(it.Current))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsInteger(Node n)
        {
            if (n.NodeKind != NodeKind.Cnst)
            {
                return false;
            }

            var cnst = (Cnst)n;
            if (cnst.CnstKind != CnstKind.Numeric)
            {
                return false;
            }

            return cnst.GetNumericValue().IsInteger;
        }

        private static bool IsCnst(Node n)
        {
            return n.NodeKind == NodeKind.Cnst;
        }

        private static bool IsId(Node n)
        {
            return n.NodeKind == NodeKind.Id;
        }

        private static bool IsFuncOrAtom(Node n)
        {
            return n.IsFuncOrAtom;
        }

        private static bool IsModAppArg(Node n)
        {
            return n.IsModAppArg;
        }

        private static bool IsFuncTerm(Node n)
        {
            return n.NodeKind == NodeKind.FuncTerm;
        }

        private static bool IsModApply(Node n)
        {
            return n.NodeKind == NodeKind.ModApply;        
        }

        private static bool IsBody(Node n)
        {
            return n.NodeKind == NodeKind.Body;
        }

        private static bool IsRule(Node n)
        {
            return n.NodeKind == NodeKind.Rule;
        }

        private static bool IsCompr(Node n)
        {
            return n.NodeKind == NodeKind.Compr;
        }

        private static bool IsDomain(Node n)
        {
            return n.NodeKind == NodeKind.Domain;
        }

        private static bool IsTransform(Node n)
        {
            return n.NodeKind == NodeKind.Transform;
        }

        private static bool IsModel(Node n)
        {
            return n.NodeKind == NodeKind.Model;
        }

        private static bool IsFind(Node n)
        {
            return n.NodeKind == NodeKind.Find;
        }

        private static bool IsModelFact(Node n)
        {
            return n.NodeKind == NodeKind.ModelFact;
        }

        private static bool IsModelContract(Node n)
        {
            return n.NodeKind == NodeKind.ContractItem;
        }

        private static bool IsContract(Node n)
        {
            return n.NodeKind == NodeKind.ContractItem;
        }

        private static bool IsTransformContract(Node n)
        {
            if (n.NodeKind != NodeKind.ContractItem)
            {
                return false;
            }

            var ci = (ContractItem)n;
            return ci.ContractKind == ContractKind.RequiresProp || 
                   ci.ContractKind == ContractKind.EnsuresProp;
        }

        private static bool IsDomainContract(Node n)
        {
            if (n.NodeKind != NodeKind.ContractItem)
            {
                return false;
            }

            var ci = (ContractItem)n;
            return ci.ContractKind == ContractKind.ConformsProp;
        }

        private static bool IsConstraint(Node n)
        {
            return n.IsConstraint;
        }

        private static bool IsTypeTerm(Node n)
        {
            return n.IsTypeTerm;
        }

        private static bool IsRenamedModRef(Node n)
        {
            return n.NodeKind == NodeKind.ModRef && ((ModRef)n).Rename != null;
        }

        private static bool IsTypeDecl(Node n)
        {
            return n.IsTypeDecl;
        }

        private static bool IsParamType(Node n)
        {
            return n.IsParamType;
        }

        private static bool IsEnum(Node n)
        {
            return n.NodeKind == NodeKind.Enum;
        }

        private static bool IsMapDecl(Node n)
        {
            return n.NodeKind == NodeKind.MapDecl;
        }

        private static bool IsConDecl(Node n)
        {
            return n.NodeKind == NodeKind.ConDecl;
        }

        private static bool IsUnnDecl(Node n)
        {
            return n.NodeKind == NodeKind.UnnDecl;
        }

        private static bool IsUnion(Node n)
        {
            return n.NodeKind == NodeKind.Union;
        }

        private static bool IsUnionComponent(Node n)
        {
            return n.IsUnionComponent;
        }

        private static bool IsField(Node n)
        {
            return n.NodeKind == NodeKind.Find;
        }

        private static bool IsParam(Node n)
        {
            return n.NodeKind == NodeKind.Param;
        }

        private static bool IsEnumElement(Node n)
        {
            return n.IsEnumElement;
        }

        private static bool IsStep(Node n)
        {
            return n.NodeKind == NodeKind.Step;
        }

        private static bool IsUpdate(Node n)
        {
            return n.NodeKind == NodeKind.Update;
        }

        private static bool IsTSystem(Node n)
        {
            return n.NodeKind == NodeKind.TSystem;
        }

        private static bool IsMachine(Node n)
        {
            return n.NodeKind == NodeKind.Machine;
        }

        private static bool IsQuote(Node n)
        {
            return n.NodeKind == NodeKind.Quote;
        }

        private static bool IsConfig(Node n)
        {
            return n.NodeKind == NodeKind.Config;
        }

        private static bool IsProgram(Node n)
        {
            return n.NodeKind == NodeKind.Program;
        }

        private static bool IsModule(Node n)
        {
            return n.IsModule;
        }

        private static bool IsFolder(Node n)
        {
            return n.NodeKind == NodeKind.Folder;
        }

        private static bool IsContractSpec(Node n)
        {
            return n.IsContractSpec;
        }

        private static bool IsModRef(Node n)
        {
            return n.NodeKind == NodeKind.ModRef;
        }

        private static bool IsProperty(Node n)
        {
            return n.NodeKind == NodeKind.Property;
        }

        private static bool IsQuoteItem(Node n)
        {
            return n.IsQuoteItem;
        }

        private static bool IsConfigSettable(Node n)
        {
            return n.IsConfigSettable;
        }

        private BuilderResultKind Modify(Func<BuilderResultKind> action)
        {
            bool gotLock = false;
            try
            {
                builderLock.Enter(ref gotLock);
                if (isClosed)
                {
                    return BuilderResultKind.Fail_Closed;
                }
                else
                {
                    return action();
                }
            }
            finally
            {
                if (gotLock)
                {
                    builderLock.Exit();
                }
            }            
        }
    }
}
