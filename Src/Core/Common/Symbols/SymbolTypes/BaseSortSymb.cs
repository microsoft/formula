namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using Compiler;

    public sealed class BaseSortSymb : Symbol
    {
        public override SymbolKind Kind
        {
            get { return SymbolKind.BaseSortSymb; }
        }

        public override int Arity
        {
            get { return 0; }
        }

        public BaseSortKind SortKind
        {
            get;
            private set;
        }

        public override string PrintableName
        {
            get 
            {
                string name;
                if (!API.ASTQueries.ASTSchema.Instance.TryGetSortName(SortKind, out name))
                {
                    throw new NotImplementedException();
                }

                return name;
            }
        }
       
        internal BaseSortSymb(BaseSortKind sortKind)      
        {
            SortKind = sortKind;
        }
    }
}
