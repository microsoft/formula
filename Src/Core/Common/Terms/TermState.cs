namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;

    internal class TermState
    {
        internal const int Start = -1;
        internal const int End = -2;

        private int argPos = Start;

        public Term Term
        {
            get;
            private set;
        }

        public int ArgPos
        {
            get { return argPos; }
        }

        public int MoveState()
        {
            Contract.Requires(ArgPos != End);
            ++argPos;
            if (argPos >= Term.Symbol.Arity)
            {
                argPos = End;
            }

            return argPos;
        }

        public TermState(Term term)
        {
            Term = term;
        }
    }
}
