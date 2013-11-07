namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Compiler;
    using Extras;
    using Terms;

    internal struct FindData
    {
        private Term binding;
        private Term pattern;
        private Term type;

        public bool IsNull
        {
            get { return binding == null; }
        }

        public Term Binding
        {
            get { return binding; }
        }

        public Term Pattern
        {
            get { return pattern; }
        }

        public Term Type
        {
            get { return type; }
        }

        public FindData(Term binding, Term pattern, Term type)
        {
            Contract.Requires(binding != null && pattern != null && type != null);

            this.binding = binding;
            this.pattern = pattern;
            this.type = type;
        }

        public Term MkFindTerm(TermIndex index)
        {
            if (IsNull)
            {
                return index.FalseValue;
            }
            else if (Binding.Symbol.IsReservedOperation)
            {
                //// In this case, do not create a find(...) term.
                return Binding;
            }

            bool wasAdded;
            return index.MkApply(
                index.SymbolTable.GetOpSymbol(ReservedOpKind.Find),
                new Term[] { Binding, Pattern, index.MkApply(index.TypeRelSymbol, new Term[] { Pattern, Type }, out wasAdded) },
                out wasAdded);
        }
    }
}
