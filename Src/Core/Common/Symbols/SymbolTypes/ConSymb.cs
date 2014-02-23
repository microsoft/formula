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
    using Common.Extras;

    public sealed class ConSymb : UserSymbol
    {
        private int arity;
        private Tuple<bool, string>[] fldAttrs;
        private Map<string, int> labelMap = new Map<string, int>(string.CompareOrdinal);
        private List<AST<ConDecl>> definitions = new List<AST<ConDecl>>();

        public override int Arity
        {
            get { return arity; }
        }

        public override SymbolKind Kind
        {
            get { return SymbolKind.ConSymb; }
        }

        public override IEnumerable<AST<Node>> Definitions
        {
            get { return definitions; }
        }

        public bool IsNew
        {
            get { return ((ConDecl)Definitions.First<AST<Node>>().Node).IsNew; }
        }

        public bool IsSub
        {
            get { return ((ConDecl)Definitions.First<AST<Node>>().Node).IsSub; }
        }

        public UserSortSymb SortSymbol
        {
            get;
            internal set;
        }

        /// <summary>
        /// True if this is a sub constructor, and a corresponding sub rule should be generated.
        /// Can be false if the sub rule is optimized away.
        /// </summary>
        internal bool IsSubRuleGenerated
        {
            get;
            private set;
        }

        internal ConSymb(Namespace space, AST<ConDecl> def, bool isAutogen)
            : base(space, def.Node.Name, isAutogen)
        {
            definitions.Add(def);
            arity = def.Node.Fields.Count;
            fldAttrs = new Tuple<bool, string>[arity];
            IsSubRuleGenerated = def.Node.IsSub;
        }

        /// <summary>
        /// Creates an internal con symbol. Should not be added to the symbol table
        /// and only limited operations can be called on it.
        /// </summary>
        /// <param name="space"></param>
        /// <param name="name"></param>
        /// <param name="isAutogen"></param>
        internal ConSymb(Namespace space, string name, int arity)
            : base(space, name, true)
        {
            Contract.Requires(arity > 0);
            this.arity = arity;
        }

        /// <summary>
        /// If this data constructor has an argument with label,
        /// then returns true and provides the index of the label.
        /// Otherwise returns false.
        /// </summary>
        public bool GetLabelIndex(string label, out int index)
        {
            return labelMap.TryFindValue(label, out index);
        }

        /// <summary>
        /// Only call on a new-kind constructor.
        /// Returns true if the ith arg of this constructor is an any-kind arg.
        /// </summary>
        public bool IsAnyArg(int index)
        {
            Contract.Requires(IsNew);
            Contract.Requires(index >= 0 && index < Arity);
            return fldAttrs[index].Item1;
        }
                
        internal override bool IsCompatibleDefinition(UserSymbol s)
        {
            AST<ConDecl> sDecl = s.Definitions.First<AST<Node>>() as AST<ConDecl>;
            if (s.Kind != Kind || 
                s.IsAutoGen != IsAutoGen || 
                sDecl == null || 
                sDecl.Node.IsNew != IsNew ||
                sDecl.Node.IsSub != IsSub ||
                sDecl.Node.Fields.Count != arity)
            {
                return false;
            }

            bool isEmpty1, isEmpty2;
            using (var it1 = definitions.First<AST<ConDecl>>().Node.Fields.GetEnumerator())
            {
                using (var it2 = sDecl.Node.Fields.GetEnumerator())
                {
                    while (it1.MoveNext() & it2.MoveNext())
                    {
                        if (it1.Current.IsAny != it2.Current.IsAny)
                        {
                            return false;
                        }

                        isEmpty1 = string.IsNullOrEmpty(it1.Current.Name);
                        isEmpty2 = string.IsNullOrEmpty(it2.Current.Name);
                        if (isEmpty1 != isEmpty2 || (!isEmpty1 && string.CompareOrdinal(it1.Current.Name, it2.Current.Name) != 0))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        internal override void MergeSymbolDefinition(UserSymbol s)
        {
            foreach (var def in s.Definitions)
            {
                definitions.Add((AST<ConDecl>)def);
            }
        }

        /// <summary>
        /// Indicates that this sub constructor should not have a sub rule generated.
        /// Used to optimize away from rules introduced by relational constraints.
        /// </summary>
        internal void DoNotGenSubRule()
        {
            Contract.Requires(IsSub);
            IsSubRuleGenerated = false;
        }

        internal override bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            int i;
            var result = BuildFldAttrs(flags);
            AppFreeCanUnn[] cdefs;
            foreach (var def in definitions)
            {
                i = 0;
                cdefs = new AppFreeCanUnn[Arity];
                foreach (var fld in def.Node.Fields)
                {
                    cdefs[i] = new AppFreeCanUnn(table, Factory.Instance.ToAST(fld.Type));
                    result = cdefs[i].ResolveTypes(flags, cancel) & result;
                    ++i;
                }

                def.Node.CompilerData = cdefs;
            }

            return result;
        }

        internal override bool Canonize(List<Flag> flags, CancellationToken cancel)
        {
            AppFreeCanUnn[] cdefs = null, cdefsp = null;
            var result = true;
            foreach (var def in definitions)
            {
                cdefs = (AppFreeCanUnn[])def.Node.CompilerData;
                if (cdefsp == null)
                {
                    cdefsp = cdefs;
                }

                for (int i = 0; i < Arity; ++i)
                {
                    result = cdefs[i].Canonize(FullName, flags, cancel) & result;
                    if (!cdefsp[i].IsEquivalent(cdefs[i]))
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            cdefs[i].TypeExpr.Node,
                            Constants.DuplicateDefs.ToString(
                                string.Format("type {0}", FullName),
                                cdefs[i].TypeExpr.GetCodeLocationString(Namespace.SymbolTable.Env.Parameters),
                                cdefsp[i].TypeExpr.GetCodeLocationString(Namespace.SymbolTable.Env.Parameters)),
                            Constants.DuplicateDefs.Code);
                        flags.Add(flag);
                        result = false;
                    }
                }
            }

            if (result)
            {
                Contract.Assert(cdefs != null);
                SetCanonicalForm(cdefs);
            }

            return result;
        }

        internal override AST<Node> CopyCanonicalForm(Span span, string renaming)
        {
            AST<ConDecl> conDecl;
            if (IsSub)
            {
                conDecl = Factory.Instance.MkSubDecl(Name, span);
            }
            else
            {
                conDecl = Factory.Instance.MkConDecl(Name, IsNew, span);
            }

            for (int i = 0; i < arity; ++i)
            {
                var fld = Factory.Instance.MkField(
                    fldAttrs[i].Item2,
                    CanonicalForm[i].MkTypeTerm(span, renaming),
                    fldAttrs[i].Item1,
                    span);
                conDecl = Factory.Instance.AddField(conDecl, fld);
            }

            return conDecl;
        }

        internal override UserSymbol CloneSymbol(Namespace space, Span span, string renaming)
        {
            var clone = new ConSymb(space, (AST<ConDecl>)CopyCanonicalForm(span, renaming), IsAutoGen);
            clone.IsSubRuleGenerated = IsSubRuleGenerated;
            return clone;
        }

        private bool BuildFldAttrs(List<Flag> flags)
        {
            var decl = definitions.First<AST<ConDecl>>();
            int i = 0, j;
            var result = true;
            bool derived = !IsNew;
            foreach (var fld in decl.Node.Fields)
            {
                if (derived && fld.IsAny)
                {
                    flags.Add(new Flag(
                        SeverityKind.Error,
                        fld,
                        Constants.BadSyntax.ToString("The 'any' modifier is only allowed for new-kind constructors and maps"),
                        Constants.BadSyntax.Code));
                    result = false;
                }

                if (string.IsNullOrEmpty(fld.Name))
                {
                    fldAttrs[i] = new Tuple<bool, string>(fld.IsAny, string.Empty);
                }
                else if (API.ASTQueries.ASTSchema.Instance.IsId(fld.Name, false, false, false, false))
                {
                    fldAttrs[i] = new Tuple<bool, string>(fld.IsAny, fld.Name);
                    if (labelMap.TryFindValue(fld.Name, out j))
                    {
                        var flag = new Flag(
                                    SeverityKind.Error,
                                    fld,
                                    Constants.DuplicateDefs.ToString(
                                            string.Format("label {0}", fld.Name),
                                            string.Format("index {0}", i),
                                            string.Format("index {0}", j)),
                                    Constants.DuplicateDefs.Code);
                        flags.Add(flag);
                        result = false;
                    }
                    else
                    {
                        labelMap.Add(fld.Name, i);
                        Namespace.SymbolTable.RegisterLabel(fld.Name, SortSymbol);
                    }
                }
                else
                {
                    var flag = new Flag(
                                SeverityKind.Error,
                                fld,
                                Constants.BadId.ToString(fld.Name, "label"),
                                Constants.BadId.Code);
                    flags.Add(flag);
                    result = false;
                }

                ++i;
            }

            if (arity == 0)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    decl.Node,
                    Constants.BadTypeDecl.ToString(FullName, "it has no fields"),
                    Constants.BadTypeDecl.Code);
                flags.Add(flag);
                result = false;
            }

            return result;
        }
    }
}
