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

    public sealed class BaseCnstSymb : Symbol
    {
        public override SymbolKind Kind
        {
            get { return SymbolKind.BaseCnstSymb; }
        }

        public override bool IsNewConstant
        {
            get { return true; }
        }

        public override bool IsNonVarConstant
        {
            get { return true; }
        }

        public override string PrintableName
        {
            get 
            {
                switch (CnstKind)
                {
                    case CnstKind.Numeric:
                        return Raw.ToString();
                    case CnstKind.String:
                        return string.Format("\"{0}\"", Raw);
                    default:
                        throw new NotImplementedException();                           
                }
            }
        }

        public CnstKind CnstKind
        {
            get;
            private set;
        }

        public object Raw
        {
            get;
            private set;
        }

        public override int Arity
        {
            get { return 0; }
        }

        internal BaseCnstSymb(Rational r)
        {
            CnstKind = CnstKind.Numeric;
            Raw = r;
        }

        internal BaseCnstSymb(string s)
        {
            CnstKind = CnstKind.String;
            Raw = s == null ? string.Empty : s;
        }
    }
}
