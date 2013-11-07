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
    /// An action pairs together one constraint system and one head. 
    /// These can come from either comprehensions or rules.
    /// </summary>
    internal class Action
    {
        private Symbol theUnnSymbol;
        private Symbol theRngSymbol;
        private ComprehensionData myComprData;
        private TypeEnvironment typeEnvironment;

        internal Node Head
        {
            get;
            private set;
        }

        internal Term HeadTerm
        {
            get;
            private set;
        }

        internal Term HeadType
        {
            get;
            private set;
        }

        internal ConstraintSystem Body
        {
            get;
            private set;
        }

        internal TermIndex Index
        {
            get { return Body.Index; }
        }

        internal Action(
            Node head, 
            ConstraintSystem body,
            TypeEnvironment typeEnv,
            ComprehensionData comprData = null)
        {
            Head = head;
            Body = body;
            myComprData = comprData;

            typeEnvironment = typeEnv;
            theUnnSymbol = body.Index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn);
            theRngSymbol = body.Index.SymbolTable.GetOpSymbol(ReservedOpKind.Range);
        }

        public bool Validate(List<Flag> flags, CancellationToken cancel, bool isCompilerAction = false)
        {
            var success = new SuccessToken();
            var symbStack = new Stack<Tuple<Namespace, Symbol>>();
            symbStack.Push(new Tuple<Namespace, Symbol>(Index.SymbolTable.Root, null));
            var head = Factory.Instance.ToAST(Head).Compute<Tuple<Term, Term>>(
                x => CreateHeadTerm_Unfold(x, symbStack, success, flags),
                (x, y) => CreateHeadTerm_Fold(x, y, symbStack, success, flags),
                cancel);

            if (head == null)
            {
                success.Failed();
            }
            else if (head != null)
            {
                HeadTerm = head.Item1;
                HeadType = head.Item2;
            }

            if (myComprData == null && head != null)
            {
                head.Item2.Visit(
                    x => x.Symbol == theUnnSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol.IsNewConstant)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                Head,
                                Constants.DerivesError.ToString(string.Format("the new constant {0}", x.Symbol.PrintableName), "it is illegal to derive new-kind constants"),
                                Constants.DerivesError.Code);
                            flags.Add(flag);
                            success.Failed();
                        }
                        else if (x.Symbol.Kind == SymbolKind.BaseSortSymb)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                Head,
                                Constants.DerivesError.ToString(string.Format("values of type {0}", x.Symbol.PrintableName), "it is illegal to derive base constants"),
                                Constants.DerivesError.Code);
                            flags.Add(flag);
                            success.Failed();
                        }
                        else if (!isCompilerAction && Index.SymbolTable.IsProtectedHeadSymbol(x.Symbol))
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                Head,
                                Constants.DerivesError.ToString(string.Format("values of the form {0}", x.Symbol.PrintableName), "user-defined rules cannot derive these values"),
                                Constants.DerivesError.Code);
                            flags.Add(flag);
                            success.Failed();
                        }
                    });
            }
            
            return success.Result;
        }

        /// <summary>
        /// Compile the action into rules. Should not be called unless successfully validated.
        /// </summary>
        public bool Compile(RuleTable rules, List<Flag> flags, CancellationToken cancel)
        {
            FindData[] parts;
            if (!Body.Compile(rules, out parts, flags, cancel))
            {
                return false;
            }

            if (myComprData == null)
            {
                rules.CompileRule(HeadTerm, HeadType, parts, Head, Body);
            }
            else
            {                
                rules.CompileRule(rules.MkComprHead(myComprData, HeadTerm), Index.FalseValue, parts, Head, Body);
            }

            return true;
        }

        public int GetNextVarId(FreshVarKind kind)
        {
            return Body.GetNextVarId(kind);
        }

        private IEnumerable<Node> CreateHeadTerm_Unfold(Node n,
                                                        Stack<Tuple<Namespace, Symbol>> symbStack,
                                                        SuccessToken success,
                                                        List<Flag> flags)
        {
            var space = symbStack.Peek().Item1;
            UserSymbol other;
            switch (n.NodeKind)
            {
                case NodeKind.Cnst:
                    {
                        if (myComprData == null && symbStack.Count == 1)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadSyntax.ToString(string.Format("A rule cannot produce the base constant {0}", ((Cnst)n).Raw)),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }

                        bool wasAdded;
                        var cnst = (Cnst)n;
                        BaseCnstSymb symb;
                        switch (cnst.CnstKind)
                        {
                            case CnstKind.Numeric:
                                symb = (BaseCnstSymb)Index.MkCnst((Rational)cnst.Raw, out wasAdded).Symbol;
                                break;
                            case CnstKind.String:
                                symb = (BaseCnstSymb)Index.MkCnst((string)cnst.Raw, out wasAdded).Symbol;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        symbStack.Push(new Tuple<Namespace, Symbol>(space, symb));
                        return null;
                    }
                case NodeKind.Id:
                    {
                        var id = (Id)n;
                        UserSymbol symb;
                        if (Index.SymbolTable.HasRenamingPrefix(id))
                        {
                            if (!Resolve(id.Name, "constant", id, space, x => x.IsNonVarConstant, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }
                        else if (myComprData == null &&
                                 id.Fragments.Length == 1 &&
                                 (symb = Index.SymbolTable.Resolve(id.Fragments[0], out other, Index.SymbolTable.ModuleSpace, x => x.IsDerivedConstant)) != null &&
                                 other == null &&
                                 symb.Namespace == Index.SymbolTable.ModuleSpace)
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            return null;
                        }
                        else if (!Resolve(id.Fragments[0], "variable or constant", id, space, x => x.Kind == SymbolKind.UserCnstSymb, out symb, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else if (id.Fragments.Length > 1 && symb.IsNonVarConstant)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                id,
                                Constants.BadSyntax.ToString("constants do not have fields"),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }

                        if (symb.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symb).IsSymbolicConstant)
                        {
                            symb = (UserSymbol)Index.MkScVar(((UserCnstSymb)symb).FullName, false).Symbol;
                        }

                        symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                        return null;
                    }
                case NodeKind.FuncTerm:
                    {
                        var ft = (FuncTerm)n;
                        if (ft.Function is Id)
                        {
                            UserSymbol symb;
                            var ftid = (Id)ft.Function;
                            if (ASTSchema.Instance.IsId(ftid.Name, true, false, false, true) && 
                                ftid.Fragments[ftid.Fragments.Length - 1] == ASTSchema.Instance.DontCareName)                            
                            {
                                var nsName = ftid.Fragments.Length == 1
                                             ? string.Empty
                                             : ftid.Name.Substring(0, ftid.Name.Length - 2);
                                Namespace ns, ons;
                                ns = Index.SymbolTable.Resolve(nsName, out ons, space);
                                if (!AddNamespaceErrors(n, nsName, ns, ons, flags))
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                    success.Failed();
                                    return null;
                                }
                                else
                                {
                                    symbStack.Push(new Tuple<Namespace, Symbol>(ns, null));
                                }
                            }
                            else if (ValidateUse_UserFunc(ft, space, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            }
                            else
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            return ft.Args;
                        }
                        else
                        {
                            string opName;
                            if (ft.Function is RelKind)
                            {
                                opName = ASTSchema.Instance.ToString((RelKind)ft.Function);
                            }
                            else if (ft.Function is OpKind)
                            {
                                OpStyleKind kind;
                                opName = ASTSchema.Instance.ToString((OpKind)ft.Function, out kind);
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }

                            var flag = new Flag(
                                SeverityKind.Error,
                                ft,
                                Constants.BadSyntax.ToString(string.Format("The function {0} cannot appear here; considering moving it to the right-hand side.", opName)),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        private Tuple<Term, Term> CreateHeadTerm_Fold(
                   Node n,
                   IEnumerable<Tuple<Term, Term>> args,
                   Stack<Tuple<Namespace, Symbol>> symbStack,
                   SuccessToken success,
                   List<Flag> flags)
        {
            bool wasAdded;
            Term valTerm, typTerm;
            var space = symbStack.Peek().Item1;
            var symb = symbStack.Pop().Item2;

            if (symb == null && space == null)
            {
                return null;
            }
            else if (symb == null)
            {
                var data = args.First<Tuple<Term, Term>>();
                if (data == null)
                {
                    return null;
                }

                return SmpIdentity(space, data.Item1, data.Item2, n, flags);
            }
            if (symb.IsNonVarConstant)
            {
                valTerm = Index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                return new Tuple<Term, Term>(valTerm, valTerm);
            }
            else if (symb.IsVariable)
            {
                valTerm = MkSelectors((Id)n, symb);
                if (!Body.TryGetType(valTerm, out typTerm))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.UnorientedError.ToString(((Id)n).Name),
                        Constants.UnorientedError.Code));

                    return null;
                }
                else
                {
                    return new Tuple<Term, Term>(valTerm, Index.MkDataWidenedType(typTerm));
                }
            }
            else if (symb.IsDataConstructor)
            {
                var con = (UserSymbol)symb;
                var sort = symb.Kind == SymbolKind.ConSymb ? ((ConSymb)con).SortSymbol : ((MapSymb)con).SortSymbol;

                var vargs = new Term[con.Arity];
                var targs = new Term[con.Arity];

                var i = 0;
                var typed = true;
                Tuple<Term, Term> coerced;
                foreach (var a in args)
                {
                    if (a == null)
                    {
                        //// If an arg is null, then it already has errors, 
                        //// so skip it an check the rest.
                        typed = false;
                        continue;
                    }

                    coerced = Coerce(
                                sort.DataSymbol.CanonicalForm[i],
                                a.Item1,
                                a.Item2,
                                i,
                                n,
                                sort.PrintableName,
                                flags);

                    if (coerced == null)
                    {
                        ++i;
                        typed = false;
                        continue;
                    }

                    vargs[i] = coerced.Item1;
                    targs[i] = coerced.Item2;
                    ++i;
                }

                if (!typed)
                {
                    success.Failed();
                    return null;
                }

                return new Tuple<Term, Term>(
                            Index.MkApply(con, vargs, out wasAdded),
                            Index.MkApply(con, targs, out wasAdded));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private Term MkSelectors(Id id, Symbol varSymb)
        {
            bool wasAdded;
            var sel = Index.MkApply(varSymb, TermIndex.EmptyArgs, out wasAdded);
            for (int i = 1; i < id.Fragments.Length; ++i)
            {
                sel = Index.MkApply(
                    Index.SymbolTable.GetOpSymbol(ReservedOpKind.Select),
                    new Term[] { sel, Index.MkCnst(id.Fragments[i], out wasAdded) },
                    out wasAdded);
            }

            return sel;
        }

        private bool ValidateUse_UserFunc(FuncTerm ft, Namespace space, out UserSymbol symbol, List<Flag> flags)
        {
            Contract.Assert(ft.Function is Id);
            var result = true;
            var id = (Id)ft.Function;

            if (!Resolve(id.Name, "constructor", id, space, x => x.IsDataConstructor, out symbol, flags))
            {
                result = false;
            }
            else if (symbol.Arity != ft.Args.Count)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.BadSyntax.ToString(string.Format("{0} got {1} arguments but needs {2}", symbol.FullName, ft.Args.Count, symbol.Arity)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                result = false;
            }

            var i = 0;
            foreach (var a in ft.Args)
            {
                ++i;
                if (a.NodeKind != NodeKind.Compr)
                {
                    continue;
                }

                var flag = new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.BadSyntax.ToString(string.Format("comprehension not allowed in argument {1} of {0}", symbol == null ? id.Name : symbol.FullName, i)),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                result = false;
            }

            return result;
        }

        private bool Resolve(
                        string id,
                        string kind,
                        Node n,
                        Namespace space,
                        Predicate<UserSymbol> validator,
                        out UserSymbol symbol,
                        List<Flag> flags,
                        bool filterLookup = false)
        {
            UserSymbol other = null;

            symbol = Index.SymbolTable.Resolve(id, out other, space, filterLookup ? validator : null);
            if (symbol == null || !validator(symbol))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.UndefinedSymbol.ToString(kind, id),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }
            else if (other != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.AmbiguousSymbol.ToString(
                        "identifier",
                        id,
                        string.Format("({0}, {1}): {2}",
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                symbol.FullName),
                        string.Format("({0}, {1}): {2}",
                                other.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                other.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                other.FullName)),
                    Constants.AmbiguousSymbol.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private bool AddNamespaceErrors(
                        Node n,
                        string spaceName,
                        Namespace space,
                        Namespace other,
                        List<Flag> flags)
        {

            if (space == null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.UndefinedSymbol.ToString("namespace", spaceName),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }
            else if (other != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.AmbiguousSymbol.ToString(
                        "namespace",
                        spaceName,
                        space.FullName,
                        other.FullName),
                    Constants.AmbiguousSymbol.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private Tuple<Term, Term> SmpIdentity(Namespace idSpace,
                                              Term arg,
                                              Term argType,
                                              Node appNode,
                                              List<Flag> flags)
        {
            UserSymbol other;
            var anySymbol = Index.SymbolTable.Resolve(
                                ASTSchema.Instance.TypeNameAny,
                                out other,
                                idSpace.Parent == null ? Index.SymbolTable.ModuleSpace : idSpace);
            Contract.Assert(anySymbol != null && other == null);
            Contract.Assert(anySymbol.Kind == SymbolKind.UnnSymb);
            return Coerce(
                        anySymbol.CanonicalForm[0],
                        arg,
                        argType,
                        0,
                        appNode,
                        idSpace.Parent == null ? ASTSchema.Instance.DontCareName : idSpace.Name + "." + ASTSchema.Instance.DontCareName,
                        flags);
        }

        /// <summary>
        /// If arg : argType must be a subtype of acceptingType, then returns
        /// a possibly coerced term satisfying the property. The type of the
        /// coerced term is also returned.
        /// 
        /// Returns null and generates errors of if the term cannot be
        /// coerced.
        /// 
        /// If computeType is false, then the type of the coerced
        /// term is not returned.
        /// </summary>
        private Tuple<Term, Term> Coerce(AppFreeCanUnn acceptingType,
                                         Term arg,
                                         Term argType,
                                         int argIndex,
                                         Node appNode,
                                         string appFun,
                                         List<Flag> flags)
        {         
            bool wasAdded;
            Set<Namespace> spaces;
            Namespace maxPrefix = null;
            LiftedBool isCoercable = LiftedBool.True;
            Set<UserSymbol> dataSorts = null;
            Term resultType = null;
            UserSymbol us;
            UserSortSymb uss;

            //// Step 1. Check that all constants are accepted, and all data sorts
            //// can potentially be coerced.
            //// After this loop, resultType contains all constants.
            argType.Visit(
                x => x.Symbol == theUnnSymbol ? x.Args : null,
                t =>
                {
                    if (t.Symbol == theUnnSymbol)
                    {
                        return;
                    }
                    else if (t.Symbol.Kind == SymbolKind.UserSortSymb ||
                             t.Symbol.IsDataConstructor ||
                             t.Symbol.IsDerivedConstant ||
                             (t.Symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)t.Symbol).IsTypeConstant))
                    {
                        if (t.Symbol.Kind == SymbolKind.UserSortSymb)
                        {
                            uss = (UserSortSymb)t.Symbol;
                            us = uss.DataSymbol;
                        }
                        else if (t.Symbol.IsDataConstructor)
                        {
                            us = (UserSymbol)t.Symbol;
                            uss = us.Kind == SymbolKind.ConSymb ? ((ConSymb)us).SortSymbol : ((MapSymb)us).SortSymbol;
                        }
                        else
                        {
                            uss = null;
                            us = (UserSymbol)t.Symbol;
                        }

                        if (maxPrefix == null)
                        {
                            maxPrefix = us.Namespace;
                        }
                        else
                        {
                            us.Namespace.TryGetPrefix(maxPrefix, out maxPrefix);
                        }

                        if (dataSorts == null)
                        {
                            dataSorts = new Set<UserSymbol>(Symbol.Compare);
                        }

                        dataSorts.Add(us);
                        if (!acceptingType.Contains(uss == null ? (Symbol)us : uss))
                        {
                            if (!acceptingType.TryGetRenamings(us.Name, out spaces))
                            {
                                var flag = new Flag(
                                    SeverityKind.Error,
                                    appNode,
                                    Constants.UnsafeArgType.ToString(
                                        argIndex + 1,
                                        appFun,
                                        t.Symbol.PrintableName),
                                    Constants.UnsafeArgType.Code);
                                flags.Add(flag);
                                isCoercable = LiftedBool.False;
                            }
                            else if (isCoercable == LiftedBool.True)
                            {
                                isCoercable = LiftedBool.Unknown;
                            }
                        }
                    }
                    else if (!acceptingType.AcceptsConstants(t))
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            appNode,
                            Constants.UnsafeArgType.ToString(
                                argIndex + 1,
                                appFun,
                                t.Symbol != theRngSymbol ? t.Symbol.PrintableName : (t.Args[0].Symbol.PrintableName + ".." + t.Args[1].Symbol.PrintableName)),
                            Constants.UnsafeArgType.Code);
                        flags.Add(flag);
                        isCoercable = LiftedBool.False;
                    }
                    else 
                    {
                        resultType = resultType == null
                                        ? t
                                        : Index.MkApply(theUnnSymbol, new Term[] { t, resultType }, out wasAdded);
                    }
                });

            if (isCoercable == false)
            {
                return null;
            }
            else if (isCoercable == true)
            {
                return new Tuple<Term, Term>(arg, argType);
            }

            //// Step 2. Check that there is a unique coercion from the user sorts.
            Contract.Assert(dataSorts != null && maxPrefix != null);
            Set<Namespace> rnmgs = null, cndts;
            Namespace prefix;
            string[] suffix;

            foreach (var s in dataSorts)
            {
                suffix = s.Namespace.Split(maxPrefix);
                Contract.Assert(suffix != null);

                acceptingType.TryGetRenamings(s.Name, out spaces);
                cndts = new Set<Namespace>(Namespace.Compare);
                foreach (var ns in spaces)
                {
                    if (ns.Split(suffix, out prefix))
                    {
                        cndts.Add(prefix);
                    }
                }

                if (rnmgs == null)
                {
                    rnmgs = cndts;
                }
                else
                {
                    rnmgs.IntersectWith(cndts);
                }

                if (rnmgs.Count == 0)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        appNode,
                        Constants.UncoercibleArgType.ToString(
                            argIndex + 1,
                            appFun,
                            s.PrintableName),
                        Constants.UncoercibleArgType.Code);
                    flags.Add(flag);
                    return null;
                }
            }

            if (rnmgs.Count != 1)
            {
                foreach (var ns in rnmgs)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        appNode,
                        Constants.AmbiguousCoercibleArg.ToString(
                            argIndex + 1,
                            appFun,
                            maxPrefix.FullName,
                            ns.FullName),
                        Constants.AmbiguousCoercibleArg.Code);
                    flags.Add(flag);
                }

                return null;
            }

            var from = maxPrefix;
            var to = rnmgs.GetSomeElement();
            Symbol coerced;
            foreach (var ds in dataSorts)
            {
                if (ds.Kind == SymbolKind.UserCnstSymb)
                {
                    if (!Index.SymbolTable.IsCoercible(ds, from, to, out coerced))
                    {
                        coerced = null;
                    }
                }
                else 
                {
                    uss = ds.Kind == SymbolKind.ConSymb ? ((ConSymb)ds).SortSymbol : ((MapSymb)ds).SortSymbol;
                    if (!Index.SymbolTable.IsCoercible(uss, from, to, out coerced))
                    {
                        coerced = null;
                    }
                }
                                        
                if (coerced == null)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        appNode,
                        Constants.UncoercibleArgType.ToString(
                            argIndex + 1,
                            appFun,
                            ds.PrintableName),
                        Constants.UncoercibleArgType.Code);
                    flags.Add(flag);
                    return null;
                }
                else
                {
                    resultType = resultType == null
                                    ? Index.MkApply(coerced, TermIndex.EmptyArgs, out wasAdded)
                                    : Index.MkApply(
                                            theUnnSymbol, 
                                            new Term[] { Index.MkApply(coerced, TermIndex.EmptyArgs, out wasAdded), resultType }, 
                                            out wasAdded);
                }
            }

            typeEnvironment.AddCoercion(appNode, argIndex, from.FullName, to.FullName); 
            var coercedArg = Index.MkApply(
                                Index.SymbolTable.GetOpSymbol(ReservedOpKind.Relabel),
                                new Term[] { 
                                    Index.MkCnst(from.FullName, out wasAdded),
                                    Index.MkCnst(to.FullName, out wasAdded),
                                    arg},
                                out wasAdded);

            return new Tuple<Term, Term>(coercedArg, resultType);
        }
    }
}
