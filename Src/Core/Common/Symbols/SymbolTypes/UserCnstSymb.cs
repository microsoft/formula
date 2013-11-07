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

    public sealed class UserCnstSymb : UserSymbol
    {
        private List<AST<Id>> definitions = new List<AST<Id>>();

        public override int Arity
        {
            get { return 0; }
        }

        public override SymbolKind Kind
        {
            get { return SymbolKind.UserCnstSymb; }
        }

        public override bool IsNewConstant
        {
            get { return UserCnstKind == UserCnstSymbKind.New; }
        }

        public override bool IsDerivedConstant
        {
            get { return UserCnstKind == UserCnstSymbKind.Derived; }
        }

        public override bool IsNonVarConstant
        {
            get { return UserCnstKind != UserCnstSymbKind.Variable; }
        }

        public override bool IsVariable
        {
            get { return UserCnstKind == UserCnstSymbKind.Variable; }
        }

        public bool IsTypeConstant
        {
            get { return Name[0] == '#'; }
        }

        public bool IsSymbolicConstant
        {
            get { return Name[0] == '%'; }
        }

        public UserCnstSymbKind UserCnstKind
        {
            get;
            private set;
        }

        public override IEnumerable<AST<Node>> Definitions
        {
            get { return definitions; }
        }

        internal override bool IsCompatibleDefinition(UserSymbol s)
        {
            UserCnstSymb us = s as UserCnstSymb;
            return us != null &&
                   us.UserCnstKind == UserCnstKind &&
                   us.IsAutoGen == IsAutoGen;
        }

        internal override void MergeSymbolDefinition(UserSymbol s)
        {
            foreach (var def in s.Definitions)
            {
                definitions.Add((AST<Id>)def);
            }
        }

        internal override bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            SetCanonicalForm(new AppFreeCanUnn[] { new AppFreeCanUnn(table, this) });
            return true;
        }

        internal override bool Canonize(List<Flag> flags, CancellationToken cancel)
        {
            return true;
        }

        internal UserCnstSymb(Namespace space, AST<Id> def, UserCnstSymbKind kind, bool isAutogen)
            : base(space, def.Node.Fragments[0], isAutogen)
        {
            UserCnstKind = kind;
            definitions.Add(def);            
        }

        /// <summary>
        /// Makes a constant for internal use only. Only a limited set of operations should be
        /// performed on this constant.
        /// </summary>
        internal UserCnstSymb(Namespace space, string name, UserCnstSymbKind kind)
            : base(space, name, true)
        {
            UserCnstKind = kind;
        }

        internal override UserSymbol CloneSymbol(Namespace space, Span span, string renaming)
        {
            return new UserCnstSymb(space, Factory.Instance.MkId(Name, span), UserCnstKind, IsAutoGen);
        }
    }
}
