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

    public abstract class Symbol
    {
        private int id = -1;

        public virtual bool IsVariable
        {
            get { return false; }
        }

        public virtual bool IsReservedOperation
        {
            get { return false; }
        }

        public virtual bool IsDerivedConstant
        {
            get { return false; }
        }

        public virtual bool IsNewConstant
        {
            get { return false; }
        }

        public virtual bool IsNonVarConstant
        {
            get { return false; }
        }

        public virtual bool IsTypeUnn
        {
            get { return false; }
        }

        public virtual bool IsRange
        {
            get { return false; }
        }

        public virtual bool IsSelect
        {
            get { return false; }
        }

        public virtual bool IsRelabel
        {
            get { return false; }
        }

        public virtual bool IsSymCount
        {
            get { return false; }
        }

        public virtual bool IsSymAnd
        {
            get { return false; }
        }

        public virtual bool IsSymAndAll
        {
            get { return false; }
        }

        public bool IsDataConstructor
        {
            get { return Kind == SymbolKind.ConSymb || Kind == SymbolKind.MapSymb; }
        }

        public abstract string PrintableName
        {
            get;
        }

        public abstract SymbolKind Kind { get; }

        /// <summary>
        /// A unique Id for this symbol
        /// </summary>
        public int Id
        {
            get
            {
                Contract.Assert(id >= 0);
                return id;
            }

            internal set
            {
                Contract.Requires(value >= 0);
                Contract.Assert(id == -1);
                id = value;
            }
        }

        /// <summary>
        /// The number of arguments this symbol takes
        /// </summary>
        public abstract int Arity
        {
            get;
        }

        /// <summary>
        /// True if the symbol has been constructed and assigned an Id.
        /// </summary>
        internal bool IsFullyConstructed
        {
            get { return id >= 0; }
        }

        internal Symbol()
        {
        }

        public static int Compare(Symbol s1, Symbol s2)
        {      
            return s1.Id - s2.Id;
        }
    }
}
