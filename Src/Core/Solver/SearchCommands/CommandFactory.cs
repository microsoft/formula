namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using Common.Terms;

    public sealed class CommandFactory
    {
        private static readonly CommandFactory theInstance = new CommandFactory();

        public static CommandFactory Instance
        {
            get { return theInstance; }
        }

        public HaltCmd MkHalt(string msg)
        {
            return new HaltCmd(msg);
        }

        public PopCmd MkPop(string msg)
        {
            return new PopCmd(msg);
        }

        public PushCmd MkPush(IEnumerable<Tuple<UserSymbol, uint>> increments, string msg)
        {
            Contract.Requires(increments == null || Contract.ForAll(increments, x => x.Item1.Kind == SymbolKind.ConSymb || x.Item1.Kind == SymbolKind.MapSymb));

            return new PushCmd(increments, msg);
        }        
    }
}
