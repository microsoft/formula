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
    using API.Generators;
    using API.Plugins;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Common.Rules;
    
    /// <summary>
    /// A FactSet computes the database of facts contained in a (partial) model.
    /// </summary>
    internal class FactSet
    {
        private static int CancelCheckFreq = 1000;

        private static ICSharpTerm[] EmptyCSharpTerm = new ICSharpTerm[0];

        /// <summary>
        /// After an operation is completed, then API clients may indirectly create terms in the index of this set.
        /// These operations need to locked.
        /// </summary>
        private SpinLock termIndexLock = new SpinLock();

        private ModuleData myModuleData;

        private Map<UserSymbol, AliasData> aliasDataMap = 
            new Map<UserSymbol, AliasData>(Symbol.Compare);

        private Set<Term> unaliasedNGTerms = new Set<Term>(Term.Compare);
        private Set<Term> facts = new Set<Term>(Term.Compare);

        public AST<Model> Model
        {
            get;
            private set;
        }

        public TermIndex Index
        {
            get;
            private set;
        }

        public RuleTable Rules
        {
            get;
            private set;
        }

        public Set<Term> Facts
        {
            get { return facts; }
        }

        public FactSet(ModuleData modData)
        {
            Contract.Requires(modData != null);
            myModuleData = modData;
            Model = (AST<Model>)modData.Reduced;
            Index = new TermIndex(modData.SymbolTable);
        }

        public FactSet(TermIndex index, Set<Term> facts)
        {
            Contract.Requires(index != null && facts != null);
            myModuleData = null;
            Model = null;
            Index = index;
            Rules = null;
            this.facts = facts;
        }

        public bool Validate(List<Flag> flags, CancellationToken cancel)
        {
            if (!string.IsNullOrEmpty(Model.Node.Domain.Rename))
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    Model.Node.Domain,
                    Constants.BadSyntax.ToString("Renaming operator cannot be used here"),
                    Constants.BadSyntax.Code));
                return false;
            }

            var success = new SuccessToken();
            foreach (var mr in Model.Node.Compositions)
            {
                if (!(mr.CompilerData is Location))
                {
                    return false;
                }

                var md = ((Location)mr.CompilerData).AST.Node.CompilerData as ModuleData;
                if (md == null)
                {
                    return false;
                }

                var fs = md.FinalOutput as FactSet;
                if (fs == null)
                {
                    return false;
                }

                if (!Contains(Model.Node.Domain, fs.Model.Node.Domain, mr.Rename))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        mr,
                        Constants.ModelCmpError.ToString(fs.Model.Node.Name),
                        Constants.ModelCmpError.Code));
                    success.Failed();
                }

                ImportFactSet(fs, mr.Rename);
            }

            if (!success.Result)
            {
                return false;
            }

            Term t;
            UserSymbol v;
            var stack = new Stack<Tuple<Namespace, Symbol>>();            
            int checkCancel = 1;

            stack.Push(new Tuple<Namespace, Symbol>(null, null));
            foreach (var f in Model.Node.Facts)
            {
                Contract.Assert(stack.Count == 1);
                if (checkCancel % CancelCheckFreq == 0)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return false;
                    }

                    checkCancel = 1;
                }
                else
                {
                    ++checkCancel;
                }
                
                Set<UserSymbol> aliases = new Set<UserSymbol>(Symbol.Compare);
                t = Factory.Instance.ToAST(f.Match).Compute<Term>(
                    (x) => CreateFact_Unfold(x, stack, success, flags),
                    (x, ch) => CreateFact_Fold(x, ch, stack, aliases, success, flags));

                if (t == null)
                {
                    Contract.Assert(!success.Result);
                    continue;
                }

                if (aliases.Count == 0)
                {
                    facts.Add(t);
                }

                if (f.Binding == null)
                {
                    if (aliases.Count > 0)
                    {
                        unaliasedNGTerms.Add(t);
                    }

                    continue;
                }

                if (f.Binding.Fragments.Length == 1 && f.Binding.Name == ASTSchema.Instance.DontCareName)
                {
                    v = (UserSymbol)f.Binding.CompilerData;
                }
                else if (!Index.SymbolTable.ModuleSpace.TryGetSymbol("%" + f.Binding.Name, out v))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        f.Binding,
                        Constants.UndefinedSymbol.ToString("alias", f.Binding.Name),
                        Constants.UndefinedSymbol.Code);
                    flags.Add(flag);
                    success.Failed();
                    continue;
                }
               
                if (!GetData(v, f).TryDefine(f, t, aliases, aliasDataMap, flags))
                {
                    success.Failed();
                    continue;
                }
            }

            if (success.Result && !cancel.IsCancellationRequested)
            {
                if (ValidateOrientation(flags, cancel))
                {
                    Index.SymbCnstTypeGetter = GetSymbCnstType;
                    foreach (var uat in unaliasedNGTerms)
                    {
                        facts.Add(ExpandTerm(uat));
                    }
                }
                else 
                {
                    success.Failed();
                }
            }

            if (success.Result && !cancel.IsCancellationRequested)
            {
                Rules = new RuleTable(myModuleData, Index);
                if (!Rules.Compile(flags, cancel))
                {
                    success.Failed();
                }

                //// Rules.Debug_PrintRuleTable();
            }

            return success.Result && !cancel.IsCancellationRequested;
        }

        /// <summary>
        /// Should only be called if the set compiled without errors.
        /// </summary>
        public Term GetSymbCnstValue(Term symbCnst)
        {
            return aliasDataMap[(UserSymbol)symbCnst.Symbol].ExpDefinition;
        }

        /// <summary>
        /// Should only be called if the set compiled without errors.
        /// </summary>
        public Term GetSymbCnstType(Term symbCnst)
        {
            var data = aliasDataMap[(UserSymbol)symbCnst.Symbol];
            Contract.Assert(data.ExpDefinition != null);

            int i;
            bool changed;
            AliasData datap;
            return data.ExpDefinition.Compute<Term>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    if (x.Symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)x.Symbol).IsSymbolicConstant)
                    {
                        datap = aliasDataMap[(UserSymbol)x.Symbol];
                        Contract.Assert(datap.Type != null);
                        return datap.Type;
                    }

                    i = 0;
                    changed = false;
                    foreach (var t in ch)
                    {
                        if (t != x.Args[i++])
                        {
                            changed = true;
                            break;
                        }
                    }

                    if (!changed)
                    {
                        return x;
                    }

                    i = 0;
                    var args = new Term[x.Args.Length];
                    foreach (var t in ch)
                    {
                        args[i++] = t;
                    }

                    return Index.MkApply(x.Symbol, args, out changed);
                });
        }
        
        /// <summary>
        /// Converts a term AST to a term, and expands symbolic constants as much as possible. 
        /// Returns null if there are errors.
        /// Should only be called after the set has been successfully compiled.
        /// </summary>
        public Term Expand(AST<Node> ast, List<Flag> flags)
        {
            bool gotLock = false;
            try
            {
                termIndexLock.Enter(ref gotLock);

                var success = new SuccessToken();
                var symbStack = new Stack<Tuple<Namespace, Symbol>>();
                symbStack.Push(new Tuple<Namespace, Symbol>(Index.SymbolTable.Root, null));
                var result = ast.Compute<Tuple<Term, Term>>(
                    x => Expand_Unfold(x, symbStack, success, flags),
                    (x, y) => Expand_Fold(x, y, symbStack, success, flags));
                return result == null ? null : result.Item1;
            }
            finally
            {
                if (gotLock)
                {
                    termIndexLock.Exit();
                }
            }
        }

        /// <summary>
        /// Converts a model to an object graph.
        /// Should only be called if the model has been successfully validated.
        /// </summary>
        public void MkObjectGraph(
            ObjectGraphResult result, 
            Dictionary<string, Func<ICSharpTerm[], ICSharpTerm>> conMap,
            Func<Rational, ICSharpTerm> ratCon,
            Func<string, ICSharpTerm> strCon)
        {
            var termToAlias = new Map<Term, string>(Term.Compare);
            foreach (var kv in aliasDataMap)
            {
                termToAlias[kv.Value.ExpDefinition] = kv.Key.Name.Substring(1);
            }

            ICSharpTerm cs;
            foreach (var t in facts)
            {
                if ((cs = MkObjectGraph(t, result, termToAlias, conMap, ratCon, strCon)) != null)
                {
                    result.Objects.Add(cs);
                }
            }
        }

        public ICSharpTerm MkObjectGraph(
            Term t,
            ObjectGraphResult result, 
            Map<Term, string> termToAlias,
            Dictionary<string, Func<ICSharpTerm[], ICSharpTerm>> conMap,
            Func<Rational, ICSharpTerm> ratCon,
            Func<string, ICSharpTerm> strCon)
        {
            int i;
            string alias;
            bool hasAlias;
            ICSharpTerm csTerm;
            BaseCnstSymb bc;
            UserSymbol us;

            return t.Compute<ICSharpTerm>(
                (x, s) =>
                {
                    if (x.Symbol.Arity == 0)
                    {
                        return null;
                    }
                    else if (termToAlias.TryFindValue(x, out alias) && result.Aliases.TryGetValue(alias, out csTerm))
                    {
                        return null;
                    }
                    else
                    {
                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    try
                    {
                        if (x.Symbol.Kind == SymbolKind.BaseCnstSymb)
                        {
                            bc = (BaseCnstSymb)x.Symbol;
                            switch (bc.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    return ratCon((Rational)bc.Raw);
                                case CnstKind.String:
                                    return strCon((string)bc.Raw);
                                default:
                                    throw new NotImplementedException();
                            }
                        }

                        us = (UserSymbol)x.Symbol;
                        if (us.Arity == 0)
                        {
                            return conMap[us.FullName](EmptyCSharpTerm);
                        }
                        else if ((hasAlias = termToAlias.TryFindValue(x, out alias)) && result.Aliases.TryGetValue(alias, out csTerm))
                        {
                            return csTerm;
                        }

                        var args = new ICSharpTerm[us.Arity];
                        i = 0;
                        foreach (var c in ch)
                        {
                            args[i++] = c;
                        }

                        csTerm = conMap[us.FullName](args);
                        if (hasAlias)
                        {
                            result.Aliases.Add(alias, csTerm);
                        }

                        return csTerm;
                    }
                    catch (Exception e)
                    {
                        result.AddFlag(new Flag(
                            SeverityKind.Error,
                            default(Span),
                            Constants.ObjectGraphException.ToString(e.Message),
                            Constants.ObjectGraphException.Code));
                        s.Failed();
                        return null;
                    }
                },
                new SuccessToken());
        }

        private void ImportFactSet(FactSet fs, string renaming)
        {
            UserSymbol imported;
            AliasData aliasData;
            foreach (var kv in fs.aliasDataMap)
            {
                imported = Index.SymbolTable.Resolve(kv.Key, renaming);
                Contract.Assert(imported != null);
                if (aliasDataMap.ContainsKey(imported))
                {
                    continue;
                }

                aliasData = new AliasData(imported, kv.Value.DefNode);
                aliasData.ImportDefinition(Index.MkClone(kv.Value.ExpDefinition, renaming));
                aliasDataMap.Add(imported, aliasData);
            }

            foreach (var t in fs.facts)
            {
                facts.Add(Index.MkClone(t, renaming));
            }
        }

        /// <summary>
        /// Adds a fact to an model using a builder.
        /// </summary>
        private void AddFact(Builder bldr, BuilderRef modelRef, string alias, Term t, int line)               
        {
            var span = new Span(line, 0, line, 0);
            BaseCnstSymb bc;
            UserSymbol us;
            int i;

            if (!string.IsNullOrEmpty(alias))
            {
                bldr.PushId(alias, span);
            }

            t.Compute<Unit>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    switch (x.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            bc = (BaseCnstSymb)x.Symbol;
                            switch (bc.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    bldr.PushCnst((Rational)bc.Raw, span);
                                    break;
                                case CnstKind.String:
                                    bldr.PushCnst((string)bc.Raw, span);
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            break;
                        case SymbolKind.UserCnstSymb:
                            us = (UserSymbol)x.Symbol;
                            bldr.PushId(us.Name, span);
                            break;
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            us = (UserSymbol)x.Symbol;
                            bldr.PushId(us.Name, span);
                            bldr.PushFuncTerm();
                            i = 0;
                            while (i < us.Arity)
                            {
                                bldr.AddFuncTermArg();
                                ++i;
                            }

                            break;
                    }

                    return default(Unit);
                });

            if (alias == null)
            {
                bldr.PushAnonModelFact(span);
            }
            else
            {
                bldr.PushModelFact(span);
            }

           
            bldr.Load(modelRef);
            bldr.AddModelFact(true);
            bldr.Pop();
        }
        
        private IEnumerable<Node> CreateFact_Unfold(Node n,
                                                    Stack<Tuple<Namespace, Symbol>> symbStack,
                                                    SuccessToken success,
                                                    List<Flag> flags)
        {
            var space = symbStack.Peek().Item1;
            switch (n.NodeKind)
            {
                case NodeKind.Cnst:
                    {
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
                        if (id.Name.Contains('#'))
                        {
                            if (Resolve(id.Name, "constant", id, space, x => x.IsNewConstant, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                                return null;
                            }

                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else if (id.Fragments.Length == 1 &&
                                 Index.SymbolTable.Root.TryGetSymbol(id.Name, out symb) &&
                                 symb.IsNewConstant)
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            return null;
                        }
                        else if (id.Fragments.Length == 1 && id.Name == ASTSchema.Instance.DontCareName)
                        {
                            symb = (UserSymbol)id.CompilerData;
                            Contract.Assert(symb != null && symb.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symb).IsSymbolicConstant);
                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            return null;
                        }
                        else if (id.Fragments.Length == 1 && Index.SymbolTable.ModuleSpace.TryGetSymbol("%" + id.Name, out symb))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            return null;
                        }
                        else if (!Resolve(ToSmbCnstName(id), "constant", id, space, x => x.IsNonVarConstant, out symb, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                            return null;
                        }
                    }
                case NodeKind.FuncTerm:
                    {
                        var ft = (FuncTerm)n;
                        if (ft.Function is Id)
                        {
                            UserSymbol symb;
                            if (ValidateUse_UserFunc(ft, space, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                                return ft.Args;
                            }
                            else
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }
                        else
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                ft,
                                Constants.BadSyntax.ToString("Only data constructors can appear here."),
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

        private Term CreateFact_Fold(
                   Node n,                   
                   IEnumerable<Term> args,
                   Stack<Tuple<Namespace, Symbol>> symbStack,
                   Set<UserSymbol> aliases,
                   SuccessToken success,
                   List<Flag> flags)
        {
            bool wasAdded;
            var space = symbStack.Peek().Item1;
            var symb = symbStack.Pop().Item2;           
            if (symb == null)
            {
                return null;
            }
            if (symb.IsNonVarConstant)
            {
                if (symb.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symb).IsSymbolicConstant)
                {
                    if (aliases != null)
                    {
                        aliases.Add(GetData((UserSymbol)symb, n).SmbCnst);
                    }
                    else
                    {
                        //// Ensure there is aliases data for every alias.
                        GetData((UserSymbol)symb, n);
                    }
                }

                return Index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
            }
            else if (symb.IsDataConstructor)
            {
                var con = (UserSymbol)symb;
                var targs = new Term[con.Arity];
                var i = 0;
                var typed = true;
                foreach (var a in args)
                {
                    if (a == null)
                    {
                        //// If an arg is null, then it already has errors, 
                        //// so skip it an check the rest.
                        typed = false;
                        continue;
                    }

                    targs[i] = a;
                    if (a.Symbol.IsNonVarConstant)
                    {
                        if (a.Symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)a.Symbol).IsSymbolicConstant)
                        {
                            if (!GetData((UserSymbol)a.Symbol, n).TryRefineType(Index.GetCanonicalTerm(con, i)))
                            {
                                flags.Add(MkBadArgType(n, symb, i));
                                typed = false;
                            }
                        }
                        else
                        {
                            if (!con.CanonicalForm[i].AcceptsConstant(a.Symbol))
                            {
                                flags.Add(MkBadArgType(n, symb, i));
                                typed = false;
                            }
                        }
                    }
                    else if (a.Symbol.IsDataConstructor)
                    {
                        var usrSort = a.Symbol.Kind == SymbolKind.ConSymb
                                        ? ((ConSymb)a.Symbol).SortSymbol
                                        : ((MapSymb)a.Symbol).SortSymbol;
                        if (!con.CanonicalForm[i].Contains(usrSort))
                        {
                            flags.Add(MkBadArgType(n, symb, i));
                            typed = false;
                        }
                    }
                    else 
                    {
                        throw new NotImplementedException();
                    }

                    ++i;
                }

                if (!typed)
                {
                    success.Failed();
                    return null;
                }

                return Index.MkApply(con, targs, out wasAdded); 
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private IEnumerable<Node> Expand_Unfold(Node n,
                                                Stack<Tuple<Namespace, Symbol>> symbStack,
                                                SuccessToken success,
                                                List<Flag> flags)
        {
            var space = symbStack.Peek().Item1;
            switch (n.NodeKind)
            {
                case NodeKind.Cnst:
                    {
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
                        else if (symb.IsVariable)
                        {
                            var flag = new Flag(
                                SeverityKind.Error,
                                id,
                                Constants.BadSyntax.ToString("Variables cannot appear here."),
                                Constants.BadSyntax.Code);
                            flags.Add(flag);
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
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
                            if (ValidateUse_UserFunc(ft, space, out symb, flags, true))
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
                            var flag = new Flag(
                                SeverityKind.Error,
                                ft,
                                Constants.BadSyntax.ToString("Only data constructors can appear here."),
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

        private Tuple<Term, Term> Expand_Fold(
                   Node n,
                   IEnumerable<Tuple<Term, Term>> args,
                   Stack<Tuple<Namespace, Symbol>> symbStack,
                   SuccessToken success,
                   List<Flag> flags)
        {
            bool wasAdded;
            var space = symbStack.Peek().Item1;
            var symb = symbStack.Pop().Item2;

            if (symb == null)
            {
                return null;
            }
            if (symb.IsNonVarConstant)
            {
                var cnst = symb as UserCnstSymb;
                if (cnst != null && cnst.IsSymbolicConstant)
                {
                    var expDef = aliasDataMap[cnst].ExpDefinition;
                    Contract.Assert(expDef != null);
                    return new Tuple<Term, Term>(expDef, Index.MkDataWidenedType(expDef));
                }
                else
                {
                    var valTerm = Index.MkApply(symb, TermIndex.EmptyArgs, out wasAdded);
                    return new Tuple<Term, Term>(valTerm, valTerm);
                }
            }
            else if (symb.IsDataConstructor)
            {
                var con = (UserSymbol)symb;
                var sort = symb.Kind == SymbolKind.ConSymb ? ((ConSymb)con).SortSymbol : ((MapSymb)con).SortSymbol;

                var i = 0;
                var vargs = new Term[con.Arity];
                var typed = true;
                foreach (var a in args)
                {
                    if (a == null)
                    {
                        //// If an arg is null, then it already has errors, 
                        //// so skip it an check the rest.
                        typed = false;
                        continue;
                    }
                    else if (a.Item2.Symbol.IsNonVarConstant)
                    {
                        if (!sort.DataSymbol.CanonicalForm[i].AcceptsConstant(a.Item2.Symbol))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadArgType.ToString(i + 1, sort.DataSymbol.FullName),
                                Constants.BadArgType.Code));
                            success.Failed();
                            typed = false;
                            continue;
                        }
                    }
                    else if (a.Item2.Symbol.Kind == SymbolKind.UserSortSymb)
                    {
                        if (!sort.DataSymbol.CanonicalForm[i].Contains(a.Item2.Symbol))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadArgType.ToString(i + 1, sort.DataSymbol.FullName),
                                Constants.BadArgType.Code));
                            success.Failed();
                            typed = false;
                            continue;
                        }
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    vargs[i] = a.Item1;
                    ++i;
                }

                if (!typed)
                {
                    success.Failed();
                    return null;
                }

                return new Tuple<Term, Term>(
                            Index.MkApply(con, vargs, out wasAdded),
                            Index.MkApply(sort, TermIndex.EmptyArgs, out wasAdded));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns true if container contains the definitions of containee (possibly under a renaming).
        /// </summary>
        private bool Contains(ModRef container, ModRef containee, string renaming)
        {
            ModuleData cne;
            if (!(containee.CompilerData is Location) ||
                (cne = ((Location)containee.CompilerData).AST.Node.CompilerData as ModuleData) == null ||
                cne.Reduced == null)
            {
                return false;
            }

            Contract.Assert(cne.Reduced.Node.NodeKind == NodeKind.Domain);
            return Contains(container, (Domain)cne.Reduced.Node, renaming);
        }

        private bool Contains(ModRef container, Domain containee, string renaming)
        {
            ModuleData cnr;
            if (!(container.CompilerData is Location) ||
                (cnr = ((Location)container.CompilerData).AST.Node.CompilerData as ModuleData) == null ||
                cnr.Reduced == null)
            {
                return false;
            }

            Contract.Assert(cnr.Reduced.Node.NodeKind == NodeKind.Domain);
            var dcnr = (Domain)cnr.Reduced.Node;

            if (dcnr == containee)
            {
                return string.IsNullOrEmpty(renaming);
            }

            foreach (var mr in dcnr.Compositions)
            {
                if (string.CompareOrdinal(renaming == null ? string.Empty : renaming, 
                                          mr.Rename == null ? string.Empty : mr.Rename) == 0)
                {
                    return Contains(mr, containee, string.Empty);
                }
            }

            return false;
        }

        /// <summary>
        /// Expands a term. Only use after validation succeeds,
        /// </summary>
        private Term ExpandTerm(Term t)
        {
            int i;
            bool wa;
            bool rewritten;
            bool isSymbCnst;
            return t.Compute<Term>(
                (x, s) => x.Args,
                (x, ch, s) =>
                {
                    isSymbCnst = x.Groundness == Groundness.Ground &&
                                 x.Symbol.Kind == SymbolKind.UserCnstSymb &&
                                 ((UserCnstSymb)x.Symbol).IsSymbolicConstant;

                    if (isSymbCnst)
                    {
                        return aliasDataMap[(UserSymbol)x.Symbol].ExpDefinition;
                    }
                    else if (x.Args.Length == 0)
                    {
                        return x;
                    }

                    i = 0;
                    rewritten = false;
                    foreach (var tp in ch)
                    {
                        if (tp != x.Args[i++])
                        {
                            rewritten = true;
                            break;
                        }
                    }

                    if (!rewritten)
                    {
                        return x;
                    }

                    i = 0;
                    var args = new Term[x.Symbol.Arity];
                    foreach (var tp in ch)
                    {
                        args[i++] = tp;
                    }

                    return Index.MkApply(x.Symbol, args, out wa);
                });
        }

        private AliasData GetData(UserSymbol aliasSymb, Node occurrence)
        {
            Contract.Requires(aliasSymb != null && aliasSymb.Kind == SymbolKind.UserCnstSymb);
            Contract.Requires(((UserCnstSymb)aliasSymb).IsSymbolicConstant);

            AliasData data;
            if (aliasDataMap.TryFindValue(aliasSymb, out data))
            {
                return data;
            }

            data = new AliasData(aliasSymb, occurrence);
            aliasDataMap.Add(aliasSymb, data);
            return data;
        }

        private bool ValidateUse_UserFunc(FuncTerm ft, Namespace space, out UserSymbol symbol, List<Flag> flags, bool allowDerived = false)
        {
            Contract.Assert(ft.Function is Id);
            var result = true;
            var id = (Id)ft.Function;

            if (!Resolve(id.Name, "constructor", id, space, x => x.IsDataConstructor, out symbol, flags))
            {
                return false;
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

            if (symbol.Kind == SymbolKind.ConSymb && !allowDerived && !((ConSymb)symbol).IsNew)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    ft,
                    Constants.ModelNewnessError.ToString(),
                    Constants.ModelNewnessError.Code));
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

        private static Flag MkBadArgType(Node n, Symbol symb, int index)
        {
            return new Flag(
                SeverityKind.Error,
                n,
                Constants.BadArgType.ToString(index + 1, symb.PrintableName),
                Constants.BadArgType.Code);
        }

        private static string ToSmbCnstName(Id id)
        {
            if (id.Fragments.Length == 1)
            {
                return "%" + id.Name;
            }

            var smbCnst = id.Fragments[0]; 
            for (int i = 1; i < id.Fragments.Length - 1; ++i)
            {
                smbCnst += "." + id.Fragments[i];
            }

            return smbCnst + ".%" + id.Fragments[id.Fragments.Length - 1];
        }

        private bool Resolve(
                        string id,
                        string kind,
                        Node n,
                        Namespace space,
                        Predicate<UserSymbol> validator,
                        out UserSymbol symbol,
                        List<Flag> flags)
        {
            UserSymbol other = null;

            symbol = Index.SymbolTable.Resolve(id, out other, space);
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

        private bool ValidateOrientation(List<Flag> flags, CancellationToken cancel)
        {
            bool isOriented;
            bool result = true;
            AliasData vdata;
            Flag flag;
            int checkCancel = 1;

            foreach (var kv in aliasDataMap)
            {
                if (checkCancel % CancelCheckFreq == 0)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return false;
                    }

                    checkCancel = 1;
                }
                else
                {
                    ++checkCancel;
                }

                if (kv.Value.IsOriented != LiftedBool.Unknown)
                {
                    continue;
                }
                else if (kv.Value.DefAliases == null)
                {
                    kv.Value.IsOriented = LiftedBool.True;
                    //// facts.Add(kv.Value.SetExpDefinition(Index, aliasDataMap));
                    kv.Value.SetExpDefinition(Index, aliasDataMap);
                    if (!Model.Node.IsPartial && kv.Value.Definition == null)
                    {
                        flag = new Flag(
                            SeverityKind.Error,
                            kv.Value.DefNode,
                            Constants.ModelGroundingError.ToString(kv.Value.SmbCnst.PrintableName),
                            Constants.ModelGroundingError.Code);
                        flags.Add(flag);
                        result = false;
                    }
                    continue;
                }

                kv.Value.IsOriented = false;
                Tuple<AliasData, IEnumerator<AliasData>> top = null;
                var dfsStack = new Stack<Tuple<AliasData, IEnumerator<AliasData>>>();
                dfsStack.Push(new Tuple<AliasData, IEnumerator<AliasData>>(kv.Value, kv.Value.DefAliases.GetEnumerator()));
                while (dfsStack.Count > 0)
                {
                    top = dfsStack.Peek();
                    if (top.Item2.MoveNext())
                    {
                        vdata = top.Item2.Current;
                        if (vdata.IsOriented == LiftedBool.Unknown && vdata.DefAliases != null)
                        {
                            vdata.IsOriented = false;
                            dfsStack.Push(new Tuple<AliasData, IEnumerator<AliasData>>(vdata, vdata.DefAliases.GetEnumerator()));
                        }
                        else if (vdata.IsOriented == LiftedBool.Unknown && vdata.DefAliases == null)
                        {
                            vdata.IsOriented = true;
                            //// facts.Add(vdata.SetExpDefinition(Index, aliasDataMap));
                            vdata.SetExpDefinition(Index, aliasDataMap);
                            if (!Model.Node.IsPartial && vdata.Definition == null)
                            {
                                flag = new Flag(
                                    SeverityKind.Error,
                                    vdata.DefNode,
                                    Constants.ModelGroundingError.ToString(vdata.SmbCnst.PrintableName),
                                    Constants.ModelGroundingError.Code);
                                flags.Add(flag);
                                result = false;
                            }
                        }
                    }
                    else
                    {
                        isOriented = true;
                        foreach (var dep in top.Item1.DefAliases)
                        {
                            if (dep.IsOriented != LiftedBool.True)
                            {
                                isOriented = false;
                                break;
                            }
                        }

                        top.Item1.IsOriented = isOriented;

                        if (isOriented == LiftedBool.True)
                        {
                            facts.Add(top.Item1.SetExpDefinition(Index, aliasDataMap));
                        }
                        else 
                        {
                            flag = new Flag(
                                SeverityKind.Error,
                                top.Item1.DefNode,
                                Constants.ModelCyclicDefError.ToString(top.Item1.SmbCnst.PrintableName),
                                Constants.ModelCyclicDefError.Code);
                            flags.Add(flag);
                            result = false;
                        }

                        dfsStack.Pop();
                    }
                }           
            }

            return result;
        }

        private class AliasData
        {
            private static readonly Set<AliasData> NoAliases = new Set<AliasData>(AliasData.Compare);

            public UserSymbol SmbCnst
            {
                get;
                private set;
            }

            /// <summary>
            /// True if this alias is oriented. False if not. 
            /// Unknown if a decision hasn't been made.
            /// </summary>
            public LiftedBool IsOriented
            {
                get;
                set;
            }

            /// <summary>
            /// This is the type of the alias. It is null if no type constraints
            /// were placed on this alias. If the alias is bound in a model fact,
            /// then the type is a widened form of its definition.
            /// x is F(...) gets the type x : F.
            /// </summary>
            public Term Type
            {
                get;
                private set;
            }

            /// <summary>
            /// This is the term defining this alias. It may contain aliases.    
            /// Null if the alias is not bound in a model fact.
            /// </summary>
            public Term Definition
            {
                get;
                private set;
            }

            /// <summary>
            /// Non-null if the alias can be oriented. In this case, it is the definition
            /// where its dependent alias are substituted by their expanded definitions.
            /// </summary>
            public Term ExpDefinition
            {
                get;
                private set;
            }

            /// <summary>
            /// This is the set of aliases appearing in the definition.
            /// Null if the alias is not bound in a model fact.
            /// </summary>
            public Set<AliasData> DefAliases
            {
                get;
                private set;
            }

            /// <summary>
            /// The location where this alias is bound in a model fact.
            /// If the alias is never bound in a model fact, then it is some node
            /// where the alias occurs in the model.
            /// </summary>
            public Node DefNode
            {
                get;
                private set;
            }

            public AliasData(UserSymbol alias, Node occurrence)
            {
                SmbCnst = alias;
                IsOriented = LiftedBool.Unknown;
                DefNode = occurrence;
            }

            public bool TryDefine(ModelFact n, Term def, Set<UserSymbol> defAliases, Map<UserSymbol, AliasData> aliasMap, List<Flag> flags)
            {
                Contract.Assert(n != null && def != null && defAliases != null);
                bool result = true;
                if (Definition != null)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.DuplicateDefs.ToString(
                            string.Format("model alias {0}", n.Binding.Name),
                            DefNode.GetCodeLocationString(SmbCnst.Namespace.SymbolTable.Env.Parameters),
                            n.GetCodeLocationString(SmbCnst.Namespace.SymbolTable.Env.Parameters)),
                        Constants.DuplicateDefs.Code);
                    flags.Add(flag);
                    result = false;
                }

                DefNode = n;
                DefAliases = new Set<AliasData>(Compare);
                Definition = def;

                foreach (var v in defAliases)
                {
                    DefAliases.Add(aliasMap[v]);
                }

                if (!def.Symbol.IsDataConstructor)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadSyntax.ToString("Alias must be bound to a data term."),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    return false;
                }

                bool wasAdded;
                Term type;
                switch (def.Symbol.Kind)
                {
                    case SymbolKind.ConSymb:
                        type = def.Owner.MkApply(((ConSymb)def.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    case SymbolKind.MapSymb:
                        type = def.Owner.MkApply(((MapSymb)def.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                if (!TryRefineType(type))
                {
                    var flag = new Flag(
                                SeverityKind.Error,
                                n,
                                Constants.BadConstraint.ToString(),
                                Constants.BadConstraint.Code);
                    flags.Add(flag);
                    result = false;
                }

                return result;
            }

            public bool TryRefineType(Term type)
            {
                if (Type == null)
                {
                    Type = type;
                }

                Term tintr;
                if (!type.Owner.MkIntersection(Type, type, out tintr))
                {
                    return false;
                }

                Type = tintr;
                return true;
            }

            /// <summary>
            /// Sets the imported definition
            /// </summary>
            public void ImportDefinition(Term t)
            {
                Contract.Requires(t != null);
                if (ExpDefinition != null)
                {
                    return;
                }

                ExpDefinition = t;
                IsOriented = true;
                DefAliases = NoAliases;
            }

            /// <summary>
            /// Sets the expanded definition of this variable, and returns it.
            /// </summary>
            public Term SetExpDefinition(TermIndex index, Map<UserSymbol, AliasData> aliasMap)
            {
                Contract.Requires(IsOriented == LiftedBool.True && ExpDefinition == null);

                bool wasAdded;
                if (Definition == null)
                {
                    return ExpDefinition = index.MkApply(SmbCnst, TermIndex.EmptyArgs, out wasAdded);
                }

                int i;
                bool rewritten;
                bool isSymbCnst;
                return ExpDefinition = Definition.Compute<Term>(
                    (x, s) => x.Args,
                    (x, ch, s) =>
                    {
                        isSymbCnst = x.Groundness == Groundness.Ground &&
                                     x.Symbol.Kind == SymbolKind.UserCnstSymb &&
                                     ((UserCnstSymb)x.Symbol).IsSymbolicConstant;

                        if (isSymbCnst)
                        {
                            return aliasMap[(UserSymbol)x.Symbol].ExpDefinition;
                        }
                        else if (x.Args.Length == 0)
                        {
                            return x;
                        }

                        i = 0;
                        rewritten = false;
                        foreach (var t in ch)
                        {
                            if (t != x.Args[i++])
                            {
                                rewritten = true;
                                break;
                            }
                        }

                        if (!rewritten)
                        {
                            return x;
                        }

                        i = 0;
                        var args = new Term[x.Symbol.Arity];
                        foreach (var t in ch)
                        {
                            args[i++] = t;
                        }

                        return index.MkApply(x.Symbol, args, out wasAdded);
                    });
            }

            private static int Compare(AliasData v1, AliasData v2)
            {
                return Symbol.Compare(v1.SmbCnst, v2.SmbCnst);
            }
        }
    }
}
