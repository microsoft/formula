namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;

    using API;
    using API.Nodes;

    using Common.Terms;

    internal class ModuleData
    {
        internal enum PhaseKind
        {
            Reduced = 0,
            TypesDefined = 1,
            Compiled = 2
        }

        /// <summary>
        /// True if this model is only for the purposes of holding a query.
        /// </summary>
        internal bool IsQueryContainer
        {
            get;
            private set;
        }

        internal Location Source
        {
            get;
            private set;
        }

        internal AST<Node> Reduced
        {
            get;
            private set;
        }

        internal PhaseKind Phase
        {
            get;
            private set;
        }

        internal SymbolTable SymbolTable
        {
            get;
            private set;
        }

        internal object FinalOutput
        {
            get;
            private set;
        }

        internal Env Env
        {
            get;
            private set;
        }

        internal ModuleData(Env env, Location source, Node reduced, bool isQueryContainer)
        {          
            Contract.Requires(source.AST != null && reduced != null && env != null);
            Env = env;
            Source = source;
            Reduced = Factory.Instance.ToAST(reduced);
            Phase = PhaseKind.Reduced;
            IsQueryContainer = isQueryContainer;
        }

        internal void PassedPhase(PhaseKind phase, object compilerObj)
        {
            Contract.Requires((int)phase == 1 + (int)Phase);
            switch (phase)
            {
                case PhaseKind.TypesDefined:
                    Contract.Assert(compilerObj is SymbolTable);
                    SymbolTable = (SymbolTable)compilerObj;
                    break;
                case PhaseKind.Compiled:
                    FinalOutput = compilerObj;
                    break;
                default:
                    throw new NotImplementedException();
            }

            Phase = phase;
        }
    }
}
