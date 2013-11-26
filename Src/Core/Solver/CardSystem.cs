namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Compiler;
    using Common;
    using Common.Rules;
    using Common.Terms;
    using Common.Extras;

    using DependencyNode = Microsoft.Formula.Common.DependencyCollection<Microsoft.Formula.Common.Terms.UserSymbol, Microsoft.Formula.Common.Unit>.IDependencyNode;

    /// <summary>
    /// THIS CLASS IS NOT THREAD SAFE. IT IS PRIVATE TO AN INSTANTIATED SEARCH STRATEGY.
    /// 
    /// Gives lower/upper-bounds on the l.f.p. of a partial model subject to additional cardinality constraints.
    /// </summary>
    public class CardSystem
    {
        /// <summary>
        /// Maps a con/map symbol to its nonLFP and LFP cardinality variables and a dependency node. 
        /// Two symbols are mutually recursive iff they map to the same dependency node. 
        /// </summary>
        private Map<UserSymbol, Tuple<DependencyNode, CardVar, CardVar>> varMap =
            new Map<UserSymbol, Tuple<DependencyNode, CardVar, CardVar>>(Symbol.Compare);

        /// <summary>
        /// Collects all the "requires some" into a map
        /// </summary>
        private Map<UserSymbol, int> forcedDoFs = 
            new Map<UserSymbol, int>(Symbol.Compare);  

        /// <summary>
        /// Maps a cardinality var to all the constraints where the variable occurs.
        /// </summary>
        private Map<CardVar, LinkedList<CardConstraint>> useLists = 
            new Map<CardVar, LinkedList<CardConstraint>>(CardVar.Compare);

        private Stack<Map<CardVar, MutableTuple<CardRange>>> solverState =
            new Stack<Map<CardVar, MutableTuple<CardRange>>>();

        private List<CardConstraint> constraints = new List<CardConstraint>();

        /// <summary>
        /// The number of DoFs forced by "requires some"
        /// </summary>
        public IEnumerable<KeyValuePair<UserSymbol, int>> ForcedDoFs
        {
            get { return forcedDoFs; }
        }

        /// <summary>
        /// True if the system is currently unsat. If the initial system is unsat, then will be true
        /// after constructor completes.
        /// </summary>
        public bool IsUnsat
        {
            get;
            private set;
        }

        internal FactSet Facts
        {
            get;
            private set;
        }

        internal CardSystem(FactSet facts)
        {
            Contract.Requires(facts != null);
            Facts = facts;
            

            BuildTypeSystemConstraints();
            BuildCardinalityRequires(facts.Model.Node.Compositions, null);
            BuildPartialModelLowerBounds();

            solverState.Push(new Map<CardVar, MutableTuple<CardRange>>(CardVar.Compare));
            foreach (var v in useLists.Keys)
            {
                if (!Propagate(v))
                {
                    IsUnsat = true;
                    break;
                }
            }
        }

        internal void DebugPrintSolverState()
        {
            foreach (var v in useLists.Keys)
            {
                var rng = GetRange(v);
                Console.WriteLine("{0} : [{1}, {2}]", v, rng.Lower, rng.Upper);
            }
        }

        internal void DebugPrintConstraints()
        {
            foreach (var c in constraints)
            {
                Console.WriteLine(c);
            }
        }

        private CardRange GetRange(CardVar cvar)
        {
            MutableTuple<CardRange> range;
            foreach (var valuations in solverState)
            {
                if (valuations.TryFindValue(cvar, out range))
                {
                    return range.Item1;
                }
            }

            return CardRange.All;
        }

        private void UpdateRange(CardVar cvar, CardRange newRange)
        {
            Contract.Requires(newRange.Lower != Cardinality.Infinity);
            MutableTuple<CardRange> range;
            if (solverState.Peek().TryFindValue(cvar, out range))
            {
                range.Item1 = newRange;
            }
            else
            {
                solverState.Peek().Add(cvar, new MutableTuple<CardRange>(newRange));
            }
        }

        private bool Propagate(CardVar cvar)
        {
            Cardinality cd;
            CardRange lhs, rhs;
            CardRange intr, intrp;
            CardConstraint con;
            var stack = new Stack<CardConstraint>();
            EnqueueConstraints(cvar, stack);
            while (stack.Count > 0)
            {
                con = stack.Pop();
                lhs = GetRange(con.Lhs);
                rhs = Eval(con.Rhs);
                con.IsQueued = false;

                switch (con.Op)
                {
                    case CardConstraint.OpKind.LEq:
                        if ((cd = Cardinality.Min(lhs.Upper, rhs.Upper)) != lhs.Upper)
                        {
                            if (cd < lhs.Lower)
                            {
                                return false;
                            }

                            UpdateRange(con.Lhs, lhs = new CardRange(lhs.Lower, cd));
                            EnqueueConstraints(con.Lhs, stack);
                        }

                        if (rhs.Lower < lhs.Lower)
                        {
                            foreach (var rhsvar in con.RhsVars)
                            {
                                rhs = GetRange(rhsvar);
                                if (Eval(con.Rhs, rhsvar, rhs.Lower, false) >= lhs.Lower)
                                {
                                    continue;
                                }

                                intr = new CardRange(rhs.Lower, Cardinality.Min(lhs.Lower, rhs.Upper));
                                while (intr.Lower + 1 < intr.Upper)
                                {
                                    intrp = intr.Bisect(true);
                                    if (Eval(con.Rhs, rhsvar, intrp.Lower, false) >= lhs.Lower)
                                    {
                                        intr = new CardRange(intr.Lower, intrp.Lower);
                                    }
                                    else
                                    {
                                        intr = new CardRange(intrp.Lower, intr.Upper);
                                    }
                                }

                                if (Eval(con.Rhs, rhsvar, intr.Lower, false) >= lhs.Lower)
                                {
                                    if (intr.Lower > rhs.Lower)
                                    {
                                        UpdateRange(rhsvar, new CardRange(intr.Lower, rhs.Upper));
                                        EnqueueConstraints(rhsvar, stack);
                                        continue;
                                    }
                                }
                                else if (Eval(con.Rhs, rhsvar, intr.Upper, false) >= lhs.Lower)
                                {
                                    if (intr.Upper > rhs.Lower)
                                    {
                                        UpdateRange(rhsvar, new CardRange(intr.Upper, rhs.Upper));
                                        EnqueueConstraints(rhsvar, stack);
                                        continue;
                                    }
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        }

                        break;
                    case CardConstraint.OpKind.GEq:
                        if ((cd = Cardinality.Max(lhs.Lower, rhs.Lower)) != lhs.Lower)
                        {
                            if (cd > lhs.Upper)
                            {
                                return false;
                            }

                            UpdateRange(con.Lhs, lhs = new CardRange(cd, lhs.Upper));
                            EnqueueConstraints(con.Lhs, stack);
                        }

                        if (rhs.Upper > lhs.Upper)
                        {
                            foreach (var rhsvar in con.RhsVars)
                            {
                                rhs = GetRange(rhsvar);
                                if (Eval(con.Rhs, rhsvar, rhs.Upper, true) <= lhs.Upper)
                                {
                                    continue;
                                }

                                intr = new CardRange(rhs.Lower, Cardinality.Min(rhs.Upper, lhs.Upper));
                                while (intr.Lower + 1 < intr.Upper)
                                {
                                    intrp = intr.Bisect(false);
                                    if (Eval(con.Rhs, rhsvar, intrp.Upper, true) <= lhs.Upper)
                                    {
                                        intr = new CardRange(intrp.Upper, intr.Upper);
                                    }
                                    else
                                    {
                                        intr = new CardRange(intr.Lower, intrp.Upper);
                                    }
                                }

                                if (Eval(con.Rhs, rhsvar, intr.Upper, true) <= lhs.Upper)
                                {
                                    if (intr.Upper < rhs.Upper)
                                    {
                                        UpdateRange(rhsvar, new CardRange(rhs.Lower, intr.Upper));
                                        EnqueueConstraints(rhsvar, stack);
                                        continue;
                                    }
                                }
                                else if (Eval(con.Rhs, rhsvar, intr.Lower, true) <= lhs.Upper)
                                {
                                    if (intr.Lower < rhs.Upper)
                                    {
                                        UpdateRange(rhsvar, new CardRange(rhs.Lower, intr.Lower));
                                        EnqueueConstraints(rhsvar, stack);
                                        continue;
                                    }
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }               
            }

            return true;
        }

        private void EnqueueConstraints(CardVar cvar, Stack<CardConstraint> stack)
        {
            LinkedList<CardConstraint> cons;
            if (useLists.TryFindValue(cvar, out cons))
            {
                foreach (var c in cons)
                {
                    if (!c.IsQueued)
                    {
                        c.IsQueued = true;
                        stack.Push(c);
                    }
                }
            }
        }

        private void AddConstraint(CardConstraint con)
        {
            constraints.Add(con);
            GetUseList(con.Lhs).AddLast(con);
            foreach (var cvar in con.RhsVars)
            {
                GetUseList(cvar).AddLast(con);
            }
        }

        private LinkedList<CardConstraint> GetUseList(CardVar cvar)
        {
            LinkedList<CardConstraint> cons;
            if (!useLists.TryFindValue(cvar, out cons))
            {
                cons = new LinkedList<CardConstraint>();
                useLists.Add(cvar, cons);
            }

            return cons;
        }

        private CardRange Eval(CardExpr expr)
        {
            switch (expr.Kind)
            {
                case CardExpr.ExprKind.Cnst:
                    return ((CardCnst)expr).ValueAsRange;
                case CardExpr.ExprKind.Var:
                    return GetRange((CardVar)expr);
                case CardExpr.ExprKind.Prod:
                    return Eval(expr[0]) * Eval(expr[1]);
                case CardExpr.ExprKind.Unn:
                    return Eval(expr[0]) + Eval(expr[1]);
                default:
                    throw new NotImplementedException();
            }
        }

        private Cardinality Eval(CardExpr expr, CardVar cvar, Cardinality val, bool evalOnLower)
        {
            switch (expr.Kind)
            {
                case CardExpr.ExprKind.Cnst:
                    return ((CardCnst)expr).Value;
                case CardExpr.ExprKind.Var:
                    if (expr == cvar)
                    {
                        return val;
                    }
                    else if (evalOnLower)
                    {
                        return GetRange((CardVar)expr).Lower;
                    }
                    else
                    {
                        return GetRange((CardVar)expr).Upper;
                    }
                case CardExpr.ExprKind.Prod:
                    return Eval(expr[0], cvar, val, evalOnLower) * Eval(expr[1], cvar, val, evalOnLower);
                case CardExpr.ExprKind.Unn:
                    return Eval(expr[0], cvar, val, evalOnLower) + Eval(expr[1], cvar, val, evalOnLower);
                default:
                    throw new NotImplementedException();
            }
        }

        private void BuildTypeSystemConstraints()
        {
            //// First, need to find recursive data types. Such types
            //// allow an infinite number of well-typed terms in arguments that are recursive.
            var deps = new DependencyCollection<UserSymbol, Unit>(Symbol.Compare, Unit.Compare);
            BuildDependencies(Facts.Index.SymbolTable.Root, deps);
            int nSCCs;
            var sccs = deps.GetSCCs(out nSCCs);
            foreach (var scc in sccs)
            {
                if (scc.Kind == DependencyNodeKind.Normal)
                {
                    varMap.Add(
                        scc.Resource, 
                        new Tuple<DependencyNode, CardVar, CardVar>(scc, new CardVar(scc.Resource, false), new CardVar(scc.Resource, true)));   
                }
                else 
                {
                    foreach (var n in scc.InternalNodes)
                    {
                        varMap.Add(
                            n.Resource, 
                            new Tuple<DependencyNode, CardVar, CardVar>(scc, new CardVar(n.Resource, false), new CardVar(n.Resource, true)));   
                    }
                }
            }

            //// Second, build cardinality constraints
            CardExpr expr;
            MapSymb map;
            foreach (var kv in varMap)
            {
                expr = MkExpr(kv.Key, kv.Value, 0, kv.Key.Arity);
                AddConstraint(kv.Value.Item2 <= expr);
                AddConstraint(kv.Value.Item3 <= kv.Value.Item2);

                if (kv.Key.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }

                map = (MapSymb)kv.Key;
                AddConstraint(kv.Value.Item3 <= MkExpr(map, kv.Value, 0, map.DomArity));
                if (!map.IsPartial)
                {
                    AddConstraint(kv.Value.Item3 >= MkExpr(map, kv.Value, 0, map.DomArity));
                }

                if (map.MapKind == MapKind.Inj || map.MapKind == MapKind.Bij)
                {
                    AddConstraint(kv.Value.Item3 <= MkExpr(map, kv.Value, map.DomArity, map.CodArity));
                }

                if (map.MapKind == MapKind.Sur || map.MapKind == MapKind.Bij)
                {
                    AddConstraint(kv.Value.Item3 >= MkExpr(map, kv.Value, map.DomArity, map.CodArity));
                }
            }
        }

        private CardExpr MkExpr(UserSymbol symb, Tuple<DependencyNode, CardVar, CardVar> data, int start, int len)
        {
            bool isAnyArg;
            CardExpr prod;
            ConSymb con;
            prod = null;
            for (int i = start; i < start + len; ++i)
            {
                if (symb.Kind == SymbolKind.MapSymb)
                {
                    isAnyArg = ((MapSymb)symb).IsAnyArg(i);
                }
                else
                {
                    con = (ConSymb)symb;
                    isAnyArg = con.IsNew ? con.IsAnyArg(i) : true;
                }

                if (isAnyArg)
                {
                    prod *= MkAnyUnion(data.Item1, symb.CanonicalForm[i]);
                }
                else
                {
                    prod *= MkRelationalUnion(data.Item1, symb.CanonicalForm[i]);
                }
            }

            Contract.Assert(prod != null);
            return prod;
        }

        private CardExpr MkAnyUnion(DependencyNode node, AppFreeCanUnn arg)
        {
            CardExpr expr = MkConstantCount(arg);
            if (expr.IsInfinity)
            {
                return expr;
            }

            foreach (var m in arg.NonRangeMembers)
            {
                if (m.Kind != SymbolKind.UserSortSymb)
                {
                    continue;
                }

                var data = varMap[((UserSortSymb)m).DataSymbol];
                if (data.Item1 == node)
                {
                    return new CardCnst(Cardinality.Infinity);
                }
                else
                {
                    expr += data.Item2;
                }
            }

            return expr;
        }

        private CardExpr MkRelationalUnion(DependencyNode node, AppFreeCanUnn arg)
        {
            CardExpr expr = MkConstantCount(arg);
            if (expr.IsInfinity)
            {
                return expr;
            }

            foreach (var m in arg.NonRangeMembers)
            {
                if (m.Kind != SymbolKind.UserSortSymb)
                {
                    continue;
                }

                var data = varMap[((UserSortSymb)m).DataSymbol];
                Contract.Assert(data.Item1 != node);
                expr += data.Item3;
            }

            return expr;
        }

        /// <summary>
        /// Returns the number of constants accepted by the union
        /// </summary>
        private CardCnst MkConstantCount(AppFreeCanUnn unn)
        {
            BigInteger nCnsts = 0;
            foreach (var nr in unn.NonRangeMembers)
            {
                if (nr.Kind == SymbolKind.BaseCnstSymb || nr.Kind == SymbolKind.UserCnstSymb)
                {
                    ++nCnsts;
                }
                else if (nr.Kind == SymbolKind.BaseSortSymb)
                {
                    return new CardCnst(Cardinality.Infinity);
                }
            }

            foreach (var rng in unn.RangeMembers)
            {
                nCnsts += rng.Value - rng.Key + 1;
            }

            return new CardCnst(new Cardinality(nCnsts));
        }

        private void BuildDependencies(Namespace space, DependencyCollection<UserSymbol, Unit> dependencies)
        {
            foreach (var s in space.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }
                else if (s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew)
                {
                    continue;
                }

                dependencies.Add(s);

                for (int i = 0; i < s.Arity; ++i)
                {
                    var app = s.CanonicalForm[i];
                    foreach (var t in app.NonRangeMembers)
                    {
                        if (t.Kind == SymbolKind.UserSortSymb)
                        {
                            dependencies.Add(((UserSortSymb)t).DataSymbol, s, default(Unit));
                        }
                    }
                }
            }

            foreach (var c in space.Children)
            {
                BuildDependencies(c, dependencies);
            }
        }

        private void BuildCardinalityRequires(ImmutableCollection<ModRef> models, Namespace space)
        {
            foreach (var mr in models)
            {
                var m = (Model)((Location)mr.CompilerData).AST.Node;
                if (!string.IsNullOrEmpty(mr.Rename))
                {
                    Namespace otherSpace;
                    space = Facts.Index.SymbolTable.Resolve(mr.Rename, out otherSpace, space);
                    Contract.Assert(otherSpace == null && space != null);
                }

                int n;
                CardCnst bound;
                API.Nodes.CardPair cp;
                UserSymbol symb, other;
                foreach (var c in m.Contracts)
                {
                    foreach (var cs in c.Specification)
                    {
                        cp = (API.Nodes.CardPair)cs;
                        bound = new CardCnst(new Cardinality(new BigInteger(cp.Cardinality)));
                        symb = Facts.Index.SymbolTable.Resolve(cp.TypeId.Name, out other, space);
                        Contract.Assert(symb != null && other == null);
                        foreach (var con in EnumerateNewConstructors(symb))
                        {
                            switch (c.ContractKind)
                            {
                                case ContractKind.RequiresAtMost:
                                    AddConstraint(varMap[con].Item3 <= bound);
                                    break;
                                case ContractKind.RequiresAtLeast:
                                    AddConstraint(varMap[con].Item3 >= bound);
                                    break;
                                case ContractKind.RequiresSome:
                                    {
                                        if (forcedDoFs.TryFindValue(con, out n))
                                        {
                                            forcedDoFs[con] = Math.Max(n, cp.Cardinality);
                                        }
                                        else
                                        {
                                            forcedDoFs.Add(con, cp.Cardinality);
                                        }

                                        break;
                                    }
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                    }
                }

                if (m.ComposeKind == ComposeKind.Extends)
                {
                    BuildCardinalityRequires(m.Compositions, space);
                }
            }
        }

        /// <summary>
        /// Finds the minimal elements, in the partial order induced by unification, of the partial model facts. 
        /// The number of minimal elements gives a lower bound on the number of distinct facts that must appear in an valid model.
        /// </summary>
        private void BuildPartialModelLowerBounds()
        {
            //// Step 1. Bin facts by head symbol.
            Term pattern;
            LinkedList<Term> gbin;
            Map<Term, MutableTuple<bool>> ngbin; 
            var grdBins = new Map<Symbol, LinkedList<Term>>(Symbol.Compare);

            //// A term maps to true if it is deleted from the bin.
            var ngBins = new Map<Symbol, Map<Term, MutableTuple<bool>>>(Symbol.Compare);
            foreach (var f in Facts.Facts)
            {
                pattern = MkPattern(f);
                if (pattern == f)
                {
                    Contract.Assert(pattern.Groundness == Groundness.Ground);
                    if (!grdBins.TryFindValue(f.Symbol, out gbin))
                    {
                        gbin = new LinkedList<Term>();
                        grdBins.Add(f.Symbol, gbin);
                    }

                    gbin.AddLast(f);
                }
                else
                {
                    Contract.Assert(pattern.Groundness == Groundness.Variable);
                    if (!ngBins.TryFindValue(f.Symbol, out ngbin))
                    {
                        ngbin = new Map<Term, MutableTuple<bool>>(Term.Compare);
                        ngBins.Add(f.Symbol, ngbin);
                    }

                    if (!ngbin.ContainsKey(pattern))
                    {
                        ngbin.Add(pattern, new MutableTuple<bool>(false));
                    }                    
                }
            }

            //// Step 2. Any non-ground fact that matches with a ground fact is not minimal.    
            Matcher matcher;
            foreach (var kv in ngBins)
            {
                ngbin = kv.Value;
                if (!grdBins.TryFindValue(kv.Key, out gbin))
                {
                    continue;
                }

                foreach (var p in ngbin)
                {
                    matcher = new Matcher(p.Key);
                    foreach (var g in gbin)
                    {
                        if (matcher.TryMatch(g))
                        {
                            p.Value.Item1 = true;
                            break;
                        }
                    }
                }

                PruneBin(ngbin);
            }

            //// Step 3. Iteratively compute the minimal elements until a fixpoint is reached.
            Term mgu;
            bool changed;
            LinkedList<Term> newGLBs;
            foreach (var kv in ngBins)
            {
                ngbin = kv.Value;
                changed = true;
                while (changed)
                {
                    changed = false;
                    newGLBs = null;
                    foreach (var p1 in ngbin)
                    {
                        foreach (var p2 in ngbin.GetEnumerable(p1.Key))
                        {
                            if (p1.Key == p2.Key)
                            {
                                continue;
                            }
                            else if (Unifier.IsUnifiable(p1.Key, p2.Key, x => MkNormalizedVar(Facts.Index, x), out mgu))
                            {
                                if (mgu != p1.Key)
                                {
                                    p1.Value.Item1 = true;
                                }

                                if (mgu != p2.Key)
                                {
                                    p2.Value.Item1 = true;
                                }

                                if (mgu != p1.Key && mgu != p2.Key && !ngbin.ContainsKey(mgu))
                                {
                                    changed = true;
                                    if (newGLBs == null)
                                    {
                                        newGLBs = new LinkedList<Term>();
                                    }

                                    newGLBs.AddLast(mgu);
                                }
                            }
                        }
                    }

                    PruneBin(ngbin, newGLBs);
                }
            }

            //// Step 4. Create lower bounds
            Cardinality card;
            foreach (var kv in ngBins)
            {
                grdBins.TryFindValue(kv.Key, out gbin);
                if (gbin == null)
                {
                    card = new Cardinality(new BigInteger(kv.Value.Count));
                }
                else 
                {
                    card = new Cardinality(new BigInteger(kv.Value.Count) + new BigInteger(gbin.Count));
                }

                AddConstraint(varMap[(UserSymbol)kv.Key].Item3 >= new CardCnst(card));
            }

            foreach (var kv in grdBins)
            {
                if (ngBins.ContainsKey(kv.Key))
                {
                    continue;
                }

                AddConstraint(varMap[(UserSymbol)kv.Key].Item3 >= new CardCnst(new Cardinality(new BigInteger(kv.Value.Count))));
            }
        }

        private IEnumerable<UserSymbol> EnumerateNewConstructors(UserSymbol symb)
        {
            switch (symb.Kind)
            {
                case SymbolKind.MapSymb:
                    yield return symb;
                    break;
                case SymbolKind.ConSymb:
                    if (((ConSymb)symb).IsNew)
                    {
                        yield return symb;
                    }
                    break;
                case SymbolKind.UnnSymb:
                    foreach (var s in symb.CanonicalForm[0].NonRangeMembers)
                    {
                        switch (s.Kind)
                        {
                            case SymbolKind.UserSortSymb:
                                yield return ((UserSortSymb)s).DataSymbol;
                                break;
                            default:
                                break;
                        }
                    }

                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static void PruneBin(Map<Term, MutableTuple<bool>> bin, IEnumerable<Term> additions = null)
        {
            var tmp = new LinkedList<Term>();
            foreach (var kv in bin)
            {
                if (!kv.Value.Item1)
                {
                    tmp.AddLast(kv.Key);
                }
            }

            bin.Clear();
            if (additions != null)
            {
                foreach (var t in additions)
                {
                    if (!bin.ContainsKey(t))
                    {
                        bin.Add(t, new MutableTuple<bool>(false));
                    }
                }
            }

            foreach (var t in tmp)
            {
                if (!bin.ContainsKey(t))
                {
                    bin.Add(t, new MutableTuple<bool>(false));
                }
            }
        }

        private Term MkNormalizedVar(TermIndex index, int i)
        {
            bool wasAdded;
            return index.MkVar("~pv~" + i.ToString(), true, out wasAdded);
        }

        private Term MkPattern(Term t)
        {
            int i;
            Term svar;
            bool wasAdded;
            Map<Term, Term> scMap = null;
            return t.Compute<Term>(
               (x, s) => x.Args,
               (x, c, s) =>
               {
                   if (x.Symbol.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)x.Symbol).IsSymbolicConstant)
                   {
                       if (scMap == null)
                       {
                           scMap = new Map<Term, Term>(Term.Compare);
                       }

                       if (!scMap.TryFindValue(x, out svar))
                       {
                           svar = x.Owner.MkVar("~pv~" + scMap.Count.ToString(), true, out wasAdded);
                           scMap.Add(x, svar);
                       }

                       return svar;
                   }
                   else if (x.Symbol.Arity == 0)
                   {
                       return x;
                   }

                   i = 0;
                   foreach (var tp in c)
                   {
                       if (tp != x.Args[i])
                       {
                           break;
                       }

                       ++i;
                   }

                   if (i == x.Args.Length)
                   {
                       return x;
                   }

                   i = 0;
                   var args = new Term[x.Args.Length];
                   foreach (var tp in c)
                   {
                       args[i++] = tp;
                   }

                   return x.Owner.MkApply(x.Symbol, args, out wasAdded);
               });
        }

        private class CardConstraint
        {
            public enum OpKind { LEq, GEq };

            private Set<CardVar> rhsVars = new Set<CardVar>(CardVar.Compare);

            public OpKind Op
            {
                get;
                private set;
            }

            public CardVar Lhs
            {
                get;
                private set;
            }

            public CardExpr Rhs
            {
                get;
                private set;
            }

            /// <summary>
            /// True if this constraint is in a queue awaiting propagation.
            /// </summary>
            public bool IsQueued
            {
                get;
                set;
            }

            public IEnumerable<CardVar> RhsVars
            {
                get { return rhsVars; }
            }

            public CardConstraint(OpKind op, CardVar lhs, CardExpr rhs)
            {
                Contract.Requires(lhs != null && rhs != null);
                Op = op;
                Lhs = lhs;
                Rhs = rhs;
                GetRHSVars(rhs);
            }

            public override string ToString()
            {
                switch (Op)
                {
                    case OpKind.LEq:
                        return string.Format("{0} <= {1}", Lhs, Rhs);
                    case OpKind.GEq:
                        return string.Format("{0} >= {1}", Lhs, Rhs);
                    default:
                        throw new NotImplementedException();
                }
            }

            private void GetRHSVars(CardExpr subexpr)
            {
                if (subexpr.Kind == CardExpr.ExprKind.Var)
                {
                    rhsVars.Add((CardVar)subexpr);
                }
                else
                {
                    for (int i = 0; i < subexpr.Arity; ++i)
                    {
                        GetRHSVars(subexpr[i]);
                    }
                }
            }
        }
     
        private abstract class CardExpr
        {
            public enum ExprKind { Cnst, Var, Unn, Prod };

            public abstract ExprKind Kind
            {
                get;
            }

            public abstract int Arity
            {
                get;
            }

            public abstract CardExpr this[int index]
            {
                get;
            }

            public virtual bool IsInfinity
            {
                get { return false; }
            }

            public virtual bool IsOne
            {
                get { return false; }
            }

            public virtual bool IsZero
            {
                get { return false; }
            }

            public static CardConstraint operator <=(CardVar lhs, CardExpr rhs)
            {
                return new CardConstraint(CardConstraint.OpKind.LEq, lhs, rhs);
            }

            public static CardConstraint operator >=(CardVar lhs, CardExpr rhs)
            {
                return new CardConstraint(CardConstraint.OpKind.GEq, lhs, rhs);
            }
          
            public static CardExpr operator +(CardExpr exp1, CardExpr exp2)
            {
                if (exp1 == null)
                {
                    return exp2;
                }
                else if (exp2 == null)
                {
                    return exp1;
                }
                else if (exp1.IsZero)
                {
                    return exp2;
                }
                else if (exp2.IsZero)
                {
                    return exp1;
                }
                else if (exp1.IsInfinity || exp2.IsInfinity)
                {
                    return new CardCnst(Cardinality.Infinity);
                }
                else if (exp1.Kind == ExprKind.Cnst && exp2.Kind == ExprKind.Cnst)
                {
                    return new CardCnst(((CardCnst)exp1).Value + ((CardCnst)exp2).Value);
                }
                else
                {
                    return new CardDisjUnion(exp1, exp2);
                }
            }

            public static CardExpr operator *(CardExpr exp1, CardExpr exp2)
            {
                if (exp1 == null)
                {
                    return exp2;
                }
                else if (exp2 == null)
                {
                    return exp1;
                }
                else if (exp1.IsOne)
                {
                    return exp2;
                }
                else if (exp2.IsOne)
                {
                    return exp1;
                }
                else if (exp1.IsZero || exp2.IsZero)
                {
                    return new CardCnst(Cardinality.Zero);
                }
                else if (exp1.Kind == ExprKind.Cnst && exp2.Kind == ExprKind.Cnst)
                {
                    return new CardCnst(((CardCnst)exp1).Value * ((CardCnst)exp2).Value);
                }
                else
                {
                    return new CardCartProd(exp1, exp2);
                }
            }
        }

        private class CardVar : CardExpr
        {
            public override ExprKind Kind
            {
                get { return ExprKind.Var; }
            }

            public override int Arity
            {
                get { return 0; }
            }

            public override CardExpr this[int index]
            {
                get { throw new InvalidOperationException(); }
            }

            public CardRange Range
            {
                get;
                private set;
            }

            public UserSymbol Symbol
            {
                get;
                private set;
            }

            /// <summary>
            /// True if this variable stands for the number of provable well-typed Symbol-terms in the LFP.
            /// False if this varable stands for the number of well-typed Symbol-terms that may appear as a subterm in the LFP of a model.
            /// </summary>
            public bool IsLFPCard
            {
                get;
                private set;
            }

            public CardVar(UserSymbol symbol, bool isLFPCard)
            {
                Contract.Requires(symbol != null);
                Symbol = symbol;
                Range = CardRange.All;
                IsLFPCard = isLFPCard;                             
            }

            public static int Compare(CardVar v1, CardVar v2)
            {
                if (v1 == v2)
                {
                    return 0;
                }

                var cmp = Microsoft.Formula.Common.Terms.Symbol.Compare(v1.Symbol, v2.Symbol);
                if (cmp != 0)
                {
                    return cmp;
                }

                if (v1.IsLFPCard && v2.IsLFPCard)
                {
                    return 0;
                }
                else if (!v1.IsLFPCard)
                {
                    return -1;
                }
                else
                {
                    return 1;
                }
            }

            public override string ToString()
            {
                if (IsLFPCard)
                {
                    return string.Format("|lfp({0})|", Symbol.FullName);
                }
                else
                {
                    return string.Format("|{0}|", Symbol.FullName);
                }
            }
        }

        private class CardCnst : CardExpr
        {
            private Cardinality cardVal;
            private CardRange cardValAsRange;

            public override ExprKind Kind
            {
                get { return ExprKind.Cnst; }
            }

            public override int Arity
            {
                get { return 0; }
            }

            public override CardExpr this[int index]
            {
                get { throw new InvalidOperationException(); }
            }

            public override bool IsInfinity
            {
                get { return Value == Cardinality.Infinity; }
            }

            public override bool IsOne
            {
                get { return Value == Cardinality.One; }
            }

            public override bool IsZero
            {
                get { return Value == Cardinality.Zero; }
            }

            public Cardinality Value
            {
                get 
                { 
                    return cardVal; 
                }

                set
                {
                    cardVal = value;
                    cardValAsRange = new CardRange(value, value);
                }
            }

            public CardRange ValueAsRange
            {
                get { return cardValAsRange; }
            }

            public CardCnst(Cardinality value)
            {
                Value = value;
            }

            public override string ToString()
            {
                return Value.ToString();
            }
        }

        private class CardDisjUnion : CardExpr
        {
            public override ExprKind Kind
            {
                get { return ExprKind.Unn; }
            }

            public override int Arity
            {
                get { return 2; }
            }

            public override CardExpr this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return Expr1;
                        case 1:
                            return Expr2;
                        default:
                            throw new InvalidOperationException();
                    }
                }
            }

            public CardExpr Expr1
            {
                get;
                private set;
            }

            public CardExpr Expr2
            {
                get;
                private set;
            }

            public CardDisjUnion(CardExpr expr1, CardExpr expr2)
            {
                Contract.Requires(expr1 != null && expr2 != null);
                Expr1 = expr1;
                Expr2 = expr2;
            }

            public override string ToString()
            {
                return string.Format("{0} + {1}", Expr1, Expr2);
            }
        }

        private class CardCartProd : CardExpr
        {
            public override ExprKind Kind
            {
                get { return ExprKind.Prod; }
            }

            public override int Arity
            {
                get { return 2; }
            }

            public override CardExpr this[int index]
            {
                get
                {
                    switch (index)
                    {
                        case 0:
                            return Expr1;
                        case 1:
                            return Expr2;
                        default:
                            throw new InvalidOperationException();
                    }
                }
            }

            public CardExpr Expr1
            {
                get;
                private set;
            }

            public CardExpr Expr2
            {
                get;
                private set;
            }

            public CardCartProd(CardExpr expr1, CardExpr expr2)
            {
                Contract.Requires(expr1 != null && expr2 != null);
                Expr1 = expr1;
                Expr2 = expr2;
            }

            public override string ToString()
            {
                if (Expr1.Kind == ExprKind.Unn && Expr2.Kind == ExprKind.Unn)
                {
                    return string.Format("({0}) * ({1})", Expr1, Expr2);
                }
                else if (Expr1.Kind == ExprKind.Unn)
                {
                    return string.Format("({0}) * {1}", Expr1, Expr2);
                }
                else if (Expr2.Kind == ExprKind.Unn)
                {
                    return string.Format("{0} * ({1})", Expr1, Expr2);
                }
                else
                {
                    return string.Format("{0} * {1}", Expr1, Expr2);
                }
            }
        }
    }
}
