namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using Common;
    using Common.Terms;

    /// <summary>
    /// The push command saves the state of the current configuration and then starts a new state were the DOFs of each
    /// symbol are incremented according to Increments (plus inference). The Push command succeeds if it is possible for the increments to 
    /// be distinctly instatiated. Otherwise the increments correspond to redundant effort.
    /// </summary>
    public sealed class PushCmd : ISearchCommand
    {
        private Map<UserSymbol, uint> aggIncrements = new Map<UserSymbol, uint>(Symbol.Compare);

        public string Message
        {
            get;
            private set;
        }

        public SearchCommandKind Kind
        {
            get { return SearchCommandKind.Push; }
        }

        /// <summary>
        /// Returns the set of non-zero increments. If the same symbol was incremented multiple times
        /// in the same push operation, then these increments are added together. 
        /// </summary>
        public IEnumerable<KeyValuePair<UserSymbol, uint>> Increments
        {
            get { return aggIncrements; }
        }

        internal PushCmd(IEnumerable<Tuple<UserSymbol, uint>> increments, string msg)
        {
            Message = msg;
            if (increments == null)
            {
                return;
            }

            uint crntInc;
            foreach (var inc in increments)
            {
                if (aggIncrements.TryFindValue(inc.Item1, out crntInc))
                {
                    aggIncrements[inc.Item1] = crntInc + inc.Item2;
                }
                else if (inc.Item2 != 0)
                {
                    aggIncrements.Add(inc.Item1, inc.Item2);
                }
            }
        }
    }
}
