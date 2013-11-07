namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Numerics;

    /// <summary>
    /// The pop command causes the previous push command to be removed and the search configuration to be restored.
    /// </summary>
    public struct PopCmd : ISearchCommand
    {
        private string msg;

        public SearchCommandKind Kind
        {
            get { return SearchCommandKind.Pop; }
        }

        public string Message
        {
            get { return msg; }
        }

        public PopCmd(string msg)
        {
            this.msg = msg;
        }
    }
}
