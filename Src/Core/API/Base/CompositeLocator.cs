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
        private Locator[] args;

        public override int Arity
        {
            get { return args.Length; }
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

        public CompositeLocator(Span span, Locator[] args)
        {
            Contract.Requires(args != null);
            this.span = span;
            this.args = args;
        }
    }
}
