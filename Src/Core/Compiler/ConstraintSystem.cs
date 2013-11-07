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

    internal enum FreshVarKind { DontCare, Selector, Comprehension }
    internal class ConstraintSystem
    {
        private static readonly Term[] EmptyArgs = new Term[0];

        /// <summary>
        /// Cached symbols / types that will be used frequently
        /// </summary>
        private Term[] anyType;

        /// <summary>
        /// The next Ids for a unique variable.
        /// </summary>
        private int[] nextVarIds = null;

        /// <summary>
        /// A map from terms to congruence classes.
        /// </summary>
        private Map<Term, CongruenceClass> classes = 
            new Map<Term, CongruenceClass>(Term.Compare);

        /// <summary>
        /// A map from variables, which stand for comprehensions,
        /// to the comprehension data.
        /// </summary>
        private Map<Term, ComprehensionData> comprehensions =
            new Map<Term, ComprehensionData>(Term.Compare);

        /// <summary>
        /// A map from find variables to their associated nodes
        /// </summary>
        private Map<Term, Node> finds = 
            new Map<Term, Node>(Term.Compare);

        /// <summary>
        /// The currently active equality stack that can be used
        /// to pend implied equalities. Can be null.
        /// </summary>
        private Stack<ImpliedEquality> activeEqStack = null;

        /// <summary>
        /// Contains information for orienting and compiling constraints.
        /// </summary>
        private ConstraintCompiler cnstrCompiler;

        /// <summary>
        /// Contains information used for the occurs check.
        /// </summary>
        private Unifier unifier;

        public TermIndex Index
        {
            get;
            private set;
        }

        public Body Body
        {
            get;
            private set;
        }

        /// <summary>
        /// The type environment associated with this constraint body
        /// </summary>
        public TypeEnvironment TypeEnvironment
        {
            get;
            private set;
        }

        /// <summary>
        /// The comprehension data for this constraint system.
        /// If this body is not a comprehension, then it is null.
        /// </summary>
        public ComprehensionData Comprehension
        {
            get;
            private set;
        }

        /// <summary>
        /// Enumerates all the variables appearing in the constraint systems
        /// </summary>
        public IEnumerable<Term> Variables
        {
            get
            {
                foreach (var t in classes.Keys)
                {
                    if (t.Symbol.IsVariable)
                    {
                        yield return t;
                    }
                }
            }
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

        public ConstraintSystem(
            TermIndex index, 
            Body body, 
            TypeEnvironment tenv, 
            ComprehensionData comprData)
        {
            Contract.Requires(index != null);
            Contract.Requires(body != null);

            Index = index;
            Body = body;
            Comprehension = comprData;
            TypeEnvironment = tenv;
            IsCompiled = LiftedBool.Unknown;

            var table = index.SymbolTable;
            anyType = new Term[]{ Index.CanonicalAnyType };
            unifier = new Unifier(this);
            cnstrCompiler = new ConstraintCompiler(this);
        }

        /// <summary>
        /// Returns true if the type of v is defined directly in this scope
        /// </summary>
        public bool TryGetType(Term t, out Term type)
        {
            CongruenceClass c;
            if (!classes.TryFindValue(t, out c))
            {
                //// Check of this variable was introduced later by
                //// the constraint compiler.
                return cnstrCompiler.TryGetAuxType(t, out type);
            }

            type = c.Type;
            return true;
        }

        public bool Validate(
                    List<Flag> flags, 
                    IEnumerable<Id> headVars,
                    CancellationToken cancel)
        {
            //// Step 0. Initialize var counters.
            if (Comprehension == null)
            {
                nextVarIds = new int[] { 0, 0, 0 };
            }
            else
            {
                nextVarIds = new int[3];
                nextVarIds[(int)FreshVarKind.Comprehension] = Comprehension.Owner.nextVarIds[(int)FreshVarKind.Comprehension];
                nextVarIds[(int)FreshVarKind.DontCare] = Comprehension.Owner.nextVarIds[(int)FreshVarKind.DontCare];
                nextVarIds[(int)FreshVarKind.Selector] = Comprehension.Owner.nextVarIds[(int)FreshVarKind.Selector];
            }

            //// Step 1: create congruence classes, including
            //// those for head vars
            var constraints = new List<Constraint>();
            var symbStack = new Stack<Tuple<Namespace, Symbol>>();
            var tup = new Tuple<Namespace, Symbol>(Index.SymbolTable.Root, null);
            var success = new SuccessToken();
            var noChildren = new CongruenceClass[0];
            foreach (var h in headVars)
            {
                symbStack.Clear();
                symbStack.Push(tup);
                CreateClasses_Unfold(h, symbStack, success, constraints, flags);
                CreateClasses_Fold(h, noChildren, symbStack, constraints, success, flags);
            }

            if (!CreateClasses(flags, constraints, cancel) || 
                !success.Result ||
                cancel.IsCancellationRequested)
            {
                return RecordValidationResult(false);
            }

            //// Step 2: apply equalities:
            var implEqs = new Stack<ImpliedEquality>();
            activeEqStack = implEqs;
            //// First, there may be classes whose types imply equalities
            //// but have not had a chance to do so.
            var roots = new LinkedList<CongruenceClass>();
            foreach (var c in classes.Values)
            {
                if (c.IsRoot)
                {
                    roots.AddLast(c);
                }
            }

            foreach (var c in roots)
            {
                NotifyTypeChange(c);
            }

            if (!ProcessEqualities(implEqs, flags))
            {
                return RecordValidationResult(false);
            }

            foreach (var con in constraints)
            {
                if (con.Kind != RelKind.Eq)
                {
                    continue;
                }

                implEqs.Push(new ImpliedEquality(con.Arg1, con.Arg2, true));
                if (!ProcessEqualities(implEqs, flags, con.Node))
                {
                    return RecordValidationResult(false);
                }
            }

            activeEqStack = null;
            //// Step 3. Check that relational constraints are satisfiable
            foreach (var con in constraints)
            {
                if (con.Kind == RelKind.Eq)
                {
                    continue;
                }
                else if (con.Kind == RelKind.Neq &&
                         con.Arg1.Find() == con.Arg2.Find())
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        con.Node,
                        Constants.BadConstraint.ToString(),
                        Constants.BadConstraint.Code));
                    return RecordValidationResult(false);
                }
                else if (con.Kind != RelKind.Typ && con.Kind != RelKind.No)
                {
                    Term tintr;
                    var approx = Index.SymbolTable.GetOpSymbol(con.Kind).UpwardApprox(
                        Index,
                        new Term[] { con.Arg1.Find().Type, con.Arg2.Find().Type });
                    if (approx == null || !Index.MkIntersection(approx[0], Index.TrueValue, out tintr))
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            con.Node,
                            Constants.BadConstraint.ToString(),
                            Constants.BadConstraint.Code));
                        return RecordValidationResult(false);
                    }
               }
            }

            //// Step 4. Recursively validate comprehensions.
            foreach (var kv in comprehensions)
            {
                if (!kv.Value.Validate(flags, cancel))
                {
                    success.Failed();
                }
            }

            if (!ValidateOrientation(flags))
            {
                success.Failed();
            }

            return RecordValidationResult(success.Result);
        }

        /// <summary>
        /// Compiles the constraint system.
        /// </summary>
        public bool Compile(RuleTable rules, out FindData[] parts, List<Flag> flags, CancellationToken cancel)
        {
            return cnstrCompiler.Compile(rules, out parts, flags, cancel);
        }

        /// <summary>
        /// Returns the largest next var id registered for this constraint system or any of its children.
        /// </summary>
        public int GetNextVarId(FreshVarKind kind)
        {
            var nextVarId = nextVarIds[(int)kind];
            foreach (var kv in comprehensions)
            {
                nextVarId = Math.Max(nextVarId, kv.Value.GetNextVarId(kind));
            }

            return nextVarId;
        }

        public void Debug_ClassPrintTypes()
        {
            using (var sw = new System.IO.StringWriter())
            {
                sw.WriteLine("Types at body {0}", Body.GetCodeLocationString(Index.Env.Parameters));
                var printed = new Set<CongruenceClass>((x, y) => Term.Compare(x.Representative, y.Representative));
                foreach (var cls in classes)
                {
                    var clsF = cls.Value.Find();
                    if (printed.Contains(clsF))
                    {
                        continue;
                    }

                    printed.Add(clsF);
                    sw.Write("{ ");
                    foreach (var m in clsF.Members)
                    {
                        TermIndex.Debug_PrintSmallTerm(m, sw);
                        sw.Write(" ");
                    }

                    sw.Write("} : ");
                    TermIndex.Debug_PrintSmallTerm(clsF.Type, sw);
                    sw.WriteLine();
                }

                Console.WriteLine(sw);
            }
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
        /// Tries to introduce equalities imported from a parent scope (after validation has occurred).
        /// Does not fully recompute congruence closure.
        /// </summary>
        private bool ImportEqualities(Map<Term, Node> equalities, List<Flag> flags, CancellationToken cancel)
        {
            bool result = true;
            foreach (var kv in equalities)
            {
                var success = new SuccessToken();
                cnstrCompiler.AddConstraint(kv.Key);
                kv.Key.Compute<CongruenceClass>(
                    (x, s) => x.Args,
                    (x, ch, s) => x == kv.Key ? null : CreateClasses_Fold(x, ch, kv.Value, s, flags),
                    success);
                result = result && success.Result;
            }

            if (!result || cancel.IsCancellationRequested)
            {
                return false;
            }

            //// Step 2: apply equalities:
            var implEqs = new Stack<ImpliedEquality>();
            activeEqStack = implEqs;
            //// First, there may be classes whose types imply equalities
            //// but have not had a chance to do so.
            foreach (var c in classes.Values)
            {
                if (!c.IsRoot)
                {
                    continue;
                }

                NotifyTypeChange(c);
            }

            if (!ProcessEqualities(implEqs, flags))
            {
                return false;
            }

            CongruenceClass arg1, arg2;
            foreach (var kv in equalities)
            {
                arg1 = GetClass(kv.Key.Args[0], null, null);
                arg2 = GetClass(kv.Key.Args[1], null, null);
                implEqs.Push(new ImpliedEquality(arg1, arg2, true));
                if (!ProcessEqualities(implEqs, flags, kv.Value))
                {
                    return false;
                }
            }

            activeEqStack = null;
            return true;
        }

        private bool ProcessEqualities(Stack<ImpliedEquality> implEqs,
                                       List<Flag> flags,
                                       Node blame = null)
        {
            CongruenceClass c1, c2, cmerged;
            ImpliedEquality ieq;
            while (implEqs.Count > 0)
            {
                ieq = implEqs.Pop();
                c1 = ieq.LHS.Find();
                c2 = ieq.RHS.Find();
                if (c1 == c2)
                {
                    continue;
                }

                if (!CongruenceClass.Merge(this, c1, c2, ieq.NeedsOccursCheck, implEqs, out cmerged))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        blame == null ? c1.Node : blame,
                        Constants.BadConstraint.ToString(),
                        Constants.BadConstraint.Code));
                    return false;
                }
            }

            return true;
        }

        private void NotifyTypeChange(CongruenceClass c)
        {
            if (activeEqStack == null)
            {
                return;
            }

            CongruenceClass.DefineSelectors(this, c.Find(), activeEqStack);
            CongruenceClass.DefineGroundTypes(this, c.Find(), activeEqStack);
        }

        private bool CreateClasses(List<Flag> flags, List<Constraint> constraints, CancellationToken cancel)
        {
            var success = new SuccessToken();            
            foreach (var n in Body.Constraints)
            {
                var symbStack = new Stack<Tuple<Namespace, Symbol>>();
                symbStack.Push(new Tuple<Namespace, Symbol>(Index.SymbolTable.Root, null));
                Factory.Instance.ToAST(n).Compute<CongruenceClass>(
                    x => CreateClasses_Unfold(x, symbStack, success, constraints, flags),
                    (x, y) => CreateClasses_Fold(x, y, symbStack, constraints, success, flags),
                    cancel);
            }

            return success.Result;
        }

        private IEnumerable<Node> CreateClasses_Unfold(Node n, 
                                                       Stack<Tuple<Namespace, Symbol>> symbStack,
                                                       SuccessToken success,
                                                       List<Constraint> constraints,
                                                       List<Flag> flags)
        {          
            var space = symbStack.Peek().Item1;
            switch (n.NodeKind)
            {
                case NodeKind.Compr:
                    {
                        var cvVar = MkFreshVar(FreshVarKind.Comprehension);
                        comprehensions.Add(cvVar, new ComprehensionData(n, this, Comprehension == null ? 1 : Comprehension.Depth + 1));
                        symbStack.Push(new Tuple<Namespace, Symbol>(space, cvVar.Symbol));
                        return null;
                    }
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
                        int firstAccIndex;
                        UserSymbol symb;
                        if (Index.SymbolTable.HasRenamingPrefix(id))
                        {
                            if (!Resolve(ASTSchema.Instance.StripAccessors(id, true, out firstAccIndex), "constant", id, space, x => x.IsNonVarConstant, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }
                        else if (!Resolve(ASTSchema.Instance.StripAccessors(id, false, out firstAccIndex), "variable or constant", id, space, x => x.Kind == SymbolKind.UserCnstSymb, out symb, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }
                        else if (id.Fragments.Length > 1 && symb.IsNonVarConstant && !((UserCnstSymb)symb).IsSymbolicConstant)
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

                        if (firstAccIndex < id.Fragments.Length)
                        {
                            Set<UserSortSymb> labelOwners;
                            bool badLabel = false;
                            for (int i = firstAccIndex; i < id.Fragments.Length; ++i)
                            {
                                if (!Index.SymbolTable.InverseLabelLookup(id.Fragments[i], out labelOwners))
                                {
                                    badLabel = true;
                                    var flag = new Flag(
                                        SeverityKind.Error,
                                        id,
                                        Constants.BadSyntax.ToString(string.Format("no data type has an argument named {0}", id.Fragments[i])),
                                        Constants.BadSyntax.Code);
                                    flags.Add(flag);
                                }
                            }

                            if (badLabel)
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }
                        }

                        if (symb.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symb).IsSymbolicConstant)
                        {
                            symb = CreateSymbConst(id, (UserCnstSymb)symb, constraints);
                        }

                        symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                        return null;
                    }
                case NodeKind.RelConstr:
                    {
                        var rc = (RelConstr)n;
                        var symb = Index.SymbolTable.GetOpSymbol(rc.Op);
                        if (!symb.Validator(n, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }

                        if (rc.Op == RelKind.Typ)
                        {
                            UserSymbol symbp;
                            if (!Resolve(((Id)rc.Arg1).Name, "variable", rc.Arg1, space, x => x.IsVariable, out symbp, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            if (!Resolve(((Id)rc.Arg2).Name, "type id", rc.Arg2, space, x => x.Kind == SymbolKind.ConSymb || x.Kind == SymbolKind.MapSymb || x.Kind == SymbolKind.UnnSymb || x.Kind == SymbolKind.BaseSortSymb, out symbp, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            symbStack.Push(new Tuple<Namespace, Symbol>(space, symb));
                            return EnumerableMethods.GetEnumerable<Node>(rc.Arg1);
                        }

                        symbStack.Push(new Tuple<Namespace, Symbol>(space, symb));
                        if (rc.Op == RelKind.No)
                        {
                            return EnumerableMethods.GetEnumerable<Node>(rc.Arg1);
                        }
                        else
                        {
                            return EnumerableMethods.GetEnumerable<Node>(rc.Arg1, rc.Arg2);
                        }
                    }
                case NodeKind.FuncTerm:
                    {
                        var ft = (FuncTerm)n;
                        if (ft.Function is Id)
                        {
                            UserSymbol symb;
                            if (!ValidateUse_UserFunc(ft, space, out symb, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            symbStack.Push(new Tuple<Namespace, Symbol>(symb.Namespace, symb));
                        }
                        else if (ft.Function is OpKind)
                        {
                            var symb = Index.SymbolTable.GetOpSymbol((OpKind)ft.Function);
                            if (!symb.Validator(n, flags))
                            {
                                symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                                success.Failed();
                                return null;
                            }

                            symbStack.Push(new Tuple<Namespace, Symbol>(space, symb));
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        return ft.Args;
                    }
                case NodeKind.Find:
                    {
                        var fnd = (Find)n;
                        Node fndCnstr;
                        if (!ValidateUse_Find(fnd, space, out fndCnstr, flags))
                        {
                            symbStack.Push(new Tuple<Namespace, Symbol>(null, null));
                            success.Failed();
                            return null;
                        }

                        //// Find only pushes a symbol if it is a find on a derived constant.
                        symbStack.Push(new Tuple<Namespace, Symbol>(space, null));
                        return EnumerableMethods.GetEnumerable<Node>(fndCnstr);
                    }
                default:
                    throw new NotImplementedException();
            }       
        }

        private CongruenceClass CreateClasses_Fold(
            Node n, 
            IEnumerable<CongruenceClass> args, 
            Stack<Tuple<Namespace, Symbol>> symbStack, 
            List<Constraint> constraints,
            SuccessToken success, 
            List<Flag> flags)
        {
            bool wasAdded;
            var space = symbStack.Peek().Item1;
            var symb = symbStack.Pop().Item2;

            if (!success.Result)
            {
                return null;
            }
            else if (n.NodeKind == NodeKind.Compr)
            {
                Contract.Assert(symb.IsVariable);
                return GetClass(Index.MkApply(symb, EmptyArgs, out wasAdded), anyType[0], n);
            }
            else if (n.NodeKind == NodeKind.Find)
            {
                //// The find is over data and must be restricted to non-constants.
                var fndClss = args.First<CongruenceClass>();
                if (Index.CanonicalFindType == null || !fndClss.RefineType(this, Index.CanonicalFindType))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadConstraint.ToString(),
                        Constants.BadConstraint.Code));
                    success.Failed();
                }
                else if (Comprehension != null &&
                         Comprehension.IsParentVar(fndClss.Representative))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.FindHidesError.ToString(fndClss.Representative.Symbol.PrintableName),
                        Constants.FindHidesError.Code));
                    success.Failed();
                }

                Contract.Assert(fndClss.Representative.Symbol.IsVariable);
                if (!finds.ContainsKey(fndClss.Representative))
                {
                    finds.Add(fndClss.Representative, n);
                }
                else
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.DuplicateFindError.ToString(fndClss.Representative.Symbol.PrintableName),
                        Constants.DuplicateFindError.Code));
                    success.Failed();
                }

                return null;
            }
            else if (symb == Index.TypeRelSymbol)
            {
                UserSymbol other;
                Term typTrm = null;
                var typSymb = Index.SymbolTable.Resolve(((Id)((RelConstr)n).Arg2).Name, out other, space);
                switch (typSymb.Kind)
                {
                    case SymbolKind.ConSymb:
                        typTrm = Index.MkApply(((ConSymb)typSymb).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    case SymbolKind.MapSymb:
                        typTrm = Index.MkApply(((MapSymb)typSymb).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                        break;
                    case SymbolKind.UnnSymb:
                        typTrm = Index.GetCanonicalTerm(typSymb, 0);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                if (!args.First<CongruenceClass>().RefineType(this, typTrm))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadConstraint.ToString(),
                        Constants.BadConstraint.Code));
                    success.Failed();
                }
                else
                {
                    cnstrCompiler.AddConstraint(Index.MkApply(
                        Index.TypeRelSymbol, 
                        new Term[] { args.First<CongruenceClass>().Representative, typTrm }, 
                        out wasAdded));
                }

                return args.First<CongruenceClass>();
            }

            Term[] argArr, targArr;
            if (!Apply(n, symb, args, out argArr, out targArr, success, flags))
            {
                return null;
            }

            if (n.NodeKind == NodeKind.RelConstr) 
            {
                cnstrCompiler.AddConstraint(Index.MkApply(symb, argArr, out wasAdded));
                using (var it = args.GetEnumerator())
                {
                    it.MoveNext();
                    var arg1 = it.Current;
                    it.MoveNext();
                    constraints.Add(new Constraint(n, arg1, it.Current));
                    return arg1;                         
                }
            }
            else if (symb.Kind == SymbolKind.UserCnstSymb)
            {
                var usrCnst = (UserCnstSymb)symb;
                switch (usrCnst.UserCnstKind)
                {
                    case UserCnstSymbKind.Variable:
                        return MkSelectorClasses(n, symb, success, flags);
                    case UserCnstSymbKind.New:
                    case UserCnstSymbKind.Derived:
                        return GetClass(
                            Index.MkApply(symb, EmptyArgs, out wasAdded), 
                            Index.MkApply(symb, EmptyArgs, out wasAdded), 
                            n);
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                Term appType;
                if (symb.Kind == SymbolKind.BaseOpSymb)
                {
                    var upApprox = ((BaseOpSymb)symb).UpwardApprox(Index, targArr);
                    if (upApprox == null)
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            n,
                            Constants.BadArgTypes.ToString(symb.PrintableName),
                            Constants.BadArgTypes.Code);
                        flags.Add(flag);
                        success.Failed();
                        return null;
                    }
                    else
                    {
                        appType = upApprox[0];
                    }
                }
                else
                {
                    appType = Index.MkApply(symb, targArr, out wasAdded);
                }

                var cls = GetClass(Index.MkApply(symb, argArr, out wasAdded), appType, n);
                foreach (var a in args)
                {
                    a.AddUse(cls);
                }

                if (symb.Kind == SymbolKind.BaseOpSymb)
                {
                    var bo = (BaseOpSymb)symb;
                    CongruenceClass subClsA, subClsB;
                    foreach (var constr in bo.AppConstrainer(Index, argArr))
                    {
                        Contract.Assert(constr.Item1 != RelKind.No && constr.Item1 != RelKind.Typ);

                        if ((subClsA = MkAppConstrTerm(constr.Item2, n, success, flags)) == null)
                        {
                            return null;
                        }

                        if ((subClsB = MkAppConstrTerm(constr.Item3, n, success, flags)) == null)
                        {
                            return null;
                        }

                        cnstrCompiler.AddConstraint(Index.MkApply(
                            Index.SymbolTable.GetOpSymbol(constr.Item1),
                            new Term[] { constr.Item2, constr.Item3 },
                            out wasAdded));
                        constraints.Add(new Constraint(constr.Item1, n, subClsA, subClsB));
                    }
                }
                
                return cls;
            }
        }

        /// <summary>
        /// Creates congruence classes for a non-constraint term t that is imported
        /// from a parent scope.
        /// </summary>
        private CongruenceClass CreateClasses_Fold(
            Term t,
            IEnumerable<CongruenceClass> args,
            Node blame,
            SuccessToken success,
            List<Flag> flags)
        {
            bool wasAdded;
            if (!success.Result)
            {
                return null;
            }

            var symb = t.Symbol;
            Term[] argArr, targArr;

            if (symb == Index.SelectorSymbol)
            {
                if (!ApplySelector(blame, args.First<CongruenceClass>(), t.Args[1], out argArr, out targArr, success, flags))
                {
                    return null;
                }
            }
            else if (!Apply(blame, symb, args, out argArr, out targArr, success, flags))
            {
                return null;
            }

            if (symb.Kind == SymbolKind.UserCnstSymb)
            {
                var usrCnst = (UserCnstSymb)symb;
                switch (usrCnst.UserCnstKind)
                {
                    case UserCnstSymbKind.Variable:
                        return GetClass(t, anyType[0], blame);
                    case UserCnstSymbKind.New:
                        return GetClass(
                            Index.MkApply(symb, EmptyArgs, out wasAdded),
                            Index.MkApply(symb, EmptyArgs, out wasAdded),
                            blame);
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                var appType = symb.Kind == SymbolKind.BaseOpSymb
                    ? ((BaseOpSymb)symb).UpwardApprox(Index, targArr)[0]
                    : Index.MkApply(symb, targArr, out wasAdded);

                var cls = GetClass(t, appType, blame);
                var rewriteCls = GetClass(Index.MkApply(symb, argArr, out wasAdded), appType, blame);
                foreach (var a in args)
                {
                    a.AddUse(cls);
                }

                //// In this case, the term t is equivalent to another term under the current
                //// congruence relation. Because no new equalities have been asserted, it should
                //// be safe to immediately merge these classes.
                if (cls != rewriteCls)
                {
                    var result = CongruenceClass.Merge(this, cls, rewriteCls, false, new Stack<ImpliedEquality>(), out cls);
                    Contract.Assert(result);
                }

                if (symb == Index.SelectorSymbol)
                {
                    unifier.TrackSelect(t);
                }

                return cls;
            }
        }

        /// <summary>
        /// Creates congruences classes for a side constraint introduced by an application.
        /// The side constraint should be shallow in the depth of novel terms.
        /// </summary>
        private CongruenceClass MkAppConstrTerm(Term t, Node blame, SuccessToken success, List<Flag> flags)
        {
            Contract.Requires(t.Symbol != t.Owner.SelectorSymbol);

            CongruenceClass cls;
            if (classes.TryFindValue(t, out cls))
            {
                return cls;
            }
            else if (t.Args.Length == 0)
            {
                return MkGrndClasses(t, blame);
            }

            var args = new CongruenceClass[t.Args.Length];
            for (int i = 0; i < t.Args.Length; ++i)
            {
                if ((args[i] = MkAppConstrTerm(t.Args[i], blame, success, flags)) == null)
                {
                    return null;
                }
            }

            var symb = t.Symbol;
            Term[] argArr, targArr;
            if (!Apply(blame, symb, args, out argArr, out targArr, success, flags))
            {
                return null;
            }

            Term appType;
            bool wasAdded; 
            if (symb.Kind == SymbolKind.BaseOpSymb)
            {
                var upApprox = ((BaseOpSymb)symb).UpwardApprox(Index, targArr);
                if (upApprox == null)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        blame,
                        Constants.BadArgTypes.ToString(symb.PrintableName),
                        Constants.BadArgTypes.Code);
                    flags.Add(flag);
                    success.Failed();
                    return null;
                }
                else
                {
                    appType = upApprox[0];
                }
            }
            else
            {
                appType = Index.MkApply(symb, targArr, out wasAdded);
            }

            cls = GetClass(Index.MkApply(symb, argArr, out wasAdded), appType, blame);
            foreach (var a in args)
            {
                a.AddUse(cls);
            }

            return cls;
        }

        /// <summary>
        /// For a variable x, possibly with selectors lbl1.lbl2..., creates all the congruence classes
        /// for x and its selectors. Returns the congruence class containing the final selector.
        /// </summary>
        private CongruenceClass MkSelectorClasses(
                                    Node n,
                                    Symbol varSymbol,
                                    SuccessToken success,
                                    List<Flag> flags)
        {
            bool wasAdded;
            var id = (Id)n;
            var varTerm = Index.MkApply(varSymbol, EmptyArgs, out wasAdded);
            var cls = GetClass(varTerm, anyType[0], n);

            Term parentType;
            if (Comprehension != null && Comprehension.RecordRead(varTerm, n, out parentType))
            {
                if (!cls.RefineType(this, parentType))
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadConstraint.ToString(),
                        Constants.BadConstraint.Code));
                    success.Failed();
                    return null;
                }

                cnstrCompiler.AddConstraint(Index.MkApply(Index.TypeRelSymbol, new Term[] { varTerm, parentType }, out wasAdded));
            }

            Term lbl, sel;
            CongruenceClass lblCls, nxtCls;
            Term[] argArr, targArr;

            int firstAcc;
            ASTSchema.Instance.StripAccessors(id, false, out firstAcc);
            for (int i = firstAcc; i < id.Fragments.Length; ++i)
            {
                cls.IsSelected = true;
                lbl = Index.MkCnst(id.Fragments[i], out wasAdded);
                lblCls = GetClass(lbl, lbl, id);
                if (!ApplySelector(n, cls, lbl, out argArr, out targArr, success, flags))
                {
                    return null;
                }

                nxtCls = GetClass(
                                   sel = Index.MkApply(Index.SelectorSymbol, argArr, out wasAdded), 
                                   Index.SelectorSymbol.UpwardApprox(Index, targArr)[0], 
                                   n);

                cls.AddUse(nxtCls);
                lblCls.AddUse(nxtCls);
                unifier.TrackSelect(sel);
                cls = nxtCls;
            }

            return cls;
        }

        private UserSymbol CreateSymbConst(
            Id id,
            UserCnstSymb symbCnst, 
            List<Constraint> constraints)
        {
            var scVar = Index.MkScVar(symbCnst.FullName, false);
            var scFnd = Index.MkScVar(symbCnst.FullName, true);
                      
            if (finds.ContainsKey(scFnd) || (Comprehension != null && Comprehension.IsParentVar(scVar)))
            {
                return (UserSymbol)scVar.Symbol;
            }

            bool wasAdded;
            var symbCnstTrm = Index.MkApply(symbCnst, TermIndex.EmptyArgs, out wasAdded);
            var symbCnstCls = GetClass(symbCnstTrm, symbCnstTrm, id);

            var scFndCls = GetClass(
                scFnd,
                Index.MkApply(Index.SCValueSymbol.SortSymbol, TermIndex.EmptyArgs, out wasAdded),
                id);

            var scVarCls = GetClass(scVar, Index.GetSymbCnstType(symbCnstTrm), id);

            var scValCls = GetClass(
                Index.MkApply(Index.SCValueSymbol, new Term[] { symbCnstTrm, scVar }, out wasAdded),
                Index.MkApply(Index.SCValueSymbol.SortSymbol, TermIndex.EmptyArgs, out wasAdded),
                id);

            constraints.Add(new Constraint(RelKind.Eq, id, scFndCls, scValCls));
            finds.Add(scFnd, id);
            return (UserSymbol)scVar.Symbol;
        }
                                   
        /// <summary>
        /// Tries to apply symb to args. In the process, constrains the
        /// types of args and fails if the application is impossible.
        /// Returns an array of the arguments and an array of types of the arguments.
        /// </summary>
        private bool Apply(
            Node n, 
            Symbol symb, 
            IEnumerable<CongruenceClass> args,
            out Term[] argArr,
            out Term[] targArr,
            SuccessToken success,
            List<Flag> flags)
        {
            Term tintr, targ;
            int i = 0;
            var usrSymb = symb is UserSymbol ? (UserSymbol)symb : null;
            argArr = symb.Arity == 0 ? EmptyArgs : new Term[symb.Arity];
            targArr = symb.Arity == 0 ? EmptyArgs : new Term[symb.Arity];
            var downApprox = symb.Kind == SymbolKind.BaseOpSymb ? ((BaseOpSymb)symb).DownwardApprox(Index, anyType) : null;
            bool succeeded = true;
            foreach (var a in args)
            {
                if (a == null)
                {
                    return false;
                }

                targ = symb.Kind == SymbolKind.BaseOpSymb ? downApprox[i] : Index.GetCanonicalTerm(usrSymb, i);
                if (!Index.MkIntersection(targ, a.Type, out tintr))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.BadArgType.ToString(i + 1, symb.PrintableName),
                        Constants.BadArgType.Code);
                    flags.Add(flag);
                    success.Failed();
                    succeeded = false;
                }
                else
                {
                    a.RefineType(this, tintr);
                    argArr[i] = a.Representative;
                    targArr[i] = a.Type;
                }

                ++i;
            }

            return succeeded;
        }

        /// <summary>
        /// A specialized version of Apply for selectors
        /// </summary>
        private bool ApplySelector(
            Node n,
            CongruenceClass selClass,
            Term label,
            out Term[] argArr,
            out Term[] targArr,
            SuccessToken success,
            List<Flag> flags)
        {
            Term tintr;
            argArr = new Term[2];
            targArr = new Term[2];
            var downApprox = Index.SelectorSymbol.DownwardApprox(Index, new Term[] { anyType[0], label });
            if (!Index.MkIntersection(downApprox[0], selClass.Type, out tintr))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.BadArgType.ToString(1, "." + (string)((BaseCnstSymb)label.Symbol).Raw),
                    Constants.BadArgType.Code);
                flags.Add(flag);
                success.Failed();
                return false;
            }

            //// This operation should succeed, because the parent type of this variable
            //// was already asserted and considered before reaching the compilation phase.
            bool result = selClass.RefineType(this, tintr);
            Contract.Assert(result);

            argArr[0] = selClass.Representative;
            targArr[0] = selClass.Type;
            argArr[1] = targArr[1] = label;
            return true;
        }

        /// <summary>
        /// Checks that constraints can be oriented.
        /// </summary>
        private bool ValidateOrientation(List<Flag> flags)
        {
            //// A ground term is oriented.
            //// A find variable is oriented.
            //// A variable read from a parent scope is oriented.
            //// A term f(t1,...,tn) is oriented if all t1 ... tn are oriented.
            //// A term t' is oriented if t = f(..., t', ...) is oriented and f is a data constructor.
            //// A term t is oriented if it is in the same congruence class as another oriented term.
            //// A comprehension var v is oriented if v depends on x1 ... xn and all xi are oriented.

            //// Step 1. Find all the classes that are initially oriented
            Set<Term> congVars;
            CongruenceClass cls, clsp;
            var stack = new Stack<CongruenceClass>();
            var oriented = new Set<CongruenceClass>(CongruenceClass.Compare);
            var congUses = new Map<CongruenceClass, Set<Term>>(CongruenceClass.Compare);

            //// Record the uselists implied by comprehension reads.
            //// Comprehensions with no parent reads are automatically oriented.
            Term type;
            bool hasReads;
            foreach (var kv in comprehensions)
            {
                hasReads = false;
                foreach (var v in kv.Value.ReadVars)
                {
                    //// In this case, a compr is reading a var from a parent
                    //// scope. It is registered as a read dependency for this scope.
                    if (!classes.TryFindValue(v.Key, out cls))
                    {
                        if (Comprehension != null)
                        {
                            Comprehension.RecordRead(v.Key, v.Value, out type);
                            GetClass(v.Key, type, v.Value);
                        }
                        else
                        {
                            throw new Impossible();
                        }

                        continue;
                    }

                    hasReads = true;
                    if (!congUses.TryFindValue(cls, out congVars))
                    {
                        congVars = new Set<Term>(Term.Compare);
                        congUses.Add(cls, congVars);
                    }

                    congVars.Add(kv.Key);
                }

                if (!hasReads && !oriented.Contains(cls = classes[kv.Key].Find())) 
                {
                    oriented.Add(cls);
                    stack.Push(cls);
                }
            }

            //// Find variables are automatically oriented.
            foreach (var kv in finds)
            {
                if (!oriented.Contains(cls = classes[kv.Key].Find()))
                {
                    oriented.Add(cls);
                    stack.Push(cls);
                }
            }

            //// Data grounded classes are automatically oriented.
            foreach (var kv in classes)
            {                
                if (oriented.Contains(cls = classes[kv.Key].Find()))
                {
                    continue;
                }

                if (cls.IsDataGrounded || (Comprehension != null && Comprehension.ReadVars.ContainsKey(kv.Key)))
                {
                    oriented.Add(cls);
                    stack.Push(cls);
                }
            }

            //// Step 2. Perform topological sort.
            bool isOriented;
            ComprehensionData data;
            while (stack.Count > 0)
            {
                cls = stack.Pop();
                Contract.Assert(cls.IsRoot);

                //// First check if there is congruence class that is now oriented
                //// because one of its args is now oriented.
                foreach (var c in cls.Uses)
                {
                    if (oriented.Contains(clsp = c.Find()))
                    {
                        continue;
                    }

                    foreach (var m in clsp.Members)
                    {
                        if (m.Args.Length == 0)
                        {
                            continue;
                        }

                        isOriented = true;
                        foreach (var a in m.Args)
                        {
                            if (!oriented.Contains(classes[a].Find()))
                            {
                                isOriented = false;
                                break;
                            }
                        }

                        if (isOriented)
                        {
                            oriented.Add(clsp);
                            stack.Push(clsp);
                            break;
                        }
                    }
                }

                //// Next, check if there is a comprehension that is oriented.
                if (congUses.TryFindValue(cls, out congVars))
                {
                    foreach (var v in congVars)
                    {
                        if (oriented.Contains(classes[v].Find()))
                        {
                            continue;
                        }

                        data = comprehensions[v];
                        isOriented = true;
                        foreach (var u in data.ReadVars.Keys)
                        {
                            if (classes.TryFindValue(u, out clsp) && !oriented.Contains(clsp.Find()))
                            {
                                isOriented = false;
                                break;
                            }
                        }

                        if (isOriented && !oriented.Contains(clsp = classes[v].Find()))
                        {
                            oriented.Add(clsp);
                            stack.Push(clsp);
                        }
                    }
                }

                //// Finally, apply the rule that if f(t1,...,tn) is oriented, then t1, ..., tn are oriented.
                if (cls.IsSingleton && cls.IsDataGrounded)
                {
                    continue;
                }

                foreach (var m in cls.Members)
                {
                    if (!m.Symbol.IsDataConstructor)
                    {
                        continue;
                    }

                    foreach (var a in m.Args)
                    {
                        if (!oriented.Contains(clsp = classes[a].Find()))
                        {                           
                            oriented.Add(clsp);
                            stack.Push(clsp);
                        }
                    }
                }
            }

            bool result = true;
            foreach (var kv in classes)
            {
                if (!kv.Key.Symbol.IsVariable ||
                    comprehensions.ContainsKey(kv.Key))
                {
                    continue;
                }
                else if (!oriented.Contains(kv.Value.Find()))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        Body,
                        Constants.UnorientedError.ToString(kv.Key.Symbol.PrintableName),
                        Constants.UnorientedError.Code);
                    flags.Add(flag);
                    result = false;
                }
            }

            return result;
        }

        private bool ValidateUse_Find(
            Find fnd, 
            Namespace space, 
            out Node fndCnstr,
            List<Flag> flags)
        {
            fndCnstr = null;
            var result = true;

            UserSymbol bindSymbol = null, matchSymbol = null;
            if (fnd.Binding != null)
            {
                var id = (Id)fnd.Binding;
                if (!Resolve(id.Name, "variable", id, space, x => x.IsVariable, out bindSymbol, flags))
                {
                    result = false;
                } 
            }

            if (fnd.Binding == null && fnd.Match.NodeKind == NodeKind.Id)
            {
                var id = (Id)fnd.Match;
                if (!Resolve(id.Name, "derived constant id", id, Index.SymbolTable.ModuleSpace, x => x.Kind == SymbolKind.UserCnstSymb, out matchSymbol, flags, true))
                {
                    result = false;
                }
                else if (!matchSymbol.IsDerivedConstant)
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        fnd.Match,
                        Constants.BadSyntax.ToString("expected a derived constant"),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    result = false;
                }
                else if (result)
                {
                    Contract.Assert(matchSymbol.IsDerivedConstant);
                    fndCnstr = Factory.Instance.MkRelConstr(
                                    RelKind.Eq,
                                    Factory.Instance.MkId(ASTSchema.Instance.DontCareName, fnd.Span),
                                    Factory.Instance.ToAST(fnd.Match),
                                    fnd.Span).Node;
                }
            }
            else if (fnd.Match.NodeKind == NodeKind.Id)
            {
                var id = (Id)fnd.Match;
                if (!Resolve(id.Name, "type id", id, space, x => x.Kind == SymbolKind.ConSymb || x.Kind == SymbolKind.MapSymb || x.Kind == SymbolKind.UnnSymb || x.Kind == SymbolKind.BaseSortSymb, out matchSymbol, flags))
                {
                    result = false;
                }
                else if (result)
                {
                    Contract.Assert(!matchSymbol.IsDerivedConstant);
                    fndCnstr = Factory.Instance.MkRelConstr(
                                    RelKind.Typ, 
                                    Factory.Instance.ToAST(fnd.Binding), 
                                    Factory.Instance.ToAST(fnd.Match), 
                                    fnd.Span).Node;
                }
            }
            else if (fnd.Match.NodeKind == NodeKind.FuncTerm)
            {
                if (!(((FuncTerm)fnd.Match).Function is Id))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        fnd.Match,
                        Constants.BadSyntax.ToString("find requires a type id or a data term"),
                        Constants.BadSyntax.Code);
                    flags.Add(flag);
                    result = false;
                }
                else if (result && bindSymbol == null)
                {
                    fndCnstr = Factory.Instance.MkRelConstr(
                                    RelKind.Eq,
                                    Factory.Instance.MkId(ASTSchema.Instance.DontCareName, fnd.Span),
                                    Factory.Instance.ToAST(fnd.Match),
                                    fnd.Span).Node;
                }
                else if (result && bindSymbol != null)
                {
                    fndCnstr = Factory.Instance.MkRelConstr(
                                    RelKind.Eq,
                                    Factory.Instance.ToAST(fnd.Binding),
                                    Factory.Instance.ToAST(fnd.Match),
                                    fnd.Span).Node;
                }
            }
            else
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    fnd.Match,
                    Constants.BadSyntax.ToString("find expression requires a type id or a data term"),
                    Constants.BadSyntax.Code);
                flags.Add(flag);
                result = false;
            }

            return result;
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

            if (id == ASTSchema.Instance.DontCareName)
            {
                symbol = (UserSymbol)MkFreshVar(FreshVarKind.DontCare).Symbol;
            }
            else
            {
                symbol = Index.SymbolTable.Resolve(id, out other, space, filterLookup ? validator : null);
            }

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

        /// <summary>
        /// If term is a ground data term, then makes all the classes for this term.
        /// This should only be called when term is already known to be well-typed.
        /// </summary>
        private CongruenceClass MkGrndClasses(Term term, Node node)
        {
            Contract.Requires(term.Groundness == Groundness.Ground);
            CongruenceClass cls;
            return term.Compute<CongruenceClass>(
                (x, tok) => classes.ContainsKey(x) ? null : x.Args,
                (x, ch, tok) =>
                {
                    Contract.Assert(x.Symbol.Kind == SymbolKind.BaseCnstSymb || x.Symbol.IsDataConstructor || x.Symbol.IsNonVarConstant);
                    if (classes.TryFindValue(x, out cls))
                    {
                        return cls.Find();
                    }

                    cls = new CongruenceClass(x, x, node);
                    cls.IsDataGrounded = true;
                    classes.Add(x, cls);

                    foreach (var subCls in ch)
                    {
                        subCls.Find().AddUse(cls);
                    }

                    return cls;
                });
        }

        /// <summary>
        /// Makes a congruence class, or returns the existing one if it
        /// already exists.
        /// </summary>
        private CongruenceClass GetClass(Term term, Term type, Node node)
        {
            CongruenceClass cls;
            if (classes.TryFindValue(term, out cls))
            {
                return cls.Find();
            }

            Contract.Assert(type != null && node != null);
            cls = new CongruenceClass(term, type, node);
            classes.Add(term, cls);

            if (term.Symbol.Kind == SymbolKind.BaseCnstSymb || term.Symbol.IsNonVarConstant)
            {
                cls.IsDataGrounded = true;
            }
            else if (term.Symbol.IsDataConstructor)
            {
                cls.IsDataGrounded = true;
                CongruenceClass argCls;
                for (int i = 0; i < term.Args.Length; ++i)
                {
                    argCls = classes[term.Args[i]];
                    if (!argCls.IsDataGrounded)
                    {
                        cls.IsDataGrounded = false;
                        break;
                    }
                }
            }

            return cls;
        }

        private Term MkFreshVar(FreshVarKind kind)
        {
            bool wasAdded;
            var index = (int)kind;
            switch (kind)
            {
                case FreshVarKind.DontCare:
                    return Index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "dc", nextVarIds[index]++), true, out wasAdded);
                case FreshVarKind.Selector:
                    return Index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "sv", nextVarIds[index]++), true, out wasAdded);
                case FreshVarKind.Comprehension:
                    return Index.MkVar(string.Format("{0}{1}{2}", SymbolTable.ManglePrefix, "cv", nextVarIds[index]++), true, out wasAdded);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Updates the fresh var counts to take into account the validation / compile phases.
        /// </summary>
        private void UpdateFreshVarCounts()
        {
            if (Comprehension != null)
            {
                for (int i = 0; i < nextVarIds.Length; ++i)
                {
                    nextVarIds[i] = Math.Max(nextVarIds[i], Comprehension.Owner.nextVarIds[i]);
                }
            }
            else
            {
                for (int i = 0; i < nextVarIds.Length; ++i)
                {
                    nextVarIds[i] = GetNextVarId((FreshVarKind)i);
                }
            }
        }

        private class CongruenceClass
        {
            private static readonly List<CongruenceClass> EmptyClassList = new List<CongruenceClass>();
            private int rank;
            private Term type;
            private readonly Term rep;
            private readonly Node node;
            private CongruenceClass parent;
            private LinkedList<CongruenceClass> children = null;
            private LinkedList<CongruenceClass> uselist = null;

            /// <summary>
            /// True if the class contains a term of the form f(...)
            /// </summary>
            public bool HasDataTerm
            {
                get;
                set;
            }

            /// <summary>
            /// True if the class contains a term t and there is another
            /// term .(t, "lbl") in some class
            /// </summary>
            public bool IsSelected
            {
                get;
                set;
            }

            /// <summary>
            /// True if the class contains a non-variable/non-query constant or
            /// it contains a term f(t_1,...t_n) where each [t_1] ... [t_n] is 
            /// data grounded and f is a data constructor.
            /// </summary>
            public bool IsDataGrounded
            {
                get;
                set;
            }

            /// <summary>
            /// True if the class contains only one element.
            /// </summary>
            public bool IsSingleton
            {
                get
                {
                    return parent == this && (children == null || children.Count == 0);
                }
            }

            /// <summary>
            /// True if this class is currently the root of a union-find tree.
            /// </summary>
            public bool IsRoot
            {
                get { return parent == this; }
            }

            public Term Representative
            {
                get
                {
                    return Find().rep;
                }
            }

            public Term Type
            {
                get
                {
                    return Find().type;
                }
            }

            public Node Node
            {
                get
                {
                    return Find().node;
                }
            }

            public IEnumerable<CongruenceClass> Uses
            {
                get
                {
                    var p = Find();
                    if (p.uselist != null)
                    {
                        foreach (var c in p.uselist)
                        {
                            yield return c;
                        }
                    }

                    var stack = new Stack<IEnumerator<CongruenceClass>>();
                    stack.Push(children == null
                        ? (IEnumerator<CongruenceClass>)EmptyClassList.GetEnumerator()
                        : (IEnumerator<CongruenceClass>)children.GetEnumerator());
                    IEnumerator<CongruenceClass> top;
                    while (stack.Count > 0)
                    {
                        top = stack.Peek();
                        if (top.MoveNext())
                        {
                            if (top.Current.uselist != null)
                            {
                                foreach (var c in top.Current.uselist)
                                {
                                    yield return c;
                                }
                            }

                            stack.Push(top.Current.children == null
                                ? (IEnumerator<CongruenceClass>)EmptyClassList.GetEnumerator()
                                : (IEnumerator<CongruenceClass>)top.Current.children.GetEnumerator());
                        }
                        else
                        {
                            stack.Pop();
                        }
                    }
                }
            }

            public IEnumerable<Term> Members
            {
                get
                {
                    var p = Find();
                    yield return p.Representative;
                    var stack = new Stack<IEnumerator<CongruenceClass>>();
                    stack.Push(children == null 
                        ? (IEnumerator<CongruenceClass>)EmptyClassList.GetEnumerator()
                        : (IEnumerator<CongruenceClass>)children.GetEnumerator());
                    IEnumerator<CongruenceClass> top;
                    while (stack.Count > 0)
                    {
                        top = stack.Peek();
                        if (top.MoveNext())
                        {
                            yield return top.Current.rep;
                            stack.Push(top.Current.children == null
                                ? (IEnumerator<CongruenceClass>)EmptyClassList.GetEnumerator()
                                : (IEnumerator<CongruenceClass>)top.Current.children.GetEnumerator());
                        }
                        else
                        {
                            stack.Pop();
                        }
                    }
                }
            }

            public CongruenceClass(Term rep, Term type, Node node)
            {
                Contract.Requires(rep != null && type != null);
                Contract.Requires(rep.Groundness == Groundness.Ground || rep.Groundness == Groundness.Variable);
                Contract.Requires(type.Groundness == Groundness.Ground || type.Groundness == Groundness.Type);

                this.rep = rep;
                this.type = type;
                this.node = node;
                parent = this;
                rank = 0;

                if (rep.Symbol.IsDataConstructor)
                {
                    HasDataTerm = true;
                }
            }

            public CongruenceClass Find()
            {
                var crnt = this;
                while (crnt.parent != crnt)
                {
                    crnt = crnt.parent;
                }

                return crnt;
            }

            /// <summary>
            /// Attempts to merge congruence classes
            /// </summary>
            public static bool Merge(
                ConstraintSystem sys,
                CongruenceClass c1, 
                CongruenceClass c2, 
                bool doOccurs,
                Stack<ImpliedEquality> implEqs,
                out CongruenceClass cmerged)
            {
                Contract.Assert(c1 != null && c2 != null && c1 != c2);
                Contract.Assert(c1.parent == c1 && c2.parent == c2);

                //// Premerge steps:
                //// (1) Occurs-check.
                //// (2) Type stabilization.
                if (doOccurs && !sys.unifier.Unify(c1.Representative, c2.Representative, implEqs))
                {
                    cmerged = null;
                    return false;
                }

                Term tintr;
                while (c1.Type != c2.Type)
                {
                    if (!sys.Index.MkIntersection(c1.Type, c2.Type, out tintr) ||
                        !c1.RefineType(sys, tintr) ||
                        !c2.RefineType(sys, tintr))
                    {
                        cmerged = null;
                        return false;
                    }
                }

                //// Merge step: Merge the union-find trees.
                if (c1.rank >= c2.rank)
                {
                    if (c1.children == null)
                    {
                        c1.children = new LinkedList<CongruenceClass>();
                    }

                    c1.children.AddLast(c2);
                    c1.rank = Math.Max(c1.rank, c2.rank + 1);
                    c2.parent = c1;
                    cmerged = c1;
                }
                else
                {
                    if (c2.children == null)
                    {
                        c2.children = new LinkedList<CongruenceClass>();
                    }

                    c2.children.AddLast(c1);
                    c2.rank = Math.Max(c2.rank, c1.rank + 1);
                    c1.parent = c2;
                    cmerged = c2;
                }

                cmerged.HasDataTerm = c1.HasDataTerm || c2.HasDataTerm;
                cmerged.IsSelected = c1.IsSelected || c2.IsSelected;
                cmerged.IsDataGrounded = c1.IsDataGrounded || c2.IsDataGrounded;

                //// Postmerge:
                //// (1) Add congruence equalities
                //// (2) Add equalities due to singleton types
                //// (3) If the type of a class with selectors collapses
                ////     to a specific constructor F, then the selectors
                ////     can be specialized.
                Term stdRep;
                bool wasAdded;
                CongruenceClass other;
                var stdReps = new Map<Term, CongruenceClass>(Term.Compare);
                foreach (var u in cmerged.Uses)
                {
                    stdRep = u.rep;
                    var args = new Term[stdRep.Symbol.Arity];
                    for (int i = 0; i < stdRep.Symbol.Arity; ++i)
                    {
                        args[i] = sys.GetClass(stdRep.Args[i], null, null).Representative;
                    }

                    stdRep = sys.Index.MkApply(stdRep.Symbol, args, out wasAdded);
                    if (stdReps.TryFindValue(stdRep, out other))
                    {
                        if (other != u.Find())
                        {
                            implEqs.Push(new ImpliedEquality(other, u.Find(), false));
                        }
                    }
                    else
                    {
                        stdReps.Add(stdRep, u.Find());
                    }
                }

                return true;
            }

            public static int Compare(CongruenceClass c1, CongruenceClass c2)
            {
                Contract.Requires(c1 != null && c2 != null);
                return Term.Compare(c1.Representative, c2.Representative);
            }

            public static void DefineGroundTypes(
                ConstraintSystem sys,
                CongruenceClass cls,
                Stack<ImpliedEquality> implEqs)
            {
                if (cls.IsDataGrounded)
                {
                    return;
                }

                Term member;
                if (!IsSingletonType(sys, cls.Type, out member))
                {
                    return;
                }

                cls.IsDataGrounded = true;
                implEqs.Push(new ImpliedEquality(cls, sys.MkGrndClasses(member, cls.Node), false));
            }

            /// <summary>
            /// If this class contains a term t and t is selected in .(t, "lbl") and
            /// the type of t determines that t = f(...), then an equality t = f(x_1,...,x_n)
            /// is implied. Also equalities between x_i and selections are added. 
            /// </summary>
            public static void DefineSelectors(
                ConstraintSystem sys,
                CongruenceClass cls,
                Stack<ImpliedEquality> implEqs)
            {
                Contract.Assert(cls.parent == cls);

                UserSymbol dataSymb;
                if (!cls.IsSelected ||
                    cls.HasDataTerm ||
                    !IsMonoDataType(sys, cls.Type, out dataSymb))
                {
                    return;
                }

                var args = new Term[dataSymb.Arity];
                var argClss = new CongruenceClass[dataSymb.Arity];
                for (int i = 0; i < args.Length; ++i)
                {
                    args[i] = sys.MkFreshVar(FreshVarKind.Selector);
                    argClss[i] = sys.GetClass(args[i], sys.Index.GetCanonicalTerm(dataSymb, i), cls.Node);
                }

                int index = 0;
                bool foundIndex;
                string label;
                foreach (var c in cls.Uses)
                {
                    foreach (var t in c.Members)
                    {
                        if (t.Symbol != sys.Index.SelectorSymbol ||
                            sys.GetClass(t.Args[0], null, null) != cls)
                        {
                            continue;
                        }

                        label = (string)((BaseCnstSymb)t.Args[1].Symbol).Raw;
                        foundIndex = false;
                        if (dataSymb.Kind == SymbolKind.ConSymb)
                        {
                            foundIndex = ((ConSymb)dataSymb).GetLabelIndex(label, out index);
                        }
                        else if (dataSymb.Kind == SymbolKind.MapSymb)
                        {
                            foundIndex = ((MapSymb)dataSymb).GetLabelIndex(label, out index);
                        }

                        Contract.Assert(foundIndex);
                        implEqs.Push(new ImpliedEquality(c, argClss[index], false));
                    }
                }

                bool wasAdded;
                Term type = null;
                if (dataSymb.Kind == SymbolKind.ConSymb)
                {
                    type = sys.Index.MkApply(((ConSymb)dataSymb).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                }
                else if (dataSymb.Kind == SymbolKind.MapSymb)
                {
                    type = sys.Index.MkApply(((MapSymb)dataSymb).SortSymbol, TermIndex.EmptyArgs, out wasAdded);
                }
                else
                {
                    throw new NotImplementedException();
                }

                var defCls = sys.GetClass(
                    sys.Index.MkApply(dataSymb, args, out wasAdded),
                    type,
                    cls.Node);
                foreach (var ac in argClss)
                {
                    ac.AddUse(defCls);
                }

                implEqs.Push(new ImpliedEquality(cls, defCls, false));
            }

            /// <summary>
            /// This should only happen before any equalities are asserted.
            /// </summary>
            public void AddUse(CongruenceClass cls)
            {
                Contract.Assert(parent == this);
                if (uselist == null)
                {
                    uselist = new LinkedList<CongruenceClass>();
                }

                uselist.AddLast(cls);
            }

            public bool RefineType(ConstraintSystem sys, Term type)
            {
                Contract.Assert(parent == this);
                if (this.type == type)
                {
                    return true;
                }

                //// Because the upward Galois approximation is more precise than the downward,
                //// this algorithm propagates changes down through arguments before propagating
                //// them upwards.
                Term fix;
                CongruenceClass cls;
                Tuple<CongruenceClass, Term> top;
                var lower = new Queue<Tuple<CongruenceClass, Term>>();
                var propagate = new Set<CongruenceClass>((x, y) => Term.Compare(x.Representative, y.Representative));
                lower.Enqueue(new Tuple<CongruenceClass, Term>(this, type));
                while (propagate.Count > 0 || lower.Count > 0)
                {
                    while (lower.Count > 0)
                    {
                        top = lower.Dequeue();
                        Contract.Assert(top.Item1.parent == top.Item1);

                        if (!top.Item1.GetLoweredFixpoint(sys, top.Item2, out fix))
                        {
                            return false;
                        }
                        else if (top.Item1.type != fix)
                        {
                            //// If lowering the class changed its type, then need to 
                            //// check if arguments to members should be lowered, and
                            //// need to add uselist into propagate set.

                            top.Item1.type = fix;
                            top.Item1.Lower(sys, lower);
                            top.Item1.Propagate(propagate);
                            sys.NotifyTypeChange(top.Item1);
                        }
                    }

                    while (propagate.Count > 0)
                    {
                        cls = propagate.GetSomeElement();
                        propagate.Remove(cls);
                        if (!cls.ComputeClassType(sys, out fix))
                        {
                            return false;
                        }
                        else if (fix != cls.Type)
                        {
                            lower.Enqueue(new Tuple<CongruenceClass, Term>(cls, fix));
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// If t only accepts terms of the form F(...), then returns F
            /// otherwise returns false.
            /// </summary>
            private static bool IsMonoDataType(ConstraintSystem sys, Term t, out UserSymbol symb)
            {
                var token = new SuccessToken();
                Symbol s = null, sp;
                t.Visit(
                    x => x.Symbol == sys.Index.TypeUnionSymbol ? x.Args : null,
                    x =>
                    {
                        if (x.Symbol == sys.Index.TypeUnionSymbol)
                        {
                            return;
                        }
                        else if (x.Symbol.IsDataConstructor)
                        {
                            sp = x.Symbol;
                        }
                        else if (x.Symbol.Kind == SymbolKind.UserSortSymb)
                        {
                            sp = ((UserSortSymb)x.Symbol).DataSymbol;
                        }
                        else
                        {
                            token.Failed();
                            return;
                        }

                        if (s == null)
                        {
                            s = sp;
                        }
                        else if (s != null && s != sp)
                        {
                            token.Failed();
                            return;
                        }
                    },
                    token);

                if (s == null || !token.Result)
                {
                    symb = null;
                    return false;
                }

                symb = (UserSymbol)s;
                return true;
            }

            /// <summary>
            /// Returns true if the type denotes a singleton set. Returns the member of this set.
            /// </summary>
            private static bool IsSingletonType(ConstraintSystem sys, Term t, out Term member)
            {
                var token = new SuccessToken();
                member = t.Compute<Term>(
                    (tp, tok) => IsSingletonType_Unfold(sys, tp, tok),
                    (tp, ch, tok) => IsSingletonType_Fold(sys, tp, ch, tok),
                    token);
                return token.Result;
            }
            
            /// <summary>
            /// Returns true if the type denotes a singleton set. Returns the member of this set.
            /// </summary>
            private static IEnumerable<Term> IsSingletonType_Unfold(ConstraintSystem sys, Term t, SuccessToken token)
            {
                if (t.Symbol.Kind == SymbolKind.BaseSortSymb)
                {
                    token.Failed();
                    yield break;
                }
                else if (t.Symbol == sys.Index.RangeSymbol ||
                         t.Groundness == Groundness.Ground)
                {
                    yield break;
                }
                else if (t.Symbol.IsDataConstructor ||
                         t.Symbol == sys.Index.TypeUnionSymbol)
                {
                    foreach (var a in t.Args)
                    {
                        yield return a;
                    }
                }
                else if (t.Symbol.Kind == SymbolKind.UserSortSymb)
                {
                    var dataSymb = ((UserSortSymb)t.Symbol).DataSymbol;
                    bool canExpand = true;
                    Term tp;
                    for (int i = 0; i < dataSymb.Arity; ++i)
                    {
                        tp = sys.Index.GetCanonicalTerm(dataSymb, i);
                        if (tp.Groundness == Groundness.Ground ||
                            tp.Symbol.Kind == SymbolKind.UserSortSymb ||
                            (tp.Symbol == sys.Index.RangeSymbol && tp.Args[0] == tp.Args[1]))
                        {
                            continue;
                        }

                        canExpand = false;
                        break;
                    }

                    if (!canExpand)
                    {
                        token.Failed();
                        yield break;
                    }

                    for (int i = 0; i < dataSymb.Arity; ++i)
                    {
                        yield return sys.Index.GetCanonicalTerm(dataSymb, i);
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            /// <summary>
            /// Returns true if the type denotes a singleton set. Returns the member of this set. 
            /// </summary>
            private static Term IsSingletonType_Fold(ConstraintSystem sys, Term t, IEnumerable<Term> children, SuccessToken token)
            {
                int i;
                bool wasAdded;
                if (!token.Result)
                {
                    return null;
                }
                else if (t.Symbol == sys.Index.RangeSymbol)
                {
                    if (t.Args[0] == t.Args[1])
                    {
                        return t.Args[0];
                    }
                    else
                    {
                        token.Failed();
                        return null;
                    }
                }
                else if (t.Groundness == Groundness.Ground)
                {
                    return t;
                }
                else if (t.Symbol.IsDataConstructor)
                {
                    var args = new Term[t.Symbol.Arity];
                    i = 0;
                    foreach (var a in children)
                    {
                        args[i++] = a;
                        if (a == null)
                        {
                            return null;
                        }
                    }

                    return sys.Index.MkApply(t.Symbol, args, out wasAdded);
                }
                else if (t.Symbol == sys.Index.TypeUnionSymbol)
                {
                    using (var it = children.GetEnumerator())
                    {
                        it.MoveNext();
                        var first = it.Current;
                        it.MoveNext();
                        if (it.Current != first)
                        {
                            token.Failed();
                            return null;
                        }

                        return first;
                    }
                }
                else if (t.Symbol.Kind == SymbolKind.UserSortSymb)
                {
                    var dataSymb = ((UserSortSymb)t.Symbol).DataSymbol;
                    var args = new Term[dataSymb.Arity];
                    i = 0;
                    foreach (var a in children)
                    {
                        args[i++] = a;
                        if (a == null)
                        {
                            return null;
                        }
                    }

                    return sys.Index.MkApply(dataSymb, args, out wasAdded);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            private void Propagate(Set<CongruenceClass> propagate)
            {
                foreach (var c in Uses)
                {
                    propagate.Add(c.Find());
                }
            }

            private void Lower(ConstraintSystem sys, Queue<Tuple<CongruenceClass, Term>> lower)
            {
                var type = Type;
                Term[] tarr2 = null, down; //// Special handling for selector
                var tarr = new Term[] { type };
                var i = 0;
                CongruenceClass cls;
                foreach (var m in Members)
                {
                    switch (m.Symbol.Kind)
                    {
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            if (type.Symbol.Kind == SymbolKind.UserSortSymb)
                            {
                                Contract.Assert(((UserSortSymb)type.Symbol).DataSymbol == m.Symbol);
                                continue;
                            }

                            Contract.Assert(type.Symbol == m.Symbol);
                            for (i = 0; i < m.Symbol.Arity; ++i)
                            {
                                cls = sys.GetClass(m.Args[i], null, null);
                                if (cls.Type != type.Args[i])
                                {
                                    lower.Enqueue(new Tuple<CongruenceClass, Term>(cls, type.Args[i]));
                                }
                            }

                            break;
                        case SymbolKind.BaseOpSymb:
                            if (m.Symbol == sys.Index.SelectorSymbol)
                            {
                                if (tarr2 == null)
                                {
                                    tarr2 = new Term[2];
                                    tarr2[0] = type;
                                }

                                tarr2[1] = m.Args[1];
                                down = ((BaseOpSymb)m.Symbol).DownwardApprox(sys.Index, tarr2);
                            }
                            else
                            {
                                down = ((BaseOpSymb)m.Symbol).DownwardApprox(sys.Index, tarr);
                            }

                            Contract.Assert(down != null);
                            for (i = 0; i < m.Symbol.Arity; ++i)
                            {
                                cls = sys.GetClass(m.Args[i], null, null);
                                if (cls.Type != down[i])
                                {
                                    lower.Enqueue(new Tuple<CongruenceClass, Term>(cls, down[i]));
                                }
                            }

                            break;
                        case SymbolKind.BaseCnstSymb:
                        case SymbolKind.UserCnstSymb:
                            continue;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            /// <summary>
            /// Computes the maximal type containing the types of all its members.
            /// Returns false if this type is empty.
            /// </summary>
            private bool ComputeClassType(ConstraintSystem sys, out Term type)
            {
                Term next, other;
                type = Type;
                int i;
                bool wasAdded;
                CongruenceClass cls;
                BaseOpSymb baseOp;
                Term[] up;
                foreach (var m in Members)
                {
                    switch (m.Symbol.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            if (!sys.Index.MkIntersection(m, type, out next))
                            {
                                return false;
                            }

                            type = next;
                            break;
                        case SymbolKind.UserCnstSymb:
                            {
                                var usrCnst = (UserCnstSymb)m.Symbol;
                                switch (usrCnst.UserCnstKind)
                                {
                                    case UserCnstSymbKind.Variable:
                                        continue;
                                    case UserCnstSymbKind.New:
                                    case UserCnstSymbKind.Derived:
                                        if (!sys.Index.MkIntersection(m, type, out next))
                                        {
                                            return false;
                                        }

                                        type = next;
                                        break;
                                    default:
                                        throw new NotImplementedException();
                                }

                                break;
                            }
                        case SymbolKind.ConSymb:
                        case SymbolKind.MapSymb:
                            {
                                var args = new Term[m.Symbol.Arity];
                                for (i = 0; i < args.Length; ++i)
                                {
                                    cls = sys.GetClass(m.Args[i], null, null);
                                    args[i] = cls.Type;
                                }

                                other = sys.Index.MkApply(m.Symbol, args, out wasAdded);
                                if (!sys.Index.MkIntersection(other, type, out next))
                                {
                                    return false;
                                }

                                type = next;
                                break;
                            }
                        case SymbolKind.BaseOpSymb:
                            {
                                baseOp = (BaseOpSymb)m.Symbol;
                                var args = new Term[m.Symbol.Arity];
                                for (i = 0; i < args.Length; ++i)
                                {
                                    cls = sys.GetClass(m.Args[i], null, null);
                                    args[i] = cls.Type;
                                }

                                up = baseOp.UpwardApprox(sys.Index, args);
                                if (up == null || !sys.Index.MkIntersection(up[0], type, out next))
                                {
                                    return false;
                                }

                                type = next;
                                break;
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }

                return true;
            }

            /// <summary>
            /// If lowering a congruence class to type, then finds the maximal subtype for
            /// which all members agree. Computed by repeatedly finding:
            /// next = \bigcap \{ x | x = f_{\up}(f_{\down}(prev)) \}. 
            /// </summary>
            private bool GetLoweredFixpoint(ConstraintSystem sys, Term type, out Term fix)
            {
                Term next;
                if (type == null || !sys.Index.MkIntersection(Type, type, out fix))
                {
                    fix = null;
                    return false;
                }
                else if (fix == Type)
                {
                    fix = Type;
                    return true;
                }

                bool cont = true;
                Term[] down, up;
                Term[] tarr = new Term[1];
                Term[] tarr2 = null; //// Special handling for selector
                BaseOpSymb baseSymb;

                while (cont)
                {
                    cont = false;
                    foreach (var m in Members)
                    {
                        switch (m.Symbol.Kind)
                        {
                            case SymbolKind.BaseOpSymb:
                                baseSymb = (BaseOpSymb)m.Symbol;
                                if (baseSymb == sys.Index.SelectorSymbol)
                                {
                                    if (tarr2 == null)
                                    {
                                        tarr2 = new Term[2];
                                    }

                                    tarr2[0] = fix;
                                    tarr2[1] = m.Args[1];
                                    down = baseSymb.DownwardApprox(sys.Index, tarr2);
                                }
                                else
                                {
                                    tarr[0] = fix;
                                    down = baseSymb.DownwardApprox(sys.Index, tarr);
                                }

                                if (down == null)
                                {
                                    fix = null;
                                    return false;
                                }

                                up = baseSymb.UpwardApprox(sys.Index, down);
                                if (up == null || !sys.Index.MkIntersection(up[0], fix, out next))
                                {
                                    fix = null;
                                    return false;
                                }
                                else if (next != fix)
                                {
                                    cont = true;
                                    fix = next;
                                }
                                break;
                            case SymbolKind.ConSymb:
                            case SymbolKind.MapSymb:
                            case SymbolKind.BaseCnstSymb:
                            case SymbolKind.UserCnstSymb:
                                continue;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                }

                return true;
            }
        }

        private class Constraint
        {
            public RelKind Kind
            {
                get;
                private set;
            }

            public Node Node
            {
                get;
                private set;
            }

            public CongruenceClass Arg1
            {
                get;
                private set;
            }

            public CongruenceClass Arg2
            {
                get;
                private set;
            }

            public Constraint(Node node, CongruenceClass arg1, CongruenceClass arg2)
            {
                Node = node;
                Arg1 = arg1;
                Arg2 = arg2;
                Kind = ((RelConstr)node).Op;
            }

            public Constraint(RelKind kind, Node node, CongruenceClass arg1, CongruenceClass arg2)
            {
                Node = node;
                Arg1 = arg1;
                Arg2 = arg2;
                Kind = kind;
            }
        }

        private class ImpliedEquality
        {
            public CongruenceClass LHS
            {
                get;
                private set;
            }

            public CongruenceClass RHS
            {
                get;
                private set;
            }

            public bool NeedsOccursCheck
            {
                get;
                private set;
            }

            public ImpliedEquality(CongruenceClass lhs, CongruenceClass rhs, bool needsOccursCheck)
            {
                LHS = lhs;
                RHS = rhs;
                NeedsOccursCheck = needsOccursCheck;
            }
        }

        private class Unifier
        {
            //// Suppose f(...,t,...) is in an equivalence class for data constructor f,
            //// and t is not of the form g(...) for data constructor g.
            //// Then t is tracked.
            ////
            //// Suppose .(t, "lbl") is in some equivalence class, then .(t, "lbl")
            //// is tracked in the class of t.
            private const int Track_FreeOccurs = 0;

            //// Suppose t = f(...,x,...) is in an equivalence class for data constructor f.
            //// Then t is tracked; only one such t is tracked per class.
            private const int Track_FreeApply = 0;

            private EquivalenceRelation<Term> unifyRel = 
                new EquivalenceRelation<Term>(Term.Compare, 1, 1);

            private ConstraintSystem owner;

            public Unifier(ConstraintSystem owner)
            {
                Contract.Requires(owner != null);
                this.owner = owner;
            }

            /// <summary>
            /// If sel = .(t, "lbl") then creates equivalence classes for sel, t and adds
            /// sel to the tracking set of t.
            /// </summary>
            public void TrackSelect(Term sel)
            {
                Contract.Requires(sel != null && (ReservedOpKind)((BaseOpSymb)sel.Symbol).OpKind == ReservedOpKind.Select);
                unifyRel.Add(sel);
                unifyRel.Add(sel.Args[0]);
                unifyRel.AddToTrackSet(sel.Args[0], Track_FreeOccurs, sel);
            }

            /// <summary>
            /// Attempts to unify the lhs and rhs. Returns equalities implied by unification which will
            /// be in the normal form: x = t.
            /// </summary>
            public bool Unify(Term lhs, Term rhs, Stack<ImpliedEquality> implEqs)
            {
                if (unifyRel.Equals(lhs, rhs))
                {
                    return true;
                }

                //// Contains the variables appearing in either the lhs or rhs.
                var allVars = new Set<Term>(Term.Compare);
                var success = new SuccessToken();
                var pend = new Stack<Tuple<Term, Term>>();
                pend.Push(new Tuple<Term, Term>(lhs, rhs));
                Tuple<Term, Term> top;
                while (pend.Count > 0)
                {
                    top = pend.Pop();
                    top.Item1.Compute<Unit>(
                        top.Item2,
                        (t1, t2, s) => new Tuple<IEnumerable<Term>, IEnumerable<Term>>(Unify_Unfold(t1, t2, true), Unify_Unfold(t1, t2, false)),
                        (t1, t2, ch, s) => Unify_Fold(t1, t2, allVars, implEqs, pend, ch, s),
                        success);
                    if (!success.Result)
                    {
                        return false;
                    }
                }

                return OccursCheck(allVars);
            }

            private IEnumerable<Term> Unify_Unfold(Term t1, Term t2, bool unfoldFirst)
            {
                if (t1.Groundness != Groundness.Variable ||
                    t2.Groundness != Groundness.Variable ||
                    t1.Symbol.Arity == 0 ||
                    t2.Symbol.Arity == 0 ||
                    t1.Symbol != t2.Symbol ||
                    (t1.Symbol.Kind != SymbolKind.ConSymb && t1.Symbol.Kind != SymbolKind.MapSymb) ||
                    (t2.Symbol.Kind != SymbolKind.ConSymb && t2.Symbol.Kind != SymbolKind.MapSymb))
                {
                    yield break;
                }

                using (var it1 = t1.Args.GetEnumerator())
                {
                    using (var it2 = t2.Args.GetEnumerator())
                    {
                        while (it1.MoveNext() && it2.MoveNext())
                        {
                            if (it1.Current.Groundness == Groundness.Ground &&
                                it2.Current.Groundness == Groundness.Ground)
                            {
                                continue;
                            }

                            yield return unfoldFirst ? it1.Current : it2.Current;
                        }
                    }
                }
            }

            private Unit Unify_Fold(
                Term t1, 
                Term t2, 
                Set<Term> allVars, 
                Stack<ImpliedEquality> implEqs,
                Stack<Tuple<Term, Term>> pend,
                IEnumerable<Unit> children, 
                SuccessToken token)
            {
                if (!children.IsEmpty<Unit>())
                {
                    unifyRel.Equate(t1, t2);
                    return default(Unit);
                }

                //// Only need to handle equalities that are obtained after
                //// applying f(t_1,...,t_n) = f(t'_1,...,t'_n) => t_i = t'_i
                //// until a base case is reached.

                //// Step 1. Handle any selectors
                MergeSelectors(t1, t2, pend);

                //// Step 2. Handle free apps
                if (!MergeFreeApps(t1, t2, pend))
                {
                    token.Failed();
                }

                //// Step 3. Expose this equality to the constraint system
                //// and record variables that should be tested for the occurs check
                unifyRel.Equate(t1, t2);
                implEqs.Push(new ImpliedEquality(
                    owner.GetClass(t1, null, null), 
                    owner.GetClass(t2, null, null), 
                    false));

                NoticeVariables(t1, allVars);
                NoticeVariables(t2, allVars);
                return default(Unit);
            }

            private bool OccursCheck(Set<Term> vars)
            {
                /*
                Console.Write("Occurs check on variables: ");
                foreach (var v in vars)
                {
                    Console.Write("{0} ", v.Debug_GetSmallTermString());
                }

                Console.WriteLine();
                unifyRel.Debug_PrintRelation(MessageHelpers.Debug_GetSmallTermString);
                */

                //// History[v] is false if the occurs check is visiting v.
                //// History[v] is true if the occurs check visited v and did not find violations.
                var history = new Map<Term, bool>(Term.Compare);
                var stack = new Stack<OccursState>();

                Term u, rep;
                OccursState top;
                bool isVisited, violated;
                foreach (var v in vars)
                {
                    rep = unifyRel.GetRepresentative(v);
                    if (history.TryFindValue(rep, out isVisited))
                    {
                        Contract.Assert(isVisited);
                        continue;
                    }

                    stack.Push(new OccursState(rep, history, this));
                    while (stack.Count > 0)
                    {
                        top = stack.Peek();
                        if (top.MoveNext(this, history, out u, out violated))
                        {
                            stack.Push(new OccursState(u, history, this));
                        }
                        else if (violated)
                        {
                            return false;
                        }
                        else
                        {
                            stack.Pop().Finished(history);
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// If t is a selector, then finds the variable
            /// at the root of the selector chain and records
            /// that it must be tested for unification.
            /// </summary>
            private void NoticeVariables(Term t, Set<Term> allVars)
            {
                if (t.Symbol.IsVariable)
                {
                    allVars.Add(t);
                    return;
                }

                var bo = t.Symbol as BaseOpSymb;
                if (bo == null || 
                    !(bo.OpKind is ReservedOpKind) ||
                    (ReservedOpKind)bo.OpKind != ReservedOpKind.Select)
                {
                    return;
                }

                while (t.Symbol.Arity > 0)
                {
                    t = t.Args[0];
                }

                allVars.Add(t);              
            }

            /// <summary>
            /// If lhs and rhs have .(lhs, lbl) and .(rhs, lbl) in their
            /// tracking set, then .(lhs, lbl) = .(rhs, lbl).
            /// </summary>
            private void MergeSelectors(
                Term lhs,
                Term rhs,
                Stack<Tuple<Term, Term>> pend)
            {
                if (lhs == rhs)
                {
                    return;
                }

                string label;
                BaseOpSymb bo;
                var mergeMap = new Map<string, MutableTuple<Term, Term>>(string.CompareOrdinal);
                foreach (var t in unifyRel.GetTrackSet(lhs, Track_FreeOccurs))
                {
                    if (t.Symbol.Kind != SymbolKind.BaseOpSymb)
                    {
                        continue;
                    }

                    bo = (BaseOpSymb)t.Symbol; 
                    if (!(bo.OpKind is ReservedOpKind) ||
                        (ReservedOpKind)bo.OpKind != ReservedOpKind.Select)
                    {
                        continue;
                    }

                    label = (string)((BaseCnstSymb)t.Args[1].Symbol).Raw;
                    mergeMap[label] = new MutableTuple<Term, Term>(t, null);
                }

                MutableTuple<Term, Term> mergeData;
                foreach (var t in unifyRel.GetTrackSet(rhs, Track_FreeOccurs))
                {
                    if (t.Symbol.Kind != SymbolKind.BaseOpSymb)
                    {
                        continue;
                    }

                    bo = (BaseOpSymb)t.Symbol;
                    if (!(bo.OpKind is ReservedOpKind) ||
                        (ReservedOpKind)bo.OpKind != ReservedOpKind.Select)
                    {
                        continue;
                    }

                    label = (string)((BaseCnstSymb)t.Args[1].Symbol).Raw;
                    if (!mergeMap.TryFindValue(label, out mergeData) ||
                        mergeData.Item2 != null ||
                        unifyRel.Equals(mergeData.Item1, t))
                    {
                        continue;
                    }

                    mergeData.Item2 = t;
                    pend.Push(new Tuple<Term, Term>(mergeData.Item1, t));
                }
            }

            /// <summary>
            /// Considers an equalty of the form x = t. If t = f(...) for data con f, 
            /// then subterms of f(...) are tracked. If t is already equal to f(...)
            /// then an equality is placed in pend.
            /// </summary>
            private bool MergeFreeApps(Term lhs, Term rhs, Stack<Tuple<Term, Term>> pend)
            {
                Contract.Requires(lhs != null && rhs != null);
                if (lhs.Symbol.IsDataConstructor && rhs.Symbol.IsDataConstructor)
                {
                    //// Only possible if a bad equality f(...) = g(...)
                    //// is obtained, so ignore it.
                    return true;
                }
                else if (lhs.Symbol.IsDataConstructor)
                {
                    return MergeFreeApps(rhs, lhs, pend);
                }

                Term lhsApply, rhsApply;
                if (!unifyRel.GetTracker(lhs, Track_FreeApply, out lhsApply))
                {
                    lhsApply = null;
                }

                if (!unifyRel.GetTracker(rhs, Track_FreeApply, out rhsApply))
                {
                    rhsApply = null;
                }

                if (rhs.Symbol.IsDataConstructor)
                {
                    if (rhsApply == null)
                    {
                        rhsApply = rhs;
                        unifyRel.SetTracker(rhs, Track_FreeApply, rhs);
                    }
                    else if (rhsApply != rhs)
                    {
                        pend.Push(new Tuple<Term, Term>(rhsApply, rhs));
                    }
                }

                if (lhsApply != null && rhsApply != null && lhsApply != rhsApply)
                {
                    pend.Push(new Tuple<Term, Term>(lhsApply, rhsApply));
                }

                if (!rhs.Symbol.IsDataConstructor)
                {
                    return true;
                }

                //// Finally, track the free occurrences of variables in rhs.
                bool result = true;
                rhs.Visit(
                    t => t.Groundness == Groundness.Variable && t.Symbol.IsDataConstructor ? t.Args : null,
                    t =>
                    {
                        if (t == lhs)
                        {
                            result = false;
                        }
                        else if (!t.Symbol.IsDataConstructor)
                        {
                            unifyRel.AddToTrackSet(rhs, Track_FreeOccurs, t);
                        }
                    });

                return result;
            }

            private class OccursState
            {
                private Term rep;
                private IEnumerator<Term> freeOccIt;

                public OccursState(Term rep, Map<Term, bool> history, Unifier unifier)
                {
                    this.rep = rep;
                    this.freeOccIt = unifier.unifyRel.GetTrackSet(rep, Track_FreeOccurs).GetEnumerator();
                    history.Add(rep, false);
                }

                public bool MoveNext(Unifier unifier, Map<Term, bool> history, out Term u, out bool violated)
                {
                    bool isVisited;
                    while (freeOccIt.MoveNext())
                    {
                        u = unifier.unifyRel.GetRepresentative(freeOccIt.Current);
                        if (history.TryFindValue(u, out isVisited))
                        {
                            if (!isVisited)
                            {
                                violated = true;
                                return false;
                            }

                            continue;
                        }

                        violated = false;
                        return true;
                    }

                    u = null;
                    violated = false;
                    return false;
                }

                public void Finished(Map<Term, bool> history)
                {
                    history[rep] = true;
                }
            }
        }

        /// <summary>
        /// Helps to compile constraints into an evaluation network.
        /// </summary>
        private class ConstraintCompiler
        {
            private FindData[] partialRules = null;

            /// <summary>
            /// The set of all constraints.
            /// </summary>
            private Set<Term> allConstraints = new Set<Term>(Term.Compare);

            /// <summary>
            /// A linear order for compiling comprehensions.
            /// </summary>
            private LinkedList<Term> comprOrder = new LinkedList<Term>();

            /// <summary>
            /// Maps each find variable to a pair (p, t) where any satisfying assignment of the variable
            /// is guarenteed to unify with p and belong to type t.
            /// </summary>
            private Map<Term, Tuple<Term, Term>> findPatterns = new Map<Term, Tuple<Term, Term>>(Term.Compare);

            /// <summary>
            /// Definitions for variables.
            /// </summary>
            private Map<Term, Definition> varDefs = new Map<Term, Definition>(Term.Compare);

            /// <summary>
            /// The congruence classes that are equivalent to a subterm of a find variable.
            /// </summary>
            private Map<CongruenceClass, Term> findRewrites = new Map<CongruenceClass, Term>(CongruenceClass.Compare);

            /// <summary>
            /// Auxilliary variables associated with congruence classes.
            /// </summary>
            private Map<CongruenceClass, Term> auxVars = new Map<CongruenceClass, Term>(CongruenceClass.Compare);

            /// <summary>
            /// Maps auxilliary variables to types.
            /// </summary>
            private Map<Term, Term> auxVarToType = new Map<Term, Term>(Term.Compare);


            public ConstraintSystem Owner
            {
                get;
                private set;
            }

            public ConstraintCompiler(ConstraintSystem owner)
            {
                Contract.Requires(owner != null);
                Owner = owner;
            }

            public void AddConstraint(Term constr)
            {
                allConstraints.Add(constr);
            }

            /// <summary>
            /// Compute the orientations for variables.
            /// TODO: Support cancellation.
            /// </summary>
            /// <param name="cancel"></param>
            public bool Compile(RuleTable rules, out FindData[] parts, List<Flag> flags, CancellationToken cancel)
            {
                if (Owner.IsCompiled != LiftedBool.Unknown)
                {
                    parts = partialRules;
                    return (bool)Owner.IsCompiled;
                }

                parts = null;
                Owner.UpdateFreshVarCounts();

                //// Step 1. Import the definitions for all read vars
                if (!ImportDefinitions(flags, cancel))
                {
                    return (bool)(Owner.IsCompiled = false);
                }

                //// Step 2. Compute the find patterns.
                foreach (var v in Owner.finds.Keys)
                {
                    findPatterns.Add(v, new Tuple<Term, Term>(GetFindPattern(v, findRewrites), GetFindType(v)));
                }

                //// Step 3. Compute the orientations for all remaining variables.
                RecordOrientations();

                //// Step 4. Compile all comprehensions that are not yet compiled.
                bool result = true;
                foreach (var c in comprOrder)
                {
                    var comprData = Owner.comprehensions[c];
                    result = comprData.Compile(rules, flags, cancel);
                }

                /*
                if (result)
                {
                    Console.WriteLine("Body at {0}", Owner.Body.GetCodeLocationString(Owner.Index.Env.Parameters));
                    foreach (var d in varDefs.Values)
                    {
                        d.Debug_PrintDefinition();
                    }
                }
                */ 

                if (result)
                {
                    var opt = new Optimizer(
                                    Owner.Index, 
                                    EnumerateNormalizedConstraints(rules),
                                    findPatterns);
                    parts = partialRules = opt.Optimize(rules, Owner, cancel);
                }

                return (bool)(Owner.IsCompiled = result);
            }

            /// <summary>
            /// If auxVar was introduced by this constraint compiler, then returns
            /// the type of the auxVar.
            /// </summary>
            public bool TryGetAuxType(Term auxVar, out Term type)
            {
                return auxVarToType.TryFindValue(auxVar, out type);
            }

            private bool ImportDefinitions(List<Flag> flags, CancellationToken cancel)
            {
                if (Owner.Comprehension == null)
                {
                    return true;
                }

                Term top;
                Definition def;
                var pending = new Set<Term>(Term.Compare);
                var defined = new Set<Term>(Term.Compare);
                var parentCC = Owner.Comprehension.Owner.cnstrCompiler;
                var equalities = new Map<Term, Node>(Term.Compare);
                foreach (var r in Owner.Comprehension.ReadVars)
                {
                    if (defined.Contains(r.Key))
                    {
                        continue;
                    }

                    pending.Add(r.Key);
                    while (pending.Count > 0)
                    {
                        top = pending.GetSomeElement();
                        pending.Remove(top);
                        defined.Add(top);
                        def = parentCC.varDefs[top];

                        switch (def.Kind)
                        {
                            case Definition.VarDefKind.Find:
                                Owner.finds.Add(top, r.Value);
                                break;
                            case Definition.VarDefKind.Compr:
                                Owner.comprehensions.Add(top, GetComprData(top));
                                break;
                            case Definition.VarDefKind.Normal:
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        foreach (var eq in def.Equalities)
                        {
                            if (!equalities.ContainsKey(eq))
                            {
                                equalities.Add(eq, r.Value);
                            }
                        }

                        foreach (var v in def.DependentVars)
                        {
                            if (!defined.Contains(v) && !pending.Contains(v))
                            {
                                pending.Add(v);
                            }
                        }
                    }
                }

                return Owner.ImportEqualities(equalities, flags, cancel);
            }

            private ComprehensionData GetComprData(Term t)
            {
                var scope = Owner;
                ComprehensionData cdata;
                while (true)
                {
                    if (scope.comprehensions.TryFindValue(t, out cdata))
                    {
                        return cdata;
                    }
                    Contract.Assert(scope.Comprehension != null);
                    scope = scope.Comprehension.Owner;
                }
            }

            /// <summary>
            /// Records variables orientations after definitions of read variables
            /// have been imported.
            /// </summary>
            private void RecordOrientations()
            {
                //// Step 1. Find all the classes that are initially oriented
                Set<Term> congVars;
                CongruenceClass cls, clsp;
                Tuple<CongruenceClass, Term> top;

                //// A stack of oriented classes. If the class is oriented because the 
                //// subterms of some expression e becomes oriented, then the terme is stored.
                //// Otherwise the term element of the tuple is null.
                var stack = new Stack<Tuple<CongruenceClass, Term>>();
                var oriented = new Set<CongruenceClass>(CongruenceClass.Compare);
                var congUses = new Map<CongruenceClass, Set<Term>>(CongruenceClass.Compare);

                //// Record the uselists implied by comprehension reads.
                //// Comprehensions with no parent reads are automatically oriented.
                bool hasReads;
                foreach (var kv in Owner.comprehensions)
                {
                    hasReads = false;
                    foreach (var v in kv.Value.ReadVars.Keys)
                    {
                        //// In this case, a compr is reading a var from a parent
                        //// scope. Its read variable must have been defined at this point.
                        if (!Owner.classes.TryFindValue(v, out cls))
                        {
                            throw new Impossible();
                        }

                        hasReads = true;
                        if (!congUses.TryFindValue(cls, out congVars))
                        {
                            congVars = new Set<Term>(Term.Compare);
                            congUses.Add(cls, congVars);
                        }

                        congVars.Add(kv.Key);
                    }

                    if (!hasReads && !oriented.Contains(cls = Owner.classes[kv.Key].Find()))
                    {
                        oriented.Add(cls);
                        stack.Push(RecordOrientation(cls, kv.Key));
                    }
                }

                //// Find variables are automatically oriented.
                foreach (var kv in Owner.finds)
                {
                    if (!oriented.Contains(cls = Owner.classes[kv.Key].Find()))
                    {
                        oriented.Add(cls);
                        stack.Push(RecordOrientation(cls, kv.Key));
                    }
                }

                //// Data grounded classes are automatically oriented.
                foreach (var kv in Owner.classes)
                {
                    if (oriented.Contains(cls = Owner.classes[kv.Key].Find()))
                    {
                        continue;
                    }

                    if (cls.IsDataGrounded)
                    {
                        oriented.Add(cls);
                        stack.Push(RecordOrientation(cls, kv.Key));
                    }
                }

                //// Step 2. Perform topological sort.
                bool isOriented;
                ComprehensionData data;
                while (stack.Count > 0)
                {
                    top = stack.Pop();
                    cls = top.Item1;
                    Contract.Assert(cls.IsRoot);

                    //// First check if there is congruence class that is now oriented
                    //// because one of its args is now oriented.
                    foreach (var c in cls.Uses)
                    {
                        if (oriented.Contains(clsp = c.Find()))
                        {
                            continue;
                        }

                        foreach (var m in clsp.Members)
                        {
                            if (m.Args.Length == 0)
                            {
                                continue;
                            }

                            isOriented = true;
                            foreach (var a in m.Args)
                            {
                                if (!oriented.Contains(Owner.classes[a].Find()))
                                {
                                    isOriented = false;
                                    break;
                                }
                            }

                            if (isOriented)
                            {
                                oriented.Add(clsp);
                                stack.Push(RecordOrientation(clsp, m));
                                break;
                            }
                        }
                    }

                    //// Next, check if there is a comprehension that is oriented.
                    if (congUses.TryFindValue(cls, out congVars))
                    {
                        foreach (var v in congVars)
                        {
                            if (oriented.Contains(Owner.classes[v].Find()))
                            {
                                continue;
                            }

                            data = Owner.comprehensions[v];
                            isOriented = true;
                            foreach (var u in data.ReadVars.Keys)
                            {
                                clsp = Owner.classes[u].Find();
                                if (!oriented.Contains(clsp))
                                {
                                    isOriented = false;
                                    break;
                                }
                            }

                            if (isOriented && !oriented.Contains(clsp = Owner.classes[v].Find()))
                            {
                                oriented.Add(clsp);
                                stack.Push(RecordOrientation(clsp, v));
                            }
                        }
                    }

                    //// Finally, apply the rule that if f(t1,...,tn) is oriented, then t1, ..., tn are oriented.
                    if (cls.IsSingleton && cls.IsDataGrounded)
                    {
                        continue;
                    }

                    isOriented = false;
                    foreach (var m in cls.Members)
                    {
                        if (!m.Symbol.IsDataConstructor)
                        {
                            continue;
                        }

                        if (!isOriented && top.Item2 != null)
                        {
                            isOriented = true;
                            RecordOrientation(top.Item2, m);
                        }

                        foreach (var a in m.Args)
                        {
                            if (!oriented.Contains(clsp = Owner.classes[a].Find()))
                            {
                                oriented.Add(clsp);
                                stack.Push(new Tuple<CongruenceClass, Term>(clsp, null));
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Record variables whose values must be equal to the evaluation of orienter.
            /// Returns a tuple of the congruence class and its orienter.
            /// </summary>
            private Tuple<CongruenceClass, Term> RecordOrientation(CongruenceClass c, Term orienter)
            {
                Contract.Requires(c != null && orienter != null);

                ComprehensionData cdata;
                Definition def;
                foreach (var m in c.Members)
                {
                    if (!m.Symbol.IsVariable || varDefs.ContainsKey(m))
                    {
                        continue;
                    }

                    if (c.IsDataGrounded)
                    {
                        def = new Definition(m, Definition.VarDefKind.Normal);
                        varDefs.Add(m, def);
                        def.AddEquality(m, c.Type);
                    }
                    else if (Owner.finds.ContainsKey(m))
                    {
                        def = new Definition(m, Definition.VarDefKind.Find);
                        varDefs.Add(m, def);
                        def.AddEquality(m, findPatterns[m].Item1);
                    }
                    else if (Owner.comprehensions.TryFindValue(m, out cdata))
                    {
                        comprOrder.AddLast(m);
                        def = new Definition(m, Definition.VarDefKind.Compr);
                        varDefs.Add(m, def);
                        foreach (var v in cdata.ReadVars.Keys)
                        {
                            def.AddEquality(v, v);
                        }
                    }
                    else
                    {
                        def = new Definition(m, Definition.VarDefKind.Normal);
                        varDefs.Add(m, def);
                        def.AddEquality(m, orienter);
                    }
                }

                return new Tuple<CongruenceClass, Term>(c, c.IsDataGrounded ? c.Type : orienter);
            }

            private Term GetAuxVar(CongruenceClass c)
            {
                Term auxVar;
                if (auxVars.TryFindValue(c, out auxVar))
                {
                    return auxVar;
                }

                auxVar = Owner.MkFreshVar(FreshVarKind.DontCare);
                varDefs.Add(auxVar, new Definition(auxVar, Definition.VarDefKind.Normal));
                auxVars.Add(c, auxVar);
                auxVarToType.Add(auxVar, c.Type);
                return auxVar;
            }

            /// <summary>
            /// Records variables that are oriented because orienter = f_1(...,f_2(...(f_n(...x...)...),...).
            /// </summary>
            private void RecordOrientation(Term orienter, Term dataTerm)
            {
                Contract.Requires(orienter != null && dataTerm != null);
                if (dataTerm.Groundness == Groundness.Ground)
                {
                    return;
                }

                Definition def;
                Term dterm;
                Term[] args;
                Tuple<Term, int> top;
                var symbStack = new Stack<Tuple<Term, int>>();
                dataTerm.Compute<Unit>(
                    (x, s) =>
                    {
                        if (x.Groundness == Groundness.Ground || x.Symbol.Arity == 0)
                        {
                            return null;
                        }
                        else
                        {
                            return EnumerateChildrenWithPos(x, symbStack);
                        }
                    },

                    (x, ch, s) =>
                    {
                        if (x == dataTerm)
                        {
                            Contract.Assert(symbStack.Count == 0);
                            return default(Unit);
                        }
                        else if (x.Symbol.IsVariable && 
                                 !varDefs.ContainsKey(x) && 
                                 !Owner.comprehensions.ContainsKey(x))
                        {
                            bool wasAdded;
                            top = symbStack.Peek();
                            dterm = x;
                            foreach (var p in symbStack)                            
                            {
                                args = new Term[p.Item1.Symbol.Arity];
                                for (int i = 0; i < args.Length; ++i)
                                {
                                    args[i] = i == p.Item2 ? dterm : GetAuxVar(Owner.GetClass(p.Item1.Args[i], null, null));
                                }

                                dterm = Owner.Index.MkApply(p.Item1.Symbol, args, out wasAdded);
                            }

                            def = new Definition(x, Definition.VarDefKind.Normal);
                            def.AddEquality(orienter, dterm);
                            varDefs.Add(x, def);
                        }

                        symbStack.Pop();
                        return default(Unit);
                    });
            }

            private IEnumerable<Term> EnumerateChildrenWithPos(Term t, Stack<Tuple<Term, int>> symbStack)
            {
                Contract.Requires(t.Symbol.IsDataConstructor);

                var i = 0;
                bool foundData;
                CongruenceClass c;
                Tuple<Term, int> pos;
                foreach (var a in t.Args)
                {
                    c = Owner.GetClass(a, null, null);
                    pos = new Tuple<Term, int>(t, i++);
                    foundData = false;
                    foreach (var m in c.Members)
                    {
                        if (t.Groundness == Groundness.Ground)
                        {
                            continue;
                        }
                        else if (m.Symbol.IsVariable)
                        {
                            symbStack.Push(pos);
                            yield return m;
                        }
                        else if (!foundData && m.Symbol.IsDataConstructor)
                        {
                            Contract.Assert(m != t);
                            foundData = true;
                            symbStack.Push(pos);
                            yield return m;
                        }
                    }
                }
            }

            /// <summary>
            /// Widens the find type and removes redundant entries. 
            /// </summary>
            private Term GetFindType(Term findVar)
            {
                Contract.Requires(Owner.finds.ContainsKey(findVar));
                Set<Term> components = new Set<Term>(Term.Compare);

                bool wasAdded;
                var type = Owner.classes[findVar].Find().Type;
                type.Visit(
                    (x) => x.Symbol == Owner.Index.TypeUnionSymbol ? x.Args : null,
                    (x) =>
                    {
                        switch (x.Symbol.Kind)
                        {
                            case SymbolKind.ConSymb:
                                components.Add(Owner.Index.MkApply(((ConSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                                break;
                            case SymbolKind.MapSymb:
                                components.Add(Owner.Index.MkApply(((MapSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                                break;
                            case SymbolKind.UnnSymb:
                                throw new Impossible();
                            default:
                                if (x.Symbol != Owner.Index.TypeUnionSymbol)
                                {
                                    components.Add(x);
                                }
                                break;
                        }
                    });

                type = null;
                foreach (var c in components)
                {
                    type = type == null ? c : Owner.Index.MkApply(Owner.Index.TypeUnionSymbol, new Term[] { c, type }, out wasAdded);
                }

                return type;
            }

            /// <summary>
            /// Returns the find pattern for a find variable.
            /// </summary>
            private Term GetFindPattern(
                Term findVar, 
                Map<CongruenceClass, Term> patternCache)
            {
                Contract.Requires(findVar != null && patternCache != null);
                Contract.Requires(Owner.finds.ContainsKey(findVar));

                Term[] args;
                Term cached;
                CongruenceClass cls;
                UserSymbol dataCon;
                int i;

                return findVar.Compute<Term>(
                    (x, s) =>
                    {
                        cls = Owner.GetClass(x, null, null);
                        if (cls.IsDataGrounded || !cls.HasDataTerm || patternCache.ContainsKey(cls))
                        {
                            return null;
                        }
                        else
                        {
                            foreach (var m in cls.Members)
                            {
                                if (m.Symbol.IsDataConstructor)
                                {
                                    return m.Args;
                                }
                            }

                            throw new Impossible();
                        }
                    },
                    (x, ch, s) =>
                    {
                        cls = Owner.GetClass(x, null, null);
                        if (patternCache.TryFindValue(cls, out cached))
                        {
                            return cached;
                        }

                        if (cls.IsDataGrounded)
                        {
                            Contract.Assert(cls.Type.Groundness == Groundness.Ground);
                            patternCache.Add(cls, cls.Type);
                            return cls.Type;
                        }
                        else if (!cls.HasDataTerm)
                        {
                            foreach (var m in cls.Members)
                            {
                                if (m.Symbol.IsVariable)
                                {
                                    patternCache.Add(cls, m);
                                    return m;
                                }
                            }

                            cached = GetAuxVar(cls);
                            patternCache.Add(cls, cached);
                            return cached;
                        }
                        else
                        {
                            dataCon = null;
                            foreach (var m in cls.Members)
                            {
                                if (m.Symbol.IsDataConstructor)
                                {
                                    dataCon = (UserSymbol)m.Symbol;
                                    break;
                                }
                            }

                            i = 0;
                            args = new Term[dataCon.Arity];
                            foreach (var m in ch)
                            {
                                args[i++] = m;
                            }

                            Contract.Assert(i == dataCon.Arity);
                            bool wasAdded;
                            cached = Owner.Index.MkApply(dataCon, args, out wasAdded);
                            patternCache.Add(cls, cached);
                            return cached;
                        }
                    });
            }

            private IEnumerable<Term> EnumerateNormalizedConstraints(RuleTable rules)
            {
                Term cp;
                var eqSymbol = Owner.Index.SymbolTable.GetOpSymbol(RelKind.Eq);
                var sideEqs = new Set<Term>(Term.Compare);
                bool wasAdded;
                bool rewroteCompr;

                foreach (var kv in findPatterns)
                {
                    allConstraints.Add(Owner.Index.MkApply(eqSymbol, new Term[] { kv.Key, kv.Value.Item1 }, out wasAdded));
                }

                foreach (var c in allConstraints)
                {
                    cp = Normalize(c, eqSymbol, sideEqs, rules, out rewroteCompr);
                    if (cp.Groundness == Groundness.Ground && !rewroteCompr)
                    {
                        continue;
                    }
                    else if (cp.Symbol == eqSymbol)
                    {
                        if (cp.Args[0] == cp.Args[1])
                        {
                            continue;
                        }
                        else if (Term.Compare(cp.Args[0], cp.Args[1]) > 0)
                        {
                            yield return Owner.Index.MkApply(eqSymbol, new Term[] { cp.Args[1], cp.Args[0] }, out wasAdded);
                        }
                        else
                        {
                            yield return cp;
                        }
                    }
                    else
                    {
                        yield return cp;
                    }
                }

                foreach (var c in sideEqs)
                {
                    if (c.Groundness == Groundness.Ground)
                    {
                        continue;
                    }
                    else if (c.Args[0] == c.Args[1])
                    {
                        continue;
                    }
                    else if (Term.Compare(c.Args[0], c.Args[1]) > 0)
                    {
                        yield return Owner.Index.MkApply(eqSymbol, new Term[] { c.Args[1], c.Args[0] }, out wasAdded);
                    }
                    else
                    {
                        yield return c;
                    }
                }
            }

            /// <summary>
            /// Replaces subterms with subterms of find patterns.
            /// </summary>
            private Term Normalize(Term t, Symbol eqSymbol, Set<Term> sideEqs, RuleTable rules, out bool rewroteCompr)
            {
                int i;
                Term tp, result;
                CongruenceClass c;
                ComprehensionData cdata;
                bool wasAdded;
                bool rewriteComprLocal = false;
                var rewrite = t.Compute<Term>(
                    (x, s) =>
                    {
                        if (x.Groundness != Groundness.Variable ||  findPatterns.ContainsKey(x))
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
                        if (x.Groundness != Groundness.Variable || findPatterns.ContainsKey(x))
                        {
                            return x;
                        }

                        if (x.Symbol.Arity == 0)
                        {
                            if (!Owner.classes.TryFindValue(x, out c))
                            {
                                ////  x should be an aux variable introduced into a find pattern.
                                ////  In this case, the variable does not need to be rewritten.
                                Contract.Assert(x.Symbol.IsVariable);
                                return x;
                            }
                            else if (x.Symbol.IsVariable && c.IsDataGrounded)
                            {
                                sideEqs.Add(Owner.Index.MkApply(eqSymbol, new Term[] { x, c.Type }, out wasAdded));
                            }
                            else if (x.Symbol.IsVariable && Owner.comprehensions.TryFindValue(x, out cdata))
                            {
                                rewriteComprLocal = true;
                                return rules.MkComprHead(cdata, Owner.Index.TrueValue); 
                            }

                            if (findRewrites.TryFindValue(c.Find(), out tp))
                            {
                                sideEqs.Add(Owner.Index.MkApply(eqSymbol, new Term[] { x, tp }, out wasAdded));
                                return tp;
                            }
                            else
                            {
                                return x;
                            }
                        }


                        result = null;
                        if (Owner.classes.TryFindValue(x, out c) && findRewrites.TryFindValue(c.Find(), out tp))
                        {
                            result = tp;
                        }

                        i = 0;
                        var args = new Term[x.Symbol.Arity];
                        foreach (var cht in ch)
                        {
                            args[i++] = cht;
                        }

                        tp = Owner.Index.MkApply(x.Symbol, args, out wasAdded);
                        if (result != null)
                        {
                            sideEqs.Add(Owner.Index.MkApply(eqSymbol, new Term[] { result, tp }, out wasAdded));
                            return result;
                        }
                        else
                        {
                            return tp;
                        }
                    });

                rewroteCompr = rewriteComprLocal;
                return rewrite;
            }

            /// <summary>
            /// Holds information needed to fully define a variable in a child scope.
            /// </summary>
            private class Definition
            {
                public enum VarDefKind { Find, Compr, Normal }

                /// <summary>
                /// The variable being defined
                /// </summary>
                public Term Variable
                {
                    get;
                    private set;
                }

                /// <summary>
                /// The kind of variable
                /// </summary>
                public VarDefKind Kind
                {
                    get;
                    private set;
                }

                /// <summary>
                /// A minimal set of equalities required to orient the variable.
                /// </summary>
                public Set<Term> Equalities
                {
                    get;
                    private set;
                }

                /// <summary>
                /// Variables appearing in orienting equalities, excluding this variable.
                /// </summary>
                public Set<Term> DependentVars
                {
                    get;
                    private set;
                }

                public Definition(Term variable, VarDefKind kind)
                {
                    Contract.Requires(variable != null);
                    Variable = variable;
                    Kind = kind;
                    Equalities = new Set<Term>(Term.Compare);
                    DependentVars = new Set<Term>(Term.Compare);
                }

                public void AddEquality(Term lhs, Term rhs)
                {
                    bool wasAdded;
                    var index = Variable.Owner;
                    var eqSymbol = index.SymbolTable.GetOpSymbol(RelKind.Eq);

                    if (Term.Compare(lhs, rhs) > 0)
                    {
                        Equalities.Add(index.MkApply(eqSymbol, new Term[] { rhs, lhs }, out wasAdded));
                    }
                    else
                    {
                        Equalities.Add(index.MkApply(eqSymbol, new Term[] { lhs, rhs }, out wasAdded));
                    }

                    if (lhs.Groundness == Groundness.Variable)
                    {
                        lhs.Visit(
                            x => x.Groundness == Groundness.Variable ? x.Args : null,
                            x =>
                            {
                                if (x != Variable && x.Symbol.IsVariable)
                                {
                                    DependentVars.Add(x);
                                }
                            });
                    }

                    if (rhs.Groundness == Groundness.Variable)
                    {
                        rhs.Visit(
                            x => x.Groundness == Groundness.Variable ? x.Args : null,
                            x =>
                            {
                                if (x != Variable && x.Symbol.IsVariable)
                                {
                                    DependentVars.Add(x);
                                }
                            });
                    }
                }

                public void Debug_PrintDefinition()
                {
                    switch (Kind)
                    {
                        case VarDefKind.Normal:
                            Console.WriteLine("Definition of normal var {0}:", Variable.Symbol.PrintableName);
                            break;
                        case VarDefKind.Find:
                            Console.WriteLine("Definition of find var {0}:", Variable.Symbol.PrintableName);
                            break;
                        case VarDefKind.Compr:
                            Console.WriteLine("Definition of compr var {0}:", Variable.Symbol.PrintableName);
                            break;
                    }

                    foreach (var eq in Equalities)
                    {
                        Console.WriteLine(
                            "\t{0} = {1}", 
                            eq.Args[0].Debug_GetSmallTermString(), 
                            eq.Args[1].Debug_GetSmallTermString());
                    }

                    Console.WriteLine();
                }
            }
        }
    }
}
