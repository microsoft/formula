namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Text;
    using System.Threading;

    using Common;
    using Common.Terms;
    using Compiler;
    using Nodes;

    /// <summary>
    /// A locator assigns line numbers to a term and its subterms, including terms that were derived from rules.
    /// </summary>
    public sealed class Locator
    {
        private static readonly Locator[] EmptyArgs = new Locator[0];

        private static readonly ProgramName unknownProgram =
            new ProgramName(string.Format("{0}/unknown", ProgramName.EnvironmentScheme));

        /// <summary>
        /// Spin lock for args
        /// </summary>
        private SpinLock argsLock = new SpinLock();

        /// <summary>
        /// If null, then sublocators are stored in location.
        /// </summary>
        private Locator[] args;

        /// <summary>
        /// If null, then this locator and its sublocators are specified
        /// by Node in ProgramName from FactSet. The ProgramName in location
        /// may differ if this locator is an expanded symbolic constant.
        /// </summary>
        private Tuple<Node, ProgramName, FactSet> location;

        /// <summary>
        /// The maximum distance from this term to a subterm that appeared in some input model.
        /// The smaller the synthetic distance, the more precisely this locator corresponds to 
        /// a specific place in an input model.
        /// </summary>
        public int SyntheticDistance
        {
            get;
            private set;
        }

        /// <summary>
        /// The arity of the term corresponding to this locator.
        /// </summary>
        public int Arity
        {
            get
            {
                if (location != null)
                {
                    if (location.Item1.NodeKind == NodeKind.FuncTerm)
                    {
                        return ((FuncTerm)location.Item1).Args.Count;
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return args.Length;
                }
            }
        }

        /// <summary>
        /// Gets the locators of this locators subterms
        /// </summary>
        public Locator this[int index]
        {
            get
            {
                Contract.Requires(index >= 0 && index < Arity);
                if (location == null)
                {
                    return args[index];
                }

                bool gotLock = false;
                try
                {
                    argsLock.Enter(ref gotLock);
                    if (args != null)
                    {
                        return args[index];
                    }

                    var ft = (FuncTerm)location.Item1;
                    args = new Locator[ft.Args.Count];
                    int i = 0;
                    foreach (var a in ft.Args)
                    {
                        args[i] = ExpandLocation(a, location.Item2, location.Item3);
                        ++i;
                    }

                    return args[index];
                }
                finally
                {
                    if (gotLock)
                    {
                        argsLock.Exit();
                    }
                }
            }
        }

        /// <summary>
        /// A span that is related to this term.
        /// </summary>
        public Span Span
        {
            get;
            private set;
        }

        /// <summary>
        /// The program where the related span occurs.
        /// </summary>
        public ProgramName Program
        {
            get;
            private set;
        }

        internal static ProgramName UnknownProgram
        {
            get { return unknownProgram; }
        }

        /// <summary>
        /// Constructors the location (0, 0, 0, 0) in the UnknownProgram with synthetic distance 1.
        /// </summary>
        internal Locator()
        {
            Span = new Span(0, 0, 0, 0);
            Program = unknownProgram;
            args = EmptyArgs;
            location = null;
            SyntheticDistance = 1;
        }

        /// <summary>
        /// Constructors a locator with synthetic distance max(distance(args)) + 1
        /// </summary>
        internal Locator(Span span, ProgramName program, Locator[] args)
        {
            Contract.Requires(program != null && args != null);
            Span = span;
            Program = program;
            this.args = args;
            location = null;

            SyntheticDistance = 0;
            for (int i = 0; i < args.Length; ++i)
            {
                SyntheticDistance = Math.Max(SyntheticDistance, args[i].SyntheticDistance);
            }

            ++SyntheticDistance;
        }

        /// <summary>
        /// Constructs a locator with synthetic distance of zero. The span may differ from node.Span
        /// if this is the location of a symbolic constant. In this case, the span will locate the
        /// symbolic constant, whereas node will locate its expansion into a term.
        /// </summary>
        internal Locator(Span span, ProgramName program, Node node, ProgramName nodeProgram, FactSet source)
        {
            Contract.Requires(node != null && program != null && source != null && nodeProgram != null);

            if (node.NodeKind == NodeKind.ModelFact)
            {
                node = ((ModelFact)node).Match;
            }

            Program = program;
            Span = span;
            SyntheticDistance = 0;
            location = new Tuple<Node, ProgramName, FactSet>(node, nodeProgram, source);
            args = null;
        }

        public static int Compare(Locator l1, Locator l2)
        {
            bool isFullyEqual;
            var cmp = CompareLocal(l1, l2, out isFullyEqual);
            if (cmp != 0 || isFullyEqual)
            {
                return cmp;
            }

            int expandArg;
            Locator c1, c2;
            Locator p1, p2;

            MutableTuple<Locator, Locator, int> top;
            var stack = new Stack<MutableTuple<Locator, Locator, int>>();
            stack.Push(new MutableTuple<Locator, Locator, int>(l1, l2, 0));
            while (stack.Count > 0)
            {
                top = stack.Peek();
                p1 = top.Item1;
                p2 = top.Item2;
                expandArg = top.Item3;

                if (expandArg >= p1.Arity)
                {
                    Contract.Assert(expandArg == p1.Arity);
                    stack.Pop();
                }
                else
                {
                    c1 = p1[expandArg];
                    c2 = p2[expandArg];
                    if ((cmp = CompareLocal(c1, c2, out isFullyEqual)) != 0)
                    {
                        return cmp;
                    }

                    top.Item3++;
                    if (!isFullyEqual)
                    {
                        stack.Push(new MutableTuple<Locator, Locator, int>(c1, c2, 0));
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Chooses the lexicographically smallest program name, and then produces a span containing
        /// all the spans occuring in that program name. Returns null if there are no locators.
        /// </summary>
        internal static Tuple<Span, ProgramName> Widen(IEnumerable<Locator> locators)
        {
            if (locators == null || Common.Extras.EnumerableMethods.IsEmpty(locators))
            {
                return null;
            }

            var widened = default(Span);
            ProgramName smallest = null;
            foreach (var l in locators)
            {
                if (smallest == null || string.Compare(l.Program.ToString(), smallest.ToString()) < 0)
                {
                    smallest = l.Program;
                    widened = l.Span;
                }
            }

            foreach (var l in locators)
            {
                if (l.Program.ToString() != smallest.ToString())
                {
                    continue;
                }

                widened = new Span(
                    Math.Min(widened.StartLine, l.Span.StartLine),
                    Math.Min(widened.StartCol, l.Span.StartCol),
                    Math.Max(widened.EndLine, l.Span.EndLine),
                    Math.Max(widened.EndCol, l.Span.EndCol));
            }

            return new Tuple<Span, ProgramName>(widened, smallest);
        }

        private static Locator ExpandLocation(Node node, ProgramName nodeProgram, FactSet source)
        {
            if (node.NodeKind == NodeKind.Id && node.CompilerData is UserCnstSymb)
            {
                var cnst = (UserCnstSymb)node.CompilerData;
                if (cnst.IsSymbolicConstant)
                {
                    Locator loc;
                    var result = source.TryGetLocator(node.Span, nodeProgram, cnst, out loc);
                    Contract.Assert(result);
                    return loc;
                }
            }

            return new Locator(node.Span, nodeProgram, node, nodeProgram, source);
        }

        /// <summary>
        /// Compares only the data local to locators l1 and l2. Out parameter isFullyEqual = true
        /// if l1 and l2 can be determined to be equal. 
        /// </summary>
        private static int CompareLocal(Locator l1, Locator l2, out bool isFullyEqual)
        {
            //// If same object, then fully equal.
            if (l1 == l2)
            {
                isFullyEqual = true;
                return 0;
            }
            else if (l1.location != null && l2.location != null)
            {
                //// Can use the underlying Node objects to test for equality.
                if (Span.Compare(l1.Span, l2.Span) == 0 && 
                    l1.Program.ToString() == l2.Program.ToString() &&
                    l1.location.Item1 == l2.location.Item1 &&
                    l1.location.Item2.ToString() == l2.location.Item2.ToString() &&
                    l1.location.Item3 == l2.location.Item3)
                {
                    isFullyEqual = true;
                    return 0;
                }
            }

            isFullyEqual = false;
            if (l1.Arity != l2.Arity)
            {
                return l1.Arity < l2.Arity ? -1 : 1;
            }

            var cmp = string.Compare(l1.Program.ToString(), l2.Program.ToString());
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = Span.Compare(l1.Span, l2.Span);
            if (cmp != 0)
            {
                return cmp;
            }

            isFullyEqual = (l1.Arity == 0);
            return 0;
        }
    }
}
