namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;

    using API;

    public sealed class Term
    {
        private long uid = -1;
        internal const int FamilyNumeric = 0;
        internal const int FamilyString = 1;
        internal const int FamilyUsrCnst = 2;
        internal const int FamilyApp = 3;

        public Groundness Groundness
        {
            get;
            private set;
        }

        public Symbol Symbol
        {
            get;
            private set;
        }

        public ImmutableArray<Term> Args
        {
            get;
            private set;
        }

        /// <summary>
        /// An equivalence relation on terms according to the kind of symbol.
        /// For convenience UserSorts are grouped into the AppFamily and BaseSorts are also grouped into their natural family.
        /// </summary>
        internal int Family
        {
            get
            {
                switch (Symbol.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                        {
                            var bc = (BaseCnstSymb)Symbol;
                            switch (bc.CnstKind)
                            {
                                case CnstKind.Numeric:
                                    return FamilyNumeric;
                                case CnstKind.String:
                                    return FamilyString;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                    case SymbolKind.BaseSortSymb:
                        {
                            return ((BaseSortSymb)Symbol).SortKind == BaseSortKind.String ? FamilyString : FamilyNumeric;
                        }
                    case SymbolKind.UserCnstSymb:
                        return FamilyUsrCnst;
                    default:
                        return FamilyApp;
                }
            }
        }

        internal TermIndex Owner
        {
            get;
            private set;
        }

        internal long UId
        {
            get
            {
                Contract.Assert(uid != -1);
                return uid;
            }

            set
            {
                Contract.Assert(uid == -1);
                uid = value;
            }
        }

        internal Term(Symbol symbol, Term[] args, TermIndex owner)
        {
            Contract.Requires(args != null && args.Length == symbol.Arity);
            Owner = owner;
            Symbol = symbol;
            Args = new ImmutableArray<Term>(args);

            if (symbol.Arity == 0)
            {
                switch (symbol.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                        Groundness = Groundness.Ground;
                        break;
                    case SymbolKind.BaseSortSymb:
                    case SymbolKind.UnnSymb:
                    case SymbolKind.UserSortSymb:
                        Groundness = Groundness.Type;
                        break;
                    case SymbolKind.UserCnstSymb:
                        Groundness = symbol.IsVariable ? Groundness.Variable : Groundness.Ground;
                        break;
                    case SymbolKind.ConSymb:
                    case SymbolKind.MapSymb:
                        throw new InvalidOperationException();
                    default:
                        throw new NotImplementedException();
                }
            }
            else if (symbol == owner.TypeRelSymbol)
            {
                Contract.Assert(args[0].Groundness != Groundness.Type);
                Contract.Assert(args[1].Groundness != Terms.Groundness.Variable);
                Groundness = args[0].Groundness;
            }
            else
            {
                Groundness = Groundness.Ground;
                foreach (var a in args)
                {
                    if (a.Groundness == Groundness.Variable)
                    {
                        Contract.Assert(Groundness != Groundness.Type);
                        Groundness = Groundness.Variable;
                    }
                    else if (a.Groundness == Groundness.Type)
                    {
                        Contract.Assert(Groundness != Groundness.Variable);
                        Groundness = Groundness.Type;
                    }
                }

                if (symbol == owner.RangeSymbol || symbol == owner.TypeUnionSymbol)
                {
                    Contract.Assert(Groundness != Groundness.Variable);
                    Groundness = Groundness.Type;
                }
            }        
        }

        public static int Compare(Term t1, Term t2)
        {
            if (t1.uid < t2.uid)
            {
                return -1;
            }
            else if (t1.uid > t2.uid)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public static bool IsSymbolicTerm(Term t)
        {
            if (t.Groundness == Groundness.Variable ||
                t.Symbol.IsSymCount)
            {
                return true;
            }

            return false;
        }

        public static bool IsSymbolicTerm(Term t1, Term t2)
        {
            if (t1.Groundness == Groundness.Variable ||
                t2.Groundness == Groundness.Variable ||
                t1.Symbol.IsSymCount ||
                t2.Symbol.IsSymCount)
            {
                return true;
            }

            return false;
        }
        
        public void PrintTerm(
                    TextWriter wr, 
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)

        {
            TermPrinting.PrintTerm(this, wr, cancel, envParams);
        }

        /// <summary>
        /// Expects this to be a type term. If not an InvalidOperationException will be thrown.
        /// </summary>
        public void PrintTypeTerm(
                    TextWriter wr, 
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)
        {
            TermPrinting.PrintTypeTerm(this, wr, cancel, envParams);
        }

        /// <summary>
        /// Expects this to be a type term. If not an InvalidOperationException will be thrown.
        /// </summary>
        public string PrintTypeTerm(
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)
        {
            var sw = new StringWriter();
            TermPrinting.PrintTypeTerm(this, sw, cancel, envParams);
            return sw.ToString();
        }

        public int LexicographicCompare(Term t)
        {
            Contract.Requires(t != null && t.Owner == Owner);
            if (this == t)
            {
                return 0;
            }
            else if (Symbol != t.Symbol)
            {
                return Symbol.Id - t.Symbol.Id;
            }

            var s1 = new Stack<TermState>();
            var s2 = new Stack<TermState>();
            s1.Push(new TermState(this));
            s2.Push(new TermState(t));

            int n1, n2;
            Term t1, t2;
            while (s1.Count > 0 && s2.Count > 0)
            {
                n1 = s1.Peek().MoveState();
                n2 = s2.Peek().MoveState();
                if (n1 == TermState.End)
                {
                    Contract.Assert(n2 == TermState.End);
                    s1.Pop();
                    s2.Pop();
                    continue;
                }

                t1 = s1.Peek().Term.Args[n1];
                t2 = s2.Peek().Term.Args[n2];
                if (t1.Symbol.Id != t2.Symbol.Id)
                {
                    return t1.Symbol.Id - t2.Symbol.Id;
                }

                s1.Push(new TermState(t1));
                s2.Push(new TermState(t2));
            }

            return 0;
        }

        /// <summary>
        /// Performs an AST computation over a terms by unfold and fold operations.
        /// If a token is provided and then Failed() during computation, then computation
        /// is immediately canceled and default(S) is returned.
        /// 
        /// Compute(t, unfold, fold) = 
        /// fold(t, Compute(t_1, unfold, fold), ... , Compute(t_1, unfold, fold)) 
        /// 
        /// where:
        /// t_1 ... t_n are returned by unfold(t)
        /// </summary>
        public S Compute<S>(
            Func<Term, SuccessToken, IEnumerable<Term>> unfold,
            Func<Term, IEnumerable<S>, SuccessToken, S> fold,
            SuccessToken token = null)
        {
            Term t;
            Compute1State<S> top;
            var stack = new Stack<Compute1State<S>>();
            stack.Push(new Compute1State<S>(null, this, unfold(this, token)));
            if (token != null && !token.Result)
            {
                return default(S);
            }

            while (stack.Count > 0)
            {
                top = stack.Peek();
                if (top.GetNext(out t))
                {
                    stack.Push(new Compute1State<S>(top, t, unfold(t, token)));
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
                else
                {
                    if (top.Parent == null)
                    {
                        Contract.Assert(stack.Count == 1);
                        return fold(top.T, top.ChildrenValues, token);
                    }

                    top.Parent.ChildrenValues.AddLast(fold(top.T, top.ChildrenValues, token));
                    stack.Pop();
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
            }

            throw new Impossible();
        }

        /// <summary>
        /// Performs an AST computation over two terms by unfold and fold operations.
        /// Let A be this term and B be the other term. Let a \in A and b \in B.
        /// If a token is provided and then Failed() during computation, then computation
        /// is immediately canceled and default(S) is returned.
        /// 
        /// Then:
        /// Compute(a, b, unfold, fold) = 
        /// fold(a, b, Compute(a_1, b_1), ... Compute(a_n, b_n)) 
        /// 
        /// where:
        /// a_1 ... a_n are returned by the first enumerator of unfold(a, b)
        /// b_1 ... b_n are returned by the second enumerator of unfold(a, b).
        /// 
        /// If one enumerator enumerates fewer elements, then the remaining
        /// elements are null.
        /// </summary>
        public S Compute<S>(
            Term other,
            Func<Term, Term, SuccessToken, Tuple<IEnumerable<Term>, IEnumerable<Term>>> unfold,
            Func<Term, Term, IEnumerable<S>, SuccessToken, S> fold,
            SuccessToken token = null)
        {
            Term a, b;
            Compute2State<S> top;
            var stack = new Stack<Compute2State<S>>();
            stack.Push(new Compute2State<S>(null, this, other, unfold(this, other, token)));
            if (token != null && !token.Result)
            {
                return default(S);
            }

            while (stack.Count > 0)
            {
                top = stack.Peek();
                if (top.GetNext(out a, out b))
                {
                    stack.Push(new Compute2State<S>(top, a, b, unfold(a, b, token)));
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
                else
                {
                    if (top.Parent == null)
                    {
                        Contract.Assert(stack.Count == 1);
                        return fold(top.A, top.B, top.ChildrenValues, token);
                    }

                    top.Parent.ChildrenValues.AddLast(fold(top.A, top.B, top.ChildrenValues, token));
                    stack.Pop();
                    if (token != null && !token.Result)
                    {
                        return default(S);
                    }
                }
            }

            throw new Impossible();
        }

        /// <summary>
        /// Returns all the unfolded terms as an enumerator
        /// </summary>
        public IEnumerable<Term> Enumerate(Func<Term, IEnumerable<Term>> unfold)
        {
            var stack = new Stack<IEnumerator<Term>>();
            yield return this;
            var ures = unfold(this);
            if (ures != null)
            {
                stack.Push(ures.GetEnumerator());
            }
            
            IEnumerator<Term> enm;
            while (stack.Count > 0)
            {
                enm = stack.Peek();
                if (enm != null && enm.MoveNext())
                {
                    yield return enm.Current;
                    ures = unfold(enm.Current);
                    if (ures != null)
                    {
                        stack.Push(ures.GetEnumerator());
                    }
                }
                else
                {
                    if (enm != null)
                    {
                        enm.Dispose();
                    }

                    stack.Pop();
                }
            }
        }

        /// <summary>
        /// Applies the visitor to all unfolded terms.
        /// t.Visit(unfold, visitor) = visitor(t); s.Visit(unfold, visitor) for s \in unfold(t).
        /// </summary>
        public void Visit(
            Func<Term, IEnumerable<Term>> unfold, 
            Action<Term> visitor,
            SuccessToken token = null)
        {
            IEnumerable<Term> ures;
            var stack = new Stack<IEnumerator<Term>>();
            visitor(this);
            ures = unfold(this);
            if (ures == null || (token != null && !token.Result))
            {
                return;
            }

            stack.Push(ures.GetEnumerator());
            IEnumerator<Term> enm;
            while (stack.Count > 0)
            {
                enm = stack.Peek();
                if (enm != null && enm.MoveNext())
                {
                    visitor(enm.Current);
                    ures = unfold(enm.Current);

                    if (token != null && !token.Result)
                    {
                        return;
                    }
                    else if (ures != null)
                    {
                        stack.Push(ures.GetEnumerator());
                    }
                }
                else
                {
                    if (enm != null)
                    {
                        enm.Dispose();
                    }

                    stack.Pop();
                }
            }
        }

        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            TermPrinting.PrintTerm(this, sw, default(System.Threading.CancellationToken), null);
            return sw.ToString();
        }

        public override int GetHashCode()
        {
            return uid.GetHashCode();
        }

        private class Compute1State<S>
        {
            private IEnumerator<Term> it;

            public Term T
            {
                get;
                private set;
            }

            public Compute1State<S> Parent
            {
                get;
                private set;
            }

            public LinkedList<S> ChildrenValues
            {
                get;
                private set;
            }

            public Compute1State(Compute1State<S> parent, Term t, IEnumerable<Term> unfolding)
            {
                T = t;
                Parent = parent;
                it = unfolding == null ? null : unfolding.GetEnumerator();
                ChildrenValues = new LinkedList<S>();
            }

            public bool GetNext(out Term t)
            {
                if (it != null)
                {
                    if (it.MoveNext())
                    {
                        t = it.Current;
                        return true;
                    }
                    else
                    {
                        t = null;
                        it = null;
                        return false;
                    }
                }

                t = null;
                return false;
            }
        }

        private class Compute2State<S>
        {
            private IEnumerator<Term> itA;
            private IEnumerator<Term> itB;

            public Term A
            {
                get;
                private set;
            }

            public Term B
            {
                get;
                private set;
            }

            public Compute2State<S> Parent
            {
                get;
                private set;
            }

            public LinkedList<S> ChildrenValues
            {
                get;
                private set;
            }

            public Compute2State(Compute2State<S> parent, Term a, Term b, Tuple<IEnumerable<Term>, IEnumerable<Term>> unfolding)
            {
                A = a;
                B = b;
                Parent = parent;
                if (unfolding == null)
                {
                    itA = itB = null;
                }
                else
                {
                    itA = unfolding.Item1 == null ? null : unfolding.Item1.GetEnumerator();
                    itB = unfolding.Item2 == null ? null : unfolding.Item2.GetEnumerator();
                }

                ChildrenValues = new LinkedList<S>();
            }   

            public bool GetNext(out Term a, out Term b)
            {
                bool result = false;
                if (itA != null)
                {
                    if (itA.MoveNext())
                    {
                        result = true;
                        a = itA.Current;
                    }
                    else
                    {
                        a = null;
                        itA = null;
                    }
                }
                else
                {
                    a = null;
                }


                if (itB != null)
                {
                    if (itB.MoveNext())
                    {
                        result = true;
                        b = itB.Current;
                    }
                    else
                    {
                        b = null;
                        itB = null;
                    }
                }
                else
                {
                    b = null;
                }

                return result;
            }         
        }
    }
}
