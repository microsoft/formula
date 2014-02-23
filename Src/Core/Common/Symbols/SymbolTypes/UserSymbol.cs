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

    public abstract class UserSymbol : Symbol
    {
        /// <summary>
        /// True if the symbol is a mangled symbol, which is not intended to be directly used.
        /// </summary>
        public bool IsMangled
        {
            get
            {
                return Name.StartsWith(SymbolTable.ManglePrefix);
            }
        }

        /// <summary>
        /// True if this symbol was automatically introduced by the compiler.
        /// </summary>
        public bool IsAutoGen
        {
            get;
            private set;
        }

        /// <summary>
        /// The name of this symbol, excluding namespace qualifications
        /// </summary>
        public string Name
        {
            get;
            private set;
        }

        /// <summary>
        /// The name of this symbol, including namespace qualifications
        /// </summary>
        public string FullName
        {
            get;
            private set;
        }

        public override string PrintableName
        {
            get { return FullName; }
        }

        /// <summary>
        /// The namespace where this symbols resides
        /// </summary>
        public Namespace Namespace
        {
            get;
            private set;
        }

        /// <summary>
        /// The definitions of this symbol.
        /// </summary>
        public abstract IEnumerable<AST<Node>> Definitions
        {
            get;
        }

        internal AppFreeCanUnn[] CanonicalForm
        {
            get;
            private set;
        }

        internal UserSymbol(Namespace space, string name, bool isAutogen)
        {
            Namespace = space;
            Name = name;
            IsAutoGen = isAutogen;
            FullName = Namespace.FullName == string.Empty ? Name : Namespace.FullName + "." + Name;
        }

        /// <summary>
        /// Returns true if the declaration of symbols s does not trivially conflict with other definitions.
        /// </summary>
        internal virtual bool IsCompatibleDefinition(UserSymbol s)
        {
            Contract.Requires(CanonicalForm == null);
            Contract.Requires(s != null);
            Contract.Requires(s.Name == Name);
            Contract.Requires(s.Namespace == Namespace);
            throw new NotImplementedException();
        }

        /// <summary>
        /// If there are multiple compatible definitions of the symbol, then this function merges
        /// those definitions into this symbol.
        /// </summary>
        /// <param name="s"></param>
        internal virtual void MergeSymbolDefinition(UserSymbol s)
        {
            Contract.Requires(CanonicalForm == null);
            Contract.Requires(s != null);
            Contract.Requires(s.Name == Name);
            Contract.Requires(s.Namespace == Namespace);
            Contract.Requires(s.Kind == Kind);
            Contract.Requires(s.Arity == Arity);
            Contract.Requires(s.IsAutoGen == IsAutoGen);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Verifies that all symbol names in all definitions can be uniquely resolved.
        /// </summary>
        internal virtual bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            Contract.Requires(CanonicalForm == null);
            Contract.Requires(table != null && flags != null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Produces the canonical form for this symbol.
        /// </summary>
        internal virtual bool Canonize(List<Flag> flags, CancellationToken cancel)
        {
            Contract.Requires(CanonicalForm == null);            
            Contract.Requires(flags != null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Builds an AST representing the canonical form of the symbol. An optional
        /// renaming can be applied to the components of the definition.
        /// </summary>
        /// <param name="renaming"></param>
        /// <returns></returns>
        internal virtual AST<Node> CopyCanonicalForm(Span span, string renaming)
        {
            Contract.Requires(CanonicalForm != null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a clone of this symbol using the canonical form as its definition.
        /// </summary>
        internal virtual UserSymbol CloneSymbol(Namespace space, Span span, string renaming)
        {
            Contract.Requires(CanonicalForm != null);
            throw new NotImplementedException();
        }

        /// <summary>
        /// DO NOT CALL DIRECTLY
        /// </summary>
        internal void SetCanonicalForm(AppFreeCanUnn[] can)
        {
            Contract.Requires(CanonicalForm == null);
            CanonicalForm = can;
        }
    }
}
