namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Threading;

    using API;

    public sealed class UserSortSymb : Symbol
    {
        private SizeExpr sizeExpr = null;

        internal SizeExpr Size
        {
            get
            {
                return sizeExpr;
            }

            set
            {
                Contract.Assert(sizeExpr == null);
                sizeExpr = value;
            }
        }

        public override string PrintableName
        {
            get { return DataSymbol.PrintableName; }
        }

        public override SymbolKind Kind
        {
            get { return SymbolKind.UserSortSymb; }
        }

        public override int Arity
        {
            get { return 0; }
        }

        public UserSymbol DataSymbol
        {
            get;
            private set;
        }

        internal UserSortSymb(UserSymbol dataSymbol)
        {
            Contract.Assert(dataSymbol != null);
            DataSymbol = dataSymbol;
        }
    }
}
