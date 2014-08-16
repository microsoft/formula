namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Common;
    using Common.Terms;
    using Compiler;
    using Nodes;

    /// <summary>
    /// A locator given by a span, program, and array of sub-locators
    /// </summary>
    internal class CompositeLocator : Locator
    {
        private Span span;
        private ProgramName program;
        private Locator[] args;

        public override int Arity
        {
            get { return args.Length; }
        }

        public override ProgramName Program
        {
            get { return program; }
        }

        public override Span Span
        {
            get { return span; }
        }

        public override Locator this[int index]
        {
            get 
            {
                Contract.Assert(index >= 0 && index < args.Length);
                return args[index];
            }
        }

        public CompositeLocator(Span span, ProgramName program, Locator[] args)
        {
            Contract.Requires(program != null && args != null);
            this.span = span;
            this.program = program;
            this.args = args;
        }
    }
}
