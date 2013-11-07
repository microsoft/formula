namespace Microsoft.Formula.Solver
{
    using System;
    using System.Diagnostics.Contracts;

    /// <summary>
    /// The halt command causes search to terminate the moment it is encountered.
    /// </summary>
    public struct HaltCmd : ISearchCommand
    {
        private string msg;

        public SearchCommandKind Kind
        {
            get { return SearchCommandKind.Halt; }
        }

        public string Message
        {
            get { return msg; }
        }

        public HaltCmd(string msg)
        {
            this.msg = msg;
        }
    }
}
