namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using API;
    using Common;
    using Common.Extras;
    using Common.Terms;

    using Z3Sort = Microsoft.Z3.Sort;
    using Z3BVSort = Microsoft.Z3.BitVecSort;
    using Z3Expr = Microsoft.Z3.Expr;
    using Z3BoolExpr = Microsoft.Z3.BoolExpr;
    using Z3BVExpr = Microsoft.Z3.BitVecExpr;
    using Z3Symbol = Microsoft.Z3.Symbol;
    using Z3Model = Microsoft.Z3.Model;
    using Z3Context = Microsoft.Z3.Context;
    using Z3Solver = Microsoft.Z3.Solver;
    using Z3BVNum = Microsoft.Z3.BitVecNum;
    using Z3Con = Microsoft.Z3.Constructor;
    using Z3Fun = Microsoft.Z3.FuncDecl;
    using Z3IntExpr = Microsoft.Z3.IntExpr;

    internal class TypeEmbedder
    {
        private Map<Term, AppFreeCanUnn> termToUnn = 
            new Map<Term, AppFreeCanUnn>(Term.Compare);

        private Map<TypePair, Tuple<Term, AppFreeCanUnn>> intrCache =
            new Map<TypePair, Tuple<Term, AppFreeCanUnn>>(TypePair.Compare);

        private Map<Z3Sort, ITypeEmbedding> sortToEmbedding =
            new Map<Z3Sort, ITypeEmbedding>((x, y) => ((int)x.Id) - ((int)y.Id));

        private Map<Term, ITypeEmbedding> typeToEmbedding =
            new Map<Term, ITypeEmbedding>(Term.Compare);

        /// <summary>
        /// Maps a type term to a pair of factorizations (fi, fc):
        /// fi factorizes integer constants, fc factorizes non-integer contants.
        /// Either list may be empty.
        /// </summary>
        private Map<Term, Tuple<LinkedList<ITypeEmbedding>, LinkedList<ITypeEmbedding>>> typeToFactors =
            new Map<Term, Tuple<LinkedList<ITypeEmbedding>, LinkedList<ITypeEmbedding>>>(Term.Compare);

        /// <summary>
        /// Maps a type atom to the list of all types directly containing that atom.
        /// Type embeddings are stored according to their cost.
        /// </summary>
        private Map<Term, Map<int, LinkedList<ITypeEmbedding>>> typeAtomsToEmbeddings =
            new Map<Term, Map<int, LinkedList<ITypeEmbedding>>>(Term.Compare);

        /// <summary>
        /// Maps a type rng to the list of all types containing that rng.
        /// Type embeddings are stored according to their cost.
        /// </summary>
        private Map<Term, Map<int, LinkedList<ITypeEmbedding>>> typeRngsToEmbeddings =
            new Map<Term, Map<int, LinkedList<ITypeEmbedding>>>(Term.Compare);

        public TermIndex Index
        {
            get;
            private set;
        }

        public Z3Context Context
        {
            get;
            private set;
        }

        public TypeEmbedder(
            TermIndex index, 
            Z3Context context, 
            Map<BaseSortKind, uint> baseSortCosts)
        {
            Contract.Requires(index != null && context != null);
            Index = index;
            Context = context;

            //// Build base sorts
            Register(new RealEmbedding(this, baseSortCosts[BaseSortKind.Real]));
            Register(new IntegerEmbedding(this, baseSortCosts[BaseSortKind.Integer]));
            Register(new NaturalEmbedding(this, baseSortCosts[BaseSortKind.Natural]));
            Register(new PosIntegerEmbedding(this, baseSortCosts[BaseSortKind.PosInteger]));
            Register(new NegIntegerEmbedding(this, baseSortCosts[BaseSortKind.NegInteger]));
            Register(new StringEmbedding(this, baseSortCosts[BaseSortKind.String]));

            //// Build finite enumerations
            var sortToIndex = new Map<Term, Tuple<uint, UserSymbol>>(Term.Compare);
            MkEnumTypes(Index.SymbolTable.Root, sortToIndex);
            MkConUnnTypes(sortToIndex);
            SetDefaultValues();
            RegisterEmbeddingAtoms();
        }

        public ITypeEmbedding GetEmbedding(Z3Sort sort)
        {
            return sortToEmbedding[sort];
        }

        public ITypeEmbedding GetEmbedding(BaseSortKind sort)
        {
            bool wasAdded;
            return typeToEmbedding[Index.MkApply(Index.SymbolTable.GetSortSymbol(sort), TermIndex.EmptyArgs, out wasAdded)];
        }

        public ITypeEmbedding GetEmbedding(Term type)
        {
            return typeToEmbedding[type];
        }

        public AppFreeCanUnn GetUnion(Term t)
        {
            Contract.Requires(t != null && t.Groundness != Groundness.Variable);
            AppFreeCanUnn unn;
            if (!termToUnn.TryFindValue(t, out unn))
            {
                unn = new AppFreeCanUnn(t);
                termToUnn.Add(t, unn);
            }

            return unn;
        }

        /// <summary>
        /// Returns null if the intersection is empty.
        /// </summary>
        public AppFreeCanUnn GetIntersection(Term t1, Term t2, out Term intr)
        {
            Tuple<Term, AppFreeCanUnn> intrData;
            var p = new TypePair(t1, t2);
            if (!intrCache.TryFindValue(p, out intrData))
            {
                if (!Index.MkIntersection(t1, t2, out intr))
                {
                    intrData = new Tuple<Term, AppFreeCanUnn>(null, null);
                }
                else
                {
                    intrData = new Tuple<Term, AppFreeCanUnn>(intr, new AppFreeCanUnn(intr));
                }

                intrCache.Add(p, intrData);
            }
            else
            {
                intr = intrData.Item1;
            }

            return intrData.Item2;
        }

        /// <summary>
        /// Returns a set of embeddings that factorize the constants of the union type into
        /// Singleton, IntRange, and Enum embeddings.
        /// </summary>
        public void GetFactorizations(
            Term type,
            out LinkedList<ITypeEmbedding> intFacts,
            out LinkedList<ITypeEmbedding> cnstFacts)
        {
            var factors = typeToFactors[type];
            intFacts = factors.Item1;
            cnstFacts = factors.Item2;
        }

        /// <summary>
        /// Encoding the term t using the embedding.
        /// </summary>
        public Z3Expr MkGround(Term t, ITypeEmbedding embedding)
        {
            Contract.Requires(t != null && t.Groundness == Groundness.Ground);
            Contract.Requires(embedding != null);
            Contract.Requires(Index.IsGroundMember(embedding.Type, t));

            ITypeEmbedding te;
            var embStack = new Stack<ITypeEmbedding>();
            embStack.Push(embedding);
            return t.Compute<Z3Expr>(
                (x, s) =>
                {
                    return EnumerateChildren(x, embStack);
                },
                (x, ch, s) =>
                {
                    te = embStack.Pop();
                    if (te.Kind == TypeEmbeddingKind.Union)
                    {
                        return te.MkGround(null, ch.ToArray(1));
                    }
                    else
                    {
                        return te.MkGround(x.Symbol, ch.ToArray(x.Symbol.Arity));
                    }
                });
        }

        /// <summary>
        /// Decode the ground z3expr t.
        /// </summary>
        public Term MkGround(Z3Expr t)
        {
            Contract.Requires(t != null);
            ITypeEmbedding te;
            var embedding = GetEmbedding(t.Sort);
            var embStack = new Stack<ITypeEmbedding>();
            embStack.Push(embedding);
            return t.Compute<Term>(
                (x, s) =>
                {
                    embedding = embStack.Peek();
                    if (embedding.Kind == TypeEmbeddingKind.Constructor ||
                        embedding.Kind == TypeEmbeddingKind.Union)
                    {
                        return EnumerateChildren(x, embStack);
                    }
                    else
                    {
                        return null;
                    }
                },
                (x, ch, s) =>
                {
                    te = embStack.Pop();
                    if (te.Kind == TypeEmbeddingKind.Union || te.Kind == TypeEmbeddingKind.Constructor)
                    {
                        return te.MkGround(x, ch.ToArray(x.Args.Length));
                    }
                    else
                    {
                        return te.MkGround(x, null);
                    }
                });
        }

        /// <summary>
        /// The unn must contain some constant. 
        /// In this case, returns a constant.
        /// </summary>
        /// <param name="unn"></param>
        /// <returns></returns>
        public Term GetSomeConstant(AppFreeCanUnn unn)
        {
            Contract.Requires(unn != null && unn.ContainsConstants);
            bool wasAdded;
            if (!unn.RangeMembers.IsEmpty())
            {
                var rng = unn.RangeMembers.First();
                return Index.MkCnst(new Rational(rng.Key, BigInteger.One), out wasAdded);
            }

            BaseSortSymb bs;
            foreach (var s in unn.NonRangeMembers)
            {
                switch (s.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                    case SymbolKind.UserCnstSymb:
                        return Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded);
                    case SymbolKind.BaseSortSymb:
                        {
                            bs = (BaseSortSymb)s;
                            switch (bs.SortKind)
                            {
                                case BaseSortKind.Real:
                                case BaseSortKind.Integer:
                                case BaseSortKind.Natural:
                                    return Index.ZeroValue;
                                case BaseSortKind.PosInteger:
                                    return Index.OneValue;
                                case BaseSortKind.NegInteger:
                                    return Index.MkCnst(-Rational.One, out wasAdded);
                                case BaseSortKind.String:
                                    return Index.EmptyStringValue;
                                default:
                                    throw new NotImplementedException();
                            }
                        }               
                    default:
                        continue;
                }
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns a constraint that is true iff the term encoded by left = the term encoded by right.
        /// </summary>
        public Z3BoolExpr MkEquality(Z3Expr left, Z3Expr right)
        {
            var leftTE = GetEmbedding(left.Sort);
            var rightTE = GetEmbedding(right.Sort);
            return leftTE.MkTest(left, rightTE.Type).And(Context, rightTE.MkCoercion(left).Eq(Context, right));
        }

        /// <summary>
        /// Chooses an embedding that can hold all the values inhabiting type.
        /// Prefers types with fewer atoms.
        /// </summary>
        public ITypeEmbedding ChooseRepresentation(Term type)
        {
            Contract.Requires(type != null && type.Groundness != Groundness.Variable);

            //// Choose representation based on widened type. 
            //// If there is an embedding of this widened type, then choose it immediately. 
            int crntMinCost = 0;
            ITypeEmbedding crntMinEmb;
            var wtype = GetUnion(type).MkTypeTerm(Index);
            if (typeToEmbedding.TryFindValue(wtype, out crntMinEmb))
            {
                return crntMinEmb;
            }

            //// Get type terms for base sorts
            bool wasAdded;
            var strType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.String), TermIndex.EmptyArgs, out wasAdded);
            var negType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.NegInteger), TermIndex.EmptyArgs, out wasAdded);
            var posType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.PosInteger), TermIndex.EmptyArgs, out wasAdded);
            var natType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.Natural), TermIndex.EmptyArgs, out wasAdded);
            var intType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.Integer), TermIndex.EmptyArgs, out wasAdded);
            var realType = Index.MkApply(Index.SymbolTable.GetSortSymbol(BaseSortKind.Real), TermIndex.EmptyArgs, out wasAdded);

            //// Begin search for embedding with fewest type atoms
            BaseCnstSymb bcs;
            BaseSortSymb bss;
            BigInteger rngS1, rngE1, rngS2, rngE2;
            Map<int, LinkedList<ITypeEmbedding>> costMap;
            foreach (var t in wtype.Enumerate(x => x.Symbol != Index.RangeSymbol ? x.Args : null))
            {
                if (t.Symbol.Arity == 0)
                {
                    if (typeAtomsToEmbeddings.TryFindValue(t, out costMap))
                    {
                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                    }
                }
                else if (t.Symbol != Index.RangeSymbol)
                {
                    continue;
                }

                switch (t.Symbol.Kind)
                {
                    case SymbolKind.BaseOpSymb:
                        {
                            Contract.Assert(t.Symbol == Index.RangeSymbol);
                            rngS1 = ((Rational)((BaseCnstSymb)t.Args[0].Symbol).Raw).Numerator;
                            rngE1 = ((Rational)((BaseCnstSymb)t.Args[1].Symbol).Raw).Numerator;
                            foreach (var kv in typeRngsToEmbeddings)
                            {
                                rngS2 = ((Rational)((BaseCnstSymb)kv.Key.Args[0].Symbol).Raw).Numerator;
                                rngE2 = ((Rational)((BaseCnstSymb)kv.Key.Args[1].Symbol).Raw).Numerator;
                                if (rngS2 <= rngS1 && rngE1 <= rngE2)
                                {
                                    UpdateMinEmb(wtype, kv.Value, ref crntMinCost, ref crntMinEmb);
                                }
                            }

                            if (typeAtomsToEmbeddings.TryFindValue(negType, out costMap))
                            {
                                UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                            }

                            if (typeAtomsToEmbeddings.TryFindValue(posType, out costMap))
                            {
                                UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                            }

                            if (typeAtomsToEmbeddings.TryFindValue(natType, out costMap))
                            {
                                UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                            }

                            if (typeAtomsToEmbeddings.TryFindValue(intType, out costMap))
                            {
                                UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                            }

                            if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                            {
                                UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                            }

                            break;
                        }
                    case SymbolKind.BaseCnstSymb:
                        {
                            bcs = (BaseCnstSymb)t.Symbol;
                            if (bcs.CnstKind == CnstKind.Numeric)
                            {
                                //// All integers should appear under ranges.
                                Contract.Assert(!((Rational)bcs.Raw).IsInteger);
                                if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                                {
                                    UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                }
                            }
                            else
                            {
                                Contract.Assert(bcs.CnstKind == CnstKind.String);
                                if (typeAtomsToEmbeddings.TryFindValue(strType, out costMap))
                                {
                                    UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                }
                            }

                            break;
                        }
                    case SymbolKind.BaseSortSymb:
                        {
                            bss = (BaseSortSymb)t.Symbol;
                            switch (bss.SortKind)
                            {
                                case BaseSortKind.Real:
                                case BaseSortKind.String:
                                    //// These types are not contained by other types.
                                    break;
                                case BaseSortKind.NegInteger:
                                    if (typeAtomsToEmbeddings.TryFindValue(intType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    break;
                                case BaseSortKind.PosInteger:
                                    if (typeAtomsToEmbeddings.TryFindValue(natType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    if (typeAtomsToEmbeddings.TryFindValue(intType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    break;
                                case BaseSortKind.Natural:
                                    if (typeAtomsToEmbeddings.TryFindValue(intType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    break;
                                case BaseSortKind.Integer:
                                    if (typeAtomsToEmbeddings.TryFindValue(realType, out costMap))
                                    {
                                        UpdateMinEmb(wtype, costMap, ref crntMinCost, ref crntMinEmb);
                                    }

                                    break;
                                default:
                                    throw new NotImplementedException();
                            }

                            break;
                        }

                    default:
                        //// Other atoms do not require special handling
                        break;
                }

                Contract.Assert(crntMinEmb != null);
            }

            Contract.Assert(crntMinEmb != null);
            return crntMinEmb;
        }

        public void Debug_PrintEmbeddings()
        {
            foreach (var kv in typeToEmbedding)
            {
                kv.Value.Debug_Print();
            }
        }

        private void UpdateMinEmb(Term wtype, Map<int, LinkedList<ITypeEmbedding>> sizeMap, ref int minSize, ref ITypeEmbedding minEmb)
        {
            if (sizeMap == null)
            {
                return;
            }
            else if (minEmb == null)
            {
                foreach (var kv in sizeMap)
                {
                    foreach (var e in kv.Value)
                    {
                        if (Index.IsSubtypeWidened(wtype, e.Type))
                        {
                            minSize = kv.Key;
                            minEmb = e;
                            return;
                        }
                    }
                }
            }
            else
            {
                foreach (var kv in sizeMap)
                {
                    if (kv.Key >= minSize)
                    {
                        return;
                    }

                    foreach (var e in kv.Value)
                    {
                        if (Index.IsSubtypeWidened(wtype, e.Type))
                        {
                            minSize = kv.Key;
                            minEmb = e;
                            return;
                        }
                    }
                }
            }
        }

        private IEnumerable<Z3Expr> EnumerateChildren(Z3Expr t, Stack<ITypeEmbedding> embeddingStack)
        {
            for (int i = 0; i < t.Args.Length; ++i)
            {
                embeddingStack.Push(GetEmbedding(t.Args[i].Sort));
                yield return t.Args[i];
            }
        }

        private IEnumerable<Term> EnumerateChildren(Term t, Stack<ITypeEmbedding> embeddingStack)
        {
            if (embeddingStack.Peek().Kind == TypeEmbeddingKind.Union)
            {
                var ue = (UnionEmbedding)embeddingStack.Peek();
                embeddingStack.Push(ue.GetUnboxedEmbedding(t.Symbol));
                yield return t;
            }
            else if (t.Symbol.IsDataConstructor)
            {
                bool wasAdded;
                var type = Index.MkApply(
                            t.Symbol.Kind == SymbolKind.ConSymb ? ((ConSymb)t.Symbol).SortSymbol : ((MapSymb)t.Symbol).SortSymbol,
                            TermIndex.EmptyArgs,
                            out wasAdded);
                var te = (ConstructorEmbedding)GetEmbedding(type);
                for (int i = 0; i < t.Args.Length; ++i)
                {
                    embeddingStack.Push(GetEmbedding(te.Z3Constructor.AccessorDecls[i].Range));
                    yield return t.Args[i];
                }
            }
            else
            {
                Contract.Assert(t.Args.Length == 0);
                yield break;
            }
        }

        private void MkConUnnTypes(Map<Term, Tuple<uint, UserSymbol>> sortToIndex)
        {
            if (sortToIndex.Count == 0)
            {
                return;
            }

            var idToEmbedding = new Map<uint, ITypeEmbedding>((x, y) => ((int)x) - ((int)y));
            foreach (var kv in sortToIndex)
            {
                if (kv.Key.Symbol.Kind == SymbolKind.UserSortSymb)
                {
                    idToEmbedding.Add(
                        kv.Value.Item1,
                        new ConstructorEmbedding(this, ((UserSortSymb)kv.Key.Symbol).DataSymbol, sortToIndex));
                }
                else
                {
                    idToEmbedding.Add(
                        kv.Value.Item1,
                        new UnionEmbedding(this, kv.Key, sortToIndex));
                }
            }

            var sortNames = new string[idToEmbedding.Count];
            var cons = new Z3Con[idToEmbedding.Count][];

            UnionEmbedding ue;
            ConstructorEmbedding ce;
            uint id;
            foreach (var kv in idToEmbedding)
            {
                id = kv.Key;
                if (kv.Value.Kind == TypeEmbeddingKind.Constructor)
                {
                    ce = (ConstructorEmbedding)kv.Value;
                    sortNames[id] = ce.Constructor.FullName;
                    cons[id] = new Z3Con[] { ce.Z3Constructor };
                }
                else
                {
                    ue = (UnionEmbedding)kv.Value;
                    sortNames[id] = ue.Name;
                    cons[id] = ue.Boxers;
                }
            }

            var sorts = Context.MkDatatypeSorts(sortNames, cons);
            foreach (var kv in idToEmbedding)
            {
                if (kv.Value.Kind == TypeEmbeddingKind.Constructor)
                {
                    ce = (ConstructorEmbedding)kv.Value;
                    ce.SetRepresentation(sorts[kv.Key]);
                    Register(ce);
                }
                else
                {
                    ue = (UnionEmbedding)kv.Value;
                    ue.SetRepresentation(sorts[kv.Key]);
                    Register(ue);
                }
            }
        }

        private void MkEnumTypes(Namespace ns, Map<Term, Tuple<uint, UserSymbol>> sortToIndex)
        {
            Term type;
            bool wasAdded;
            UserSortSymb usrSort;
            foreach (var s in ns.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }

                usrSort = s.Kind == SymbolKind.ConSymb ? ((ConSymb)s).SortSymbol : ((MapSymb)s).SortSymbol;
                type = Index.MkApply(usrSort, TermIndex.EmptyArgs, out wasAdded);
                if (!sortToIndex.ContainsKey(type))
                {
                    sortToIndex.Add(
                        type, 
                        new Tuple<uint, UserSymbol>((uint)sortToIndex.Count, (UserSymbol)s));
                }

                for (int i = 0; i < s.Arity; ++i)
                {
                    FactorizeConstants(s, i);
                    type = Index.GetCanonicalTerm(s, i);
                    if (!sortToIndex.ContainsKey(type) && !typeToEmbedding.ContainsKey(type))
                    {
                        sortToIndex.Add(
                            type, 
                            new Tuple<uint, UserSymbol>((uint)sortToIndex.Count, (UserSymbol)s));
                    }
                }
            }

            foreach (var nsp in ns.Children)
            {
                MkEnumTypes(nsp, sortToIndex);
            }
        }

        /// <summary>
        /// Factorizes constants into Enum, IntRange, and Singleton embeddings.
        /// </summary>
        private void FactorizeConstants(UserSymbol s, int index)
        {
            Term type;
            if (typeToFactors.ContainsKey(Index.GetCanonicalTerm(s, index)))
            {
                return;
            }

            bool wasAdded;
            var unn = s.CanonicalForm[index];
            ITypeEmbedding factor;

            //// First, factorize ranges into IntRange and Singleton s.t. each IntRange contains
            //// 2^n constants for n > 0.
            BigInteger lower, upper, aligned, width;
            LinkedList<ITypeEmbedding> intFactors = new LinkedList<ITypeEmbedding>();
            foreach (var r in unn.RangeMembers)
            {
                lower = r.Key;
                upper = r.Value;
                while (lower <= upper)
                {
                    width = upper - lower + 1;
                    if (width == 1)
                    {
                        type = MkRngType(lower, upper);
                        if (!typeToEmbedding.TryFindValue(type, out factor))
                        {
                            Register(factor = new SingletonEmbedding(this, type.Args[0].Symbol));
                        }

                        intFactors.AddLast(factor);
                        break;
                    }
                    else
                    {
                        aligned = lower + BigInteger.Pow(2, (int)width.MostSignificantOne()) - 1;
                        type = MkRngType(lower, aligned);
                        if (!typeToEmbedding.TryFindValue(type, out factor))
                        {
                            Register(factor = new IntRangeEmbedding(this, lower, aligned));
                        }

                        lower = aligned + 1;
                        intFactors.AddLast(factor);
                    }
                }
            }

            //// Second, factorize non-integers into Enum and Singleton s.t. each Enum contains 2^n constants for n > 0.
            //// Need to know the number of constants that will be factorized.
            uint cnstsToFac = 0;
            LinkedList<ITypeEmbedding> cnstFactors = new LinkedList<ITypeEmbedding>();
            foreach (var m in unn.NonRangeMembers)
            {
                if (m.Kind != SymbolKind.BaseCnstSymb && m.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                ++cnstsToFac;
            }

            if (cnstsToFac != 0)
            {
                type = null;
                uint i = 0;
                uint amountToFac = ((uint)1) << (int)cnstsToFac.MostSignificantOne();
                foreach (var m in unn.NonRangeMembers)
                {
                    if (m.Kind != SymbolKind.BaseCnstSymb && m.Kind != SymbolKind.UserCnstSymb)
                    {
                        continue;
                    }

                    ++i;
                    type = type == null
                            ? Index.MkApply(m, TermIndex.EmptyArgs, out wasAdded)
                            : Index.MkApply(
                                    Index.TypeUnionSymbol,
                                    new Term[] { Index.MkApply(m, TermIndex.EmptyArgs, out wasAdded), type },
                                    out wasAdded);

                    if (i < amountToFac)
                    {
                        continue;
                    }
                    else if (amountToFac == 1)
                    {
                        if (!typeToEmbedding.TryFindValue(type, out factor))
                        {
                            Register(factor = new SingletonEmbedding(this, m));
                        }

                        cnstFactors.AddLast(factor);
                        break;
                    }
                    else
                    {
                        if (!typeToEmbedding.TryFindValue(type, out factor))
                        {
                            Register(factor = new EnumEmbedding(this, type, string.Format("{0}_{1}_{2}", s.FullName, index, cnstFactors.Count)));
                        }

                        cnstFactors.AddLast(factor);
                        cnstsToFac -= amountToFac;
                        if (cnstsToFac == 0)
                        {
                            break;
                        }

                        i = 0;
                        type = null;
                        amountToFac = ((uint)1) << (int)cnstsToFac.MostSignificantOne();
                    }
                }
            }

            typeToFactors.Add(
                Index.GetCanonicalTerm(s, index),
                new Tuple<LinkedList<ITypeEmbedding>, LinkedList<ITypeEmbedding>>(intFactors, cnstFactors));
        }

        /// <summary>
        /// Pick default values for all type embeddings.
        /// </summary>
        private void SetDefaultValues()
        {
            var deps = new DependencyCollection<ITypeEmbedding, int>((x, y) => Term.Compare(x.Type, y.Type), (x, y) => x - y);
            var defaultArgs = new Map<ITypeEmbedding, MutableTuple<Term[], int>>((x, y) => Term.Compare(x.Type, y.Type));
            foreach (var kv in typeToEmbedding)
            {
                if (kv.Value.Kind == TypeEmbeddingKind.Constructor)
                {
                    deps.Add(kv.Value);
                    defaultArgs.Add(kv.Value, new MutableTuple<Term[], int>(new Term[((ConstructorEmbedding)kv.Value).Constructor.Arity], 0));
                }
                else if (kv.Value.Kind == TypeEmbeddingKind.Union)
                {
                    deps.Add(kv.Value);
                    defaultArgs.Add(kv.Value, new MutableTuple<Term[], int>(new Term[1], 0));
                }
            }

            bool wasAdded;
            UnionEmbedding ue;
            ConstructorEmbedding ce;
            MutableTuple<Term[], int> args;
            foreach (var kv in typeToEmbedding)
            {
                if (kv.Value.Kind == TypeEmbeddingKind.Union)
                {
                    ue = (UnionEmbedding)kv.Value;
                    args = defaultArgs[ue];
                    if (!ue.CanonicalUnion.ContainsConstants)
                    {
                        args.Item2++;
                        foreach (var s in ue.CanonicalUnion.NonRangeMembers)
                        {
                            if (s.Kind == SymbolKind.UserSortSymb)
                            {
                                deps.Add(
                                    typeToEmbedding[Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded)],
                                    ue,
                                    0);
                            }
                        }
                    }
                }
                else if (kv.Value.Kind == TypeEmbeddingKind.Constructor)
                {
                    ce = (ConstructorEmbedding)kv.Value;
                    args = defaultArgs[ce];
                    for (int i = 0; i < ce.Constructor.Arity; ++i)
                    {
                        if (!ce.Constructor.CanonicalForm[i].ContainsConstants)
                        {
                            args.Item2++;
                            foreach (var s in ce.Constructor.CanonicalForm[i].NonRangeMembers)
                            {
                                if (s.Kind == SymbolKind.UserSortSymb)
                                {
                                    deps.Add(
                                        typeToEmbedding[Index.MkApply(s, TermIndex.EmptyArgs, out wasAdded)],
                                        ce,
                                        i);
                                }
                            }
                        }
                    }
                }
            }

            int n;
            var top = deps.GetTopologicalSort(out n);
            foreach (var d in top)
            {
                if (d.Kind == DependencyNodeKind.Normal)
                {
                    Contract.Assert(defaultArgs[d.Resource].Item2 == 0);
                    if (d.Resource.Kind == TypeEmbeddingKind.Union)
                    {
                        ((UnionEmbedding)d.Resource).SetDefaultMember(defaultArgs[d.Resource].Item1);
                    }
                    else if (d.Resource.Kind == TypeEmbeddingKind.Constructor)
                    {
                        ((ConstructorEmbedding)d.Resource).SetDefaultMember(defaultArgs[d.Resource].Item1);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    foreach (var pr in d.Provides)
                    {
                        args = defaultArgs[pr.Target.Resource];
                        if (args.Item1[pr.Role] == null)
                        {
                            args.Item2--;
                            args.Item1[pr.Role] = d.Resource.DefaultMember.Item1;
                        }
                    }
                }
                else
                {
                    var pending = new Set<DependencyCollection<ITypeEmbedding, int>.IDependencyNode>(
                                                    (x, y) => Term.Compare(x.Resource.Type, y.Resource.Type));

                    foreach (var dp in d.InternalNodes)
                    {
                        if (defaultArgs[dp.Resource].Item2 == 0)
                        {
                            pending.Add(dp);
                        }
                    }

                    while (pending.Count > 0)
                    {
                        var dp = pending.GetSomeElement();
                        pending.Remove(dp);
                        Contract.Assert(defaultArgs[dp.Resource].Item2 == 0);

                        if (dp.Resource.Kind == TypeEmbeddingKind.Union)
                        {
                            ((UnionEmbedding)dp.Resource).SetDefaultMember(defaultArgs[dp.Resource].Item1);
                        }
                        else if (dp.Resource.Kind == TypeEmbeddingKind.Constructor)
                        {
                            ((ConstructorEmbedding)dp.Resource).SetDefaultMember(defaultArgs[dp.Resource].Item1);
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }

                        foreach (var pr in dp.Provides)
                        {
                            args = defaultArgs[pr.Target.Resource];
                            if (args.Item1[pr.Role] == null)
                            {
                                args.Item1[pr.Role] = dp.Resource.DefaultMember.Item1;
                                if ((--args.Item2) == 0 && d.InternalNodes.Contains(pr.Target))
                                {
                                    pending.Add(pr.Target);
                                }
                            }
                        }
                    }
                }
            }

            /*
            foreach (var kv in typeToEmbedding)
            {
                Console.WriteLine(
                    "Default value of {0}: {1}, {2}", 
                    kv.Value.Type.Debug_GetSmallTermString(),
                    kv.Value.DefaultMember.Item1.Debug_GetSmallTermString(),
                    kv.Value.DefaultMember.Item2);
                Console.WriteLine();
            }
            */
        }

        private ITypeEmbedding Register(ITypeEmbedding embedding)
        {
            sortToEmbedding.Add(embedding.Representation, embedding);
            typeToEmbedding.Add(embedding.Type, embedding);           
            return embedding;
        }

        private void RegisterEmbeddingAtoms()
        {
            LinkedList<ITypeEmbedding> embeddings;
            Map<int, LinkedList<ITypeEmbedding>> costMap;
            Map<Term, Map<int, LinkedList<ITypeEmbedding>>> typesToEmbeddings = null;

            foreach (var embedding in sortToEmbedding.Values)
            {
                //// For every atom and range, register this type expression.
                foreach (var t in embedding.Type.Enumerate(x => x.Symbol != Index.RangeSymbol ? x.Args : null))
                {
                    if (t.Symbol.Arity == 0)
                    {
                        typesToEmbeddings = typeAtomsToEmbeddings;
                    }
                    else if (t.Symbol == Index.RangeSymbol)
                    {
                        typesToEmbeddings = typeRngsToEmbeddings;
                    }
                    else
                    {
                        continue;
                    }

                    if (!typesToEmbeddings.TryFindValue(t, out costMap))
                    {
                        costMap = new Map<int, LinkedList<ITypeEmbedding>>((x, y) => x - y);
                        typesToEmbeddings.Add(t, costMap);
                    }

                    if (!costMap.TryFindValue((int)embedding.EncodingCost, out embeddings))
                    {
                        embeddings = new LinkedList<ITypeEmbedding>();
                        costMap.Add((int)embedding.EncodingCost, embeddings);
                    }

                    embeddings.AddLast(embedding);
                }
            }
        }

        private Term MkRngType(BigInteger lower, BigInteger upper)
        {
            Contract.Requires(lower <= upper);
            bool wasAdded;
            return Index.MkApply(
                Index.RangeSymbol,
                new Term[] 
                { 
                    Index.MkCnst(new Rational(lower, BigInteger.One), out wasAdded),
                    Index.MkCnst(new Rational(upper, BigInteger.One), out wasAdded)
                },
                out wasAdded);
        }

        private struct TypePair
        {
            private Term t1;
            private Term t2;

            public Term T1
            {
                get { return t1; }
            }

            public Term T2
            {
                get { return t2; }
            }

            public TypePair(Term t1, Term t2)
            {
                Contract.Requires(t1 != null && t2 != null);
                Contract.Requires(t1.Groundness != Groundness.Variable && t2.Groundness != Groundness.Variable);
                if (Term.Compare(t1, t2) > 0)
                {
                    this.t2 = t1;
                    this.t1 = t2;
                }
                else
                {
                    this.t1 = t1;
                    this.t2 = t2;
                }
            }

            public static int Compare(TypePair p1, TypePair p2)
            {
                var cmp = Term.Compare(p1.t1, p2.t1);
                if (cmp != 0)
                {
                    return cmp;
                }

                return Term.Compare(p1.t2, p2.t2);
            }
        }
    }
}
