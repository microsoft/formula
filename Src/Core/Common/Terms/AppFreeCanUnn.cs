namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Common.Extras;

    /// <summary>
    /// The canonical form of a type term that does not contain any 
    /// applications of constructors / operators.
    /// </summary>
    internal class AppFreeCanUnn
    {
        private SymbolTable table;
        private AST<Node> typeExpr;
        private Set<Symbol> elements = new Set<Symbol>(Symbol.Compare);
        private IntIntervals intervals = new IntIntervals();

        /// <summary>
        /// A map from unqualified symbol names to the namepaces
        /// where f -> { p1, ..., pn } if this union contains the symbols
        /// p1.f, ... , pn.f. A null value indicates that the map has not 
        /// been computed.
        /// </summary>
        private Map<string, Set<Namespace>> renamingMap = null;
        private SpinLock renamingLock = new SpinLock();

        /// <summary>
        /// If this union was created from a type expr AST, then this
        /// is the source expression. Otherwise, TypeExpr is null.
        /// </summary>
        internal AST<Node> TypeExpr
        {
            get { return typeExpr; }
        }

        /// <summary>
        /// True if this union semantically contains any constants.
        /// </summary>
        internal bool ContainsConstants
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns only the user sorts in the union
        /// </summary>
        internal IEnumerable<UserSortSymb> UserSorts
        {
            get
            {
                foreach (var e in elements)
                {
                    if (e.Kind == SymbolKind.UserSortSymb)
                    {
                        yield return (UserSortSymb)e;
                    }
                }
            }
        }

        /// <summary>
        /// Returns all members in the union that are not finite integer ranges.
        /// </summary>
        internal IEnumerable<Symbol> NonRangeMembers
        {
            get { return elements; }
        }

        /// <summary>
        /// Returns the finite integer ranges in this union.
        /// </summary>
        internal IEnumerable<KeyValuePair<BigInteger, BigInteger>> RangeMembers
        {
            get { return intervals.CanonicalForm; }
        }

        /// <summary>
        /// The number of elements in the canonical form.
        /// </summary>
        internal int CanonicalSize
        {
            get { return elements.Count + intervals.Count; }
        }

        /// <summary>
        /// Constructs a union whose final canonical form is the canonical form of typeTerm.
        /// </summary>
        internal AppFreeCanUnn(SymbolTable table, AST<Node> typeTerm)
        {
            Contract.Requires(typeTerm != null && typeTerm.Node.IsTypeTerm);
            this.table = table;
            this.typeExpr = typeTerm;
        }

        /// <summary>
        /// Constructs a canonical union where the canonical form is s.
        /// </summary>
        internal AppFreeCanUnn(SymbolTable table, Symbol s)
        {
            Contract.Requires(s != null);
            this.table = table;
            this.typeExpr = null;

            if (s.Kind != SymbolKind.BaseCnstSymb)
            {
                elements.Add(s);
                return;
            }

            var bc = (BaseCnstSymb)s;
            if (bc.CnstKind == CnstKind.Numeric &&
                ((Rational)bc.Raw).IsInteger)
            {
                intervals.Add(((Rational)bc.Raw).Numerator, ((Rational)bc.Raw).Numerator);
            }
            else
            {
                elements.Add(s);
            }
        }

        /// <summary>
        /// Constructs an app-free canonical union from t. Widens data constructor
        /// applications into UserSorts. Throws an exception if there are any
        /// BaseOps other than Range and TypeUnn.
        /// </summary>
        internal AppFreeCanUnn(Term t)
        {
            Contract.Requires(t != null);
            this.table = t.Owner.SymbolTable;
            this.typeExpr = table.ModuleData.Source.AST;
            t.Visit(x => x.Symbol.IsTypeUnn ? x.Args : null, Add);
            if (!Canonize(null, null, CancellationToken.None))
            {
                throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Constructs an app-free canonical union from several terms. Widens data constructor
        /// applications into UserSorts. Throws an exception if there are any
        /// BaseOps other than Range and TypeUnn.
        /// </summary>
        internal AppFreeCanUnn(IEnumerable<Term> terms)
        {
            Contract.Requires(terms != null);
            Contract.Requires(!terms.IsEmpty<Term>());
            this.table = terms.First<Term>().Owner.SymbolTable;
            this.typeExpr = table.ModuleData.Source.AST;
            foreach (var t in terms)
            {
                Contract.Assert(table == t.Owner.SymbolTable);
                t.Visit(x => x.Symbol.IsTypeUnn ? x.Args : null, Add);
            }

            if (!Canonize(null, null, CancellationToken.None))
            {
                throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// Returns the set of all namespaces where space.dataName is a member of this
        /// union.
        /// </summary>
        internal bool TryGetRenamings(string dataName, out Set<Namespace> spaces)
        {
            bool gotLock = false;
            try
            {
                renamingLock.Enter(ref gotLock);
                if (renamingMap == null)
                {
                    UserSymbol us;
                    renamingMap = new Map<string, Set<Namespace>>(string.CompareOrdinal);
                    foreach (var s in elements)
                    {
                        if (s.Kind == SymbolKind.UserSortSymb)
                        {
                            us = ((UserSortSymb)s).DataSymbol;
                        }
                        else if (s.IsDerivedConstant)
                        {
                            us = (UserSymbol)s;
                        }
                        else if (s.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)s).IsTypeConstant)
                        {
                            us = (UserSymbol)s;
                        }
                        else
                        {
                            continue;
                        }

                        if (!renamingMap.TryFindValue(us.Name, out spaces))
                        {
                            spaces = new Set<Namespace>(Namespace.Compare);
                            renamingMap.Add(us.Name, spaces);
                        }

                        spaces.Add(us.Namespace);
                    }
                }

                return renamingMap.TryFindValue(dataName, out spaces);
            }
            finally
            {
                if (gotLock)
                {
                    renamingLock.Exit();
                }
            }
        }

        /// <summary>
        /// Makes an expression representing the maximum number of legal terms
        /// inhabiting this union. This number depends on whether the union
        /// appears in a relational context.
        /// </summary>
        internal SizeExpr MkSize(bool isRelational)
        {
            var nNewConstants = intervals.GetSize();
            var sum = new LinkedList<SizeExpr>();
            UserSortSymb uss;
            foreach (var e in NonRangeMembers)
            {
                switch (e.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                        nNewConstants += 1;
                        break;
                    case SymbolKind.BaseSortSymb:
                        return SizeExpr.Infinity;
                    case SymbolKind.UserCnstSymb:
                        if (isRelational && e.IsDerivedConstant)
                        {
                            sum.AddLast(new SizeExpr(((UserCnstSymb)e).FullName));
                        }
                        else 
                        {
                            nNewConstants += 1;
                        }

                        break;
                    case SymbolKind.UserSortSymb:
                        uss = (UserSortSymb)e;
                        Contract.Assert(uss.Size != null);
                        if (isRelational)
                        {
                            sum.AddLast(new SizeExpr(uss.DataSymbol.FullName));
                        }
                        else if (uss.Size.Kind == SizeExprKind.Infinity)
                        {
                            return SizeExpr.Infinity;
                        }
                        else
                        {
                            sum.AddLast(uss.Size);
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return new SizeExpr(nNewConstants, sum);
        }

        /// <summary>
        /// Create a type term from this union. An optional renaming may be applied
        /// to its elements. 
        /// </summary>
        internal Term MkTypeTerm(TermIndex index)
        {
            Contract.Assert(elements.Count > 0 || intervals.Count > 0);
            Term t = null;
            var tunSymb = index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn);
            var noArgs = new Term[0];
            bool wasAdded;
            //// Step 1. Create an enum of all non-integral constants.
            foreach (var e in elements)
            {
                t = t == null ? index.MkApply(e, noArgs, out wasAdded)
                              : index.MkApply(tunSymb, new Term[] { index.MkApply(e, noArgs, out wasAdded), t }, out wasAdded);                               
            }

            //// Step 2. Create an enum of all integer intervals
            var rngSymb = index.SymbolTable.GetOpSymbol(ReservedOpKind.Range);
            Term beg, end;
            foreach (var kv in intervals.CanonicalForm)
            {
                beg = index.MkCnst(new Rational(kv.Key, BigInteger.One), out wasAdded);
                end = index.MkCnst(new Rational(kv.Value, BigInteger.One), out wasAdded);
                t = t == null ? index.MkApply(rngSymb, new Term[] { beg, end }, out wasAdded)
                              : index.MkApply(tunSymb, new Term[] { index.MkApply(rngSymb, new Term[] { beg, end }, out wasAdded), t }, out wasAdded);
            }

            return t;
        }

        /// <summary>
        /// Create a type term from this union. An optional renaming may be applied
        /// to its elements. 
        /// </summary>
        internal AST<Node> MkTypeTerm(Span span, string renaming = null)
        {
            Contract.Assert(elements.Count > 0 || intervals.Count > 0);
            int cnt;
            var bld = new Builder();                 

            //// Step 1. Create an enum of all non-integral constants.
            BuilderRef nonIntRef = BuilderRef.Null;
            foreach (var e in elements)
            {
                if (e.Kind == SymbolKind.BaseCnstSymb)
                {
                    var cnst = (BaseCnstSymb)e;
                    switch (cnst.CnstKind)
                    {
                        case CnstKind.String:
                            bld.PushCnst((string)cnst.Raw, span);
                            break;
                        case CnstKind.Numeric:
                            bld.PushCnst((Rational)cnst.Raw, span);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
                else if (e.Kind == SymbolKind.UserCnstSymb)
                {
                    if (string.IsNullOrEmpty(renaming))
                    {
                        bld.PushId(((UserSymbol)e).FullName, span);
                    }
                    else
                    {
                        var usrCnst = (UserCnstSymb)e;
                        if (usrCnst.UserCnstKind == UserCnstSymbKind.Derived ||
                            (usrCnst.UserCnstKind == UserCnstSymbKind.New && usrCnst.IsTypeConstant))
                        {
                            bld.PushId(string.Format("{0}.{1}", renaming, ((UserSymbol)e).FullName), span);
                        }
                        else
                        {
                            bld.PushId(((UserSymbol)e).FullName, span);
                        }
                    }
                }
            }

            bld.GetStackCount(out cnt);
            if (cnt != 0)
            {
                bld.PushEnum(span);
                for (int i = 0; i < cnt; ++i)
                {
                    bld.AddEnumElement();
                }

                bld.Store(out nonIntRef);
            }

            //// Step 2. Create an enum of all integer intervals
            BuilderRef intrRef = BuilderRef.Null;
            foreach (var kv in intervals.CanonicalForm)
            {
                bld.PushCnst(new Rational(kv.Key, BigInteger.One), span);
                if (kv.Key != kv.Value)
                {
                    bld.PushCnst(new Rational(kv.Value, BigInteger.One), span);
                    bld.PushRange(span);
                }
            }

            bld.GetStackCount(out cnt);
            if (cnt != 0)
            {
                bld.PushEnum(span);
                for (int i = 0; i < cnt; ++i)
                {
                    bld.AddEnumElement();
                }

                bld.Store(out intrRef);
            }

            //// Step 3. Add all the remaining components
            bld.Load(nonIntRef); //// If null, then operation has no effect
            bld.Load(intrRef);
            string baseName;
            foreach (var e in elements)
            {
                switch (e.Kind)
                {
                    case SymbolKind.BaseSortSymb:
                        ASTSchema.Instance.TryGetSortName(((BaseSortSymb)e).SortKind, out baseName);
                        Contract.Assert(!string.IsNullOrEmpty(baseName));
                        bld.PushId(baseName, span);
                        break;
                    case SymbolKind.ConSymb:
                    case SymbolKind.MapSymb:
                    case SymbolKind.UnnSymb:
                        if (!string.IsNullOrEmpty(renaming))
                        {
                            bld.PushId(string.Format("{0}.{1}", renaming, ((UserSymbol)e).FullName), span);
                        }
                        else
                        {
                            bld.PushId(((UserSymbol)e).FullName, span);
                        }

                        break;
                    case SymbolKind.UserSortSymb:
                        if (!string.IsNullOrEmpty(renaming))
                        {
                            bld.PushId(string.Format("{0}.{1}", renaming, ((UserSortSymb)e).DataSymbol.FullName), span);
                        }
                        else
                        {
                            bld.PushId(((UserSortSymb)e).DataSymbol.FullName, span);
                        }

                        break;
                    case SymbolKind.BaseCnstSymb:
                    case SymbolKind.UserCnstSymb:
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            //// Step 4. Build a top-level union if neeeded.
            bld.GetStackCount(out cnt);
            Contract.Assert(cnt > 0);
            if (cnt > 1)
            {
                bld.PushUnion(span);
                for (int i = 0; i < cnt; ++i)
                {
                    bld.AddUnnCmp();
                }
            }

            bld.Close();
            ImmutableArray<AST<Node>> asts;
            var result = bld.GetASTs(out asts);
            Contract.Assert(result && asts != null && asts.Length == 1);
            return asts[0];
        }
      
        /// <summary>
        /// Tests if unn contains synactically the same elements as this union.
        /// </summary>
        internal bool IsEquivalent(AppFreeCanUnn unn)
        {
            Contract.Requires(unn != null);

            if (unn == this)
            {
                return true;
            }
            else if (elements.Count != unn.elements.Count ||
                     intervals.Count != unn.intervals.Count)
            {
                return false;
            }

            using (var it1 = elements.GetEnumerator())
            {
                using (var it2 = unn.elements.GetEnumerator())
                {
                    while (it1.MoveNext() & it2.MoveNext())
                    {
                        if (it1.Current != it2.Current)
                        {
                            return false;
                        }
                    }
                }
            }

            using (var it1 = intervals.CanonicalForm.GetEnumerator())
            {
                using (var it2 = unn.intervals.CanonicalForm.GetEnumerator())
                {
                    while (it1.MoveNext() & it2.MoveNext())
                    {
                        if (it1.Current.Key != it2.Current.Key ||
                            it1.Current.Value != it2.Current.Value)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to resolve all the type name appearing in the type term.
        /// </summary>
        internal bool ResolveTypes(List<Flag> flags, CancellationToken cancel)
        {
            if (typeExpr == null)
            {
                return true;
            }

            var compQuery = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Enum) |
                NodePredFactory.Instance.MkPredicate(NodeKind.Id)
            };

            bool result = true;
            typeExpr.FindAll(
                compQuery,
                (path, node) =>
                {
                    if (node.NodeKind == NodeKind.Id &&
                        (((LinkedList<ChildInfo>)path).Last.Previous == null ||
                        ((LinkedList<ChildInfo>)path).Last.Previous.Value.Node.NodeKind != NodeKind.Enum))
                    {
                        result = AddTypeName((Id)node, flags) & result;
                    }
                    else if (node.NodeKind == NodeKind.Enum)
                    {
                        result = AddEnum((API.Nodes.Enum)node, flags) & result;
                    }
                },
                cancel);

            return result;
        }

        /// <summary>
        /// Returns true if this union accepts all the constants
        /// contains in the base sort
        /// </summary>
        internal bool AcceptsConstants(BaseSortSymb symbol)
        {
            Contract.Requires(symbol != null);

            if (!ContainsConstants)
            {
                return false;
            }
            else if (elements.Contains(symbol))
            {
                return true;
            }

            switch (symbol.SortKind)
            {
                case BaseSortKind.String:
                case BaseSortKind.Real:
                    return false;
                case BaseSortKind.Integer:
                    return elements.Contains(table.GetSortSymbol(BaseSortKind.Real));
                case BaseSortKind.Natural:
                case BaseSortKind.NegInteger:
                    return elements.Contains(table.GetSortSymbol(BaseSortKind.Real)) ||
                           elements.Contains(table.GetSortSymbol(BaseSortKind.Integer));
                case BaseSortKind.PosInteger:
                    return elements.Contains(table.GetSortSymbol(BaseSortKind.Real)) ||
                           elements.Contains(table.GetSortSymbol(BaseSortKind.Integer)) ||
                           elements.Contains(table.GetSortSymbol(BaseSortKind.Natural));
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns true if the union accepts all constants in the range [end1, end2]
        /// </summary>
        internal bool AcceptsConstants(BigInteger end1, BigInteger end2)
        {
            if (!ContainsConstants)
            {
                return false;
            }

            BigInteger lower, upper;
            if (end1 <= end2)
            {
                lower = end1;
                upper = end2;
            }
            else
            {
                lower = end2;
                upper = end1;
            }

            if (elements.Contains(table.GetSortSymbol(BaseSortKind.Real)) ||
                elements.Contains(table.GetSortSymbol(BaseSortKind.Integer)))
            {
                return true;
            }

            if (elements.Contains(table.GetSortSymbol(BaseSortKind.NegInteger)))
            {
                if (lower.Sign < 0 && upper.Sign < 0)
                {
                    return true;
                }
                else if (lower.Sign < 0)
                {
                    lower = BigInteger.Zero;
                }
            }

            if (elements.Contains(table.GetSortSymbol(BaseSortKind.Natural)))
            {
                if (lower.Sign >= 0 && upper.Sign >= 0)
                {
                    return true;
                }
                else if (upper.Sign >= 0)
                {
                    upper = BigInteger.MinusOne;
                }
            }
            else if (elements.Contains(table.GetSortSymbol(BaseSortKind.PosInteger)))
            {
                if (lower.Sign > 0 && upper.Sign > 0)
                {
                    return true;
                }
                else if (upper.Sign > 0)
                {
                    upper = BigInteger.Zero;
                }
            }

            return intervals.Contains(lower, upper);
        }


        /// <summary>
        /// If t is (1) a constant, (2) a range, (3) a base sort, then returns true
        /// if the set of constants is accepted by this type. 
        /// </summary>
        internal bool AcceptsConstants(Term t)
        {
            Contract.Requires(t != null);

            if (!ContainsConstants)
            {
                return false;
            }
            if (t.Symbol.IsNonVarConstant)
            {
                return AcceptsConstant(t.Symbol);
            }
            else if (t.Symbol.Kind == SymbolKind.BaseSortSymb)
            {
                return AcceptsConstants((BaseSortSymb)t.Symbol);
            }

            var rng = t.Symbol as BaseOpSymb;
            if (rng == null || 
                !(rng.OpKind is ReservedOpKind) || 
                ((ReservedOpKind)rng.OpKind) != ReservedOpKind.Range)
            {
                throw new NotImplementedException();
            }

            var lower = (Rational)((BaseCnstSymb)t.Args[0].Symbol).Raw;
            var upper = (Rational)((BaseCnstSymb)t.Args[0].Symbol).Raw;
            return AcceptsConstants(lower.Numerator, upper.Numerator);
        }

        /// <summary>
        /// If this union is in canonical form, then returns true if it semantically contains the constant symb.
        /// </summary>
        /// <param name="symb"></param>
        /// <returns></returns>
        internal bool AcceptsConstant(Symbol symb)
        {
            Contract.Requires(symb != null && symb.IsNonVarConstant);

            if (symb.Kind == SymbolKind.UserCnstSymb)
            {
                return Contains(symb);
            }

            var bc = (BaseCnstSymb)symb;
            if (bc.CnstKind == CnstKind.String)
            {                
                return Contains(symb) || Contains(table.GetSortSymbol(BaseSortKind.String));
            }

            var rat = (Rational)bc.Raw;
            var realSort = table.GetSortSymbol(BaseSortKind.Real);
            if (rat.IsInteger)
            {
                var intSort = table.GetSortSymbol(BaseSortKind.Integer);
                var natSort = table.GetSortSymbol(BaseSortKind.Natural);
                var negSort = table.GetSortSymbol(BaseSortKind.NegInteger);
                var posSort = table.GetSortSymbol(BaseSortKind.PosInteger);

                if (intervals.Contains(rat.Numerator, rat.Numerator) || Contains(realSort) || Contains(intSort))
                {
                    return true;
                }
                else if (rat.Sign < 0)
                {
                    return Contains(negSort);
                }
                else if (rat.Sign == 0)
                {
                    return Contains(natSort);
                }
                else
                {
                    return Contains(posSort) || Contains(natSort);
                }
            }
            else
            {
                return Contains(symb) || Contains(realSort);
            }            
        }

        /// <summary>
        /// True if the canonical form syntactically contains this symbol.
        /// </summary>
        internal bool Contains(Symbol symb)
        {
            if (symb.Kind != SymbolKind.BaseCnstSymb)
            {
                return elements.Contains(symb);
            }

            var bc = (BaseCnstSymb)symb;
            if (bc.CnstKind != CnstKind.Numeric)
            {
                return elements.Contains(symb);
            }

            var rat = (Rational)bc.Raw;
            if (!rat.IsInteger)
            {
                return elements.Contains(symb);
            }

            return intervals.Contains(rat.Numerator, rat.Numerator);
        }

        /// <summary>
        /// Attempts to compute the canonical form.
        /// </summary>
        internal bool Canonize(string myName, List<Flag> flags, CancellationToken cancel, Symbol myself = null)
        {
            if (typeExpr == null)
            {
                return true;
            }
            
            //// Step 1. Replace all unions with their expansions and all
            //// Con/Map symbols with their corresponding sorts.
            var processed = new Set<Symbol>(Symbol.Compare);
            var stack = new Stack<UserSymbol>();

            if (myself != null)
            {
                elements.Remove(myself);
                processed.Add(myself);
            }

            foreach (var s in elements)
            {
                if (s.Kind == SymbolKind.ConSymb ||
                    s.Kind == SymbolKind.MapSymb ||
                    s.Kind == SymbolKind.UnnSymb)
                {
                    processed.Add(s);
                    stack.Push((UserSymbol)s);
                }
            }
            
            AppFreeCanUnn otherCanUnn;
            UserSymbol n;
            while (stack.Count > 0)
            {
                n = stack.Pop();
                elements.Remove(n);

                if (n.Kind == SymbolKind.ConSymb)
                {
                    elements.Add(((ConSymb)n).SortSymbol);
                }
                else if (n.Kind == SymbolKind.MapSymb)
                {
                    elements.Add(((MapSymb)n).SortSymbol);
                }
                else if (n.CanonicalForm != null)
                {
                    elements.UnionWith(n.CanonicalForm[0].elements);
                    intervals.UnionWith(n.CanonicalForm[0].intervals);
                }
                else
                {
                    otherCanUnn = (AppFreeCanUnn)n.Definitions.First<AST<Node>>().Node.CompilerData;
                    intervals.UnionWith(otherCanUnn.intervals);
                    foreach (var sp in otherCanUnn.elements)
                    {
                        if (processed.Contains(sp))
                        {
                            continue;
                        }

                        if (sp.Kind == SymbolKind.ConSymb ||
                            sp.Kind == SymbolKind.MapSymb ||
                            sp.Kind == SymbolKind.UnnSymb)
                        {
                            processed.Add(sp);
                            stack.Push((UserSymbol)sp);
                        }
                        else
                        {
                            elements.Add(sp);
                        }
                    }
                }
            }

            //// Step 2. Apply a set of simplification rules to canonize combinations of base sorts
            var realSort = table.GetSortSymbol(BaseSortKind.Real);
            var intSort = table.GetSortSymbol(BaseSortKind.Integer);
            var natSort = table.GetSortSymbol(BaseSortKind.Natural);
            var negSort = table.GetSortSymbol(BaseSortKind.NegInteger);
            var posSort = table.GetSortSymbol(BaseSortKind.PosInteger);
            var strSort = table.GetSortSymbol(BaseSortKind.String);

            //// PosInteger + {0} = Natural
            if (elements.Contains(posSort) && intervals.Contains(BigInteger.Zero, BigInteger.Zero))
            {
                elements.Add(natSort);
            }
            
            //// Natural + NegInteger = Integer
            if (elements.Contains(negSort) && elements.Contains(natSort))
            {
                elements.Add(intSort);
            }

            //// Removed subsumed sorts.
            if (elements.Contains(realSort))
            {
                intervals.Clear();
                elements.Remove(intSort);
                elements.Remove(natSort);
                elements.Remove(negSort);
                elements.Remove(posSort);
            }
            else if (elements.Contains(intSort))
            {
                intervals.Clear();
                elements.Remove(natSort);
                elements.Remove(negSort);
                elements.Remove(posSort);
            }

            if (elements.Contains(natSort))
            {
                BigInteger min, max;
                if (intervals.GetExtrema(out min, out max))
                {
                    intervals.Remove(BigInteger.Zero, BigInteger.Max(BigInteger.Zero, max));
                }
               
                elements.Remove(posSort);
            }
            else if (elements.Contains(posSort))
            {
                BigInteger min, max;
                if (intervals.GetExtrema(out min, out max))
                {
                    intervals.Remove(BigInteger.One, BigInteger.Max(BigInteger.One, max));
                }
            }

            if (elements.Contains(negSort))
            {
                BigInteger min, max;
                if (intervals.GetExtrema(out min, out max))
                {
                    intervals.Remove(BigInteger.Min(BigInteger.MinusOne, min), BigInteger.MinusOne);
                }
            }

            if (elements.Contains(strSort))
            {
                var dropList = new List<Symbol>();
                foreach (var e in elements)
                {
                    if (e.Kind == SymbolKind.BaseCnstSymb && ((BaseCnstSymb)e).CnstKind == CnstKind.String)
                    {
                        dropList.Add(e);
                    }
                }

                foreach (var e in dropList)
                {
                    elements.Remove(e);
                }
            }

            if (elements.Count == 0 && intervals.Count == 0)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    typeExpr.Node,
                    Constants.BadTypeDecl.ToString(myName, "it has no members"),
                    Constants.BadTypeDecl.Code);
                flags.Add(flag);
                return false;
            }
            else
            {
                if (intervals.Count > 0)
                {
                    ContainsConstants = true;
                }
                else
                {
                    foreach (var e in elements)
                    {
                        if (e.Kind == SymbolKind.BaseCnstSymb ||
                            e.Kind == SymbolKind.UserCnstSymb ||
                            e.Kind == SymbolKind.BaseSortSymb)
                        {
                            ContainsConstants = true;
                            break;
                        }
                    }
                }
            }
            
            return true;
        }

        private void Add(Term t)
        {
            switch (t.Symbol.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                    {
                        var bc = (BaseCnstSymb)t.Symbol;
                        switch (bc.CnstKind)
                        {
                            case CnstKind.String:
                                elements.Add(bc);
                                break;
                            case CnstKind.Numeric:
                                {
                                    var rat = (Rational)bc.Raw;
                                    if (rat.IsInteger)
                                    {
                                        intervals.Add(rat.Numerator, rat.Numerator);
                                    }
                                    else
                                    {
                                        elements.Add(bc);
                                    }

                                    break;
                                }
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    }
                case SymbolKind.UserCnstSymb:
                    {
                        var uc = (UserCnstSymb)t.Symbol;
                        if (uc.IsNonVarConstant)
                        {
                            elements.Add(uc);
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }

                        break;
                    }
                case SymbolKind.BaseOpSymb:
                    {
                        var bop = (BaseOpSymb)t.Symbol;
                        if (!(bop.OpKind is ReservedOpKind))
                        {
                            throw new InvalidOperationException();
                        }

                        var op = (ReservedOpKind)bop.OpKind;
                        if (op == ReservedOpKind.TypeUnn)
                        {
                            //// Do nothing
                        }
                        else if (op == ReservedOpKind.Range)
                        {
                            intervals.Add(
                                ((Rational)((BaseCnstSymb)t.Args[0].Symbol).Raw).Numerator,
                                ((Rational)((BaseCnstSymb)t.Args[1].Symbol).Raw).Numerator);
                        }
                        else
                        {
                            throw new InvalidOperationException();
                        }

                        break;
                    }
                case SymbolKind.UserSortSymb:
                case SymbolKind.UnnSymb:
                case SymbolKind.BaseSortSymb:
                    elements.Add(t.Symbol);
                    break;
                case SymbolKind.ConSymb:
                    elements.Add(((ConSymb)t.Symbol).SortSymbol);
                    break;
                case SymbolKind.MapSymb:
                    elements.Add(((MapSymb)t.Symbol).SortSymbol);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        private bool AddTypeName(Id typeId, List<Flag> flags)
        {
            var symbol = Resolve(typeId, flags, true);
            if (symbol == null)
            {
                return false;
            }

            elements.Add(symbol);
            return true;
        }

        private bool AddEnum(API.Nodes.Enum enm, List<Flag> flags)
        {
            bool result = true;
            Rational r;
            foreach (var e in enm.Elements)
            {
                switch (e.NodeKind)
                {
                    case NodeKind.Cnst:
                        var cnst = (Cnst)e;
                        switch (cnst.CnstKind)
                        {
                            case CnstKind.Numeric:
                                r = (Rational)cnst.Raw;
                                if (r.IsInteger)
                                {
                                    intervals.Add(r.Numerator, r.Numerator);
                                }
                                else
                                {
                                    elements.Add(table.GetCnstSymbol(r));
                                    break;
                                }

                                break;
                            case CnstKind.String:
                                elements.Add(table.GetCnstSymbol((string)cnst.Raw));
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    case NodeKind.Range:
                        var rng = (Range)e;
                        table.GetCnstSymbol(rng.Lower);
                        table.GetCnstSymbol(rng.Upper);
                        intervals.Add(rng.Lower.Numerator, rng.Upper.Numerator);
                        break;
                    case NodeKind.Id:
                        var symbol = Resolve((Id)e, flags, false);
                        if (symbol == null)
                        {
                            result = false;
                        }
                        else if (symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)symbol).IsSymbolicConstant)
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                e,
                                Constants.EnumerationError.ToString(),
                                Constants.EnumerationError.Code));
                            result = false;
                        }
                        else
                        {
                            elements.Add(symbol);
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return result;
        }

        private UserSymbol Resolve(Id id, List<Flag> flags, bool isTypeId)
        {
            UserSymbol other;
            var symbol = table.Resolve(id.Name, out other);
            if (symbol == null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    id,
                    Constants.UndefinedSymbol.ToString(isTypeId ? "type id" : "constant", id.Name),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
            }
            else if (other != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    id,
                    Constants.AmbiguousSymbol.ToString(
                        isTypeId ? "type id" : "constant",
                        id.Name,
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
            }
            else if (isTypeId && 
                     symbol.Kind != SymbolKind.ConSymb &&
                     symbol.Kind != SymbolKind.MapSymb &&
                     symbol.Kind != SymbolKind.BaseSortSymb &&
                     symbol.Kind != SymbolKind.UnnSymb)
            {
                var flag = new Flag(
                            SeverityKind.Error,
                            id,
                            Constants.BadId.ToString(symbol.Name, "type id"),
                            Constants.BadId.Code);
                flags.Add(flag);
            }
            else if (!isTypeId && (symbol.Kind != SymbolKind.UserCnstSymb || symbol.IsVariable))
            {
                var flag = new Flag(
                            SeverityKind.Error,
                            id,
                            Constants.BadId.ToString(symbol.PrintableName, "constant"),
                            Constants.BadId.Code);
                flags.Add(flag);
            }
            else
            {
                return symbol;
            }

            return null;
        }
    }
}
