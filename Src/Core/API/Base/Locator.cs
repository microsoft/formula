namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Text;
    using System.Threading;

    using Common.Terms;
    using Compiler;
    using Nodes;

    /// <summary>
    /// A locator assigns line numbers to a term and its subterms, including terms that were derived from rules.
    /// </summary>
    public sealed class Locator
    {
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
        /// by Node in ProgramName from FactSet.
        /// </summary>
        private Tuple<Node, FactSet> location;

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
                        args[i] = ExpandLocation(a, Program, location.Item2);
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
        internal Locator(Node node, Span span, ProgramName program, FactSet source)
        {
            Contract.Requires(node != null && program != null && source != null);
            if (node.NodeKind == NodeKind.ModelFact)
            {
                node = ((ModelFact)node).Match;
            }

            Program = program;
            Span = span;
            SyntheticDistance = 0;
            location = new Tuple<Node, FactSet>(node, source);
            args = null;
        }

        private static Locator ExpandLocation(Node node, ProgramName program, FactSet source)
        {
            if (node.NodeKind == NodeKind.Id && node.CompilerData is UserCnstSymb)
            {
                var cnst = (UserCnstSymb)node.CompilerData;
                if (cnst.IsSymbolicConstant)
                {
                    Locator loc;
                    var result = source.TryGetLocator(node, cnst, out loc);
                    Contract.Assert(result);
                    return loc;
                }
            }

            return new Locator(node, node.Span, program, source);
        }
    }
}
