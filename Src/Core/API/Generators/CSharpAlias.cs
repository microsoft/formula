namespace Microsoft.Formula.API.Generators
{
    using System;

    internal class CSharpAlias : ICSharpTerm
    {
        public object Symbol
        {
            get;
            private set;
        }

        public int Arity
        {
            get { return 0; }
        }

        public ICSharpTerm this[int index] 
        { 
            get { throw new InvalidOperationException(); } 
        }

        public Span Span
        {
            get;
            set;
        }

        public CSharpAlias(string alias)
        {
            Symbol = alias;
        }
    }
}
