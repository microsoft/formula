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

    public sealed class MapSymb : UserSymbol
    {
        private int arity;
        private Tuple<bool, string>[] domAttrs;
        private Tuple<bool, string>[] codAttrs;
        private Map<string, int> labelMap = new Map<string, int>(string.CompareOrdinal);
        private List<AST<MapDecl>> definitions = new List<AST<MapDecl>>();

        public override int Arity
        {
            get { return arity; }
        }

        public int DomArity
        {
            get { return domAttrs.Length; }
        }

        public int CodArity
        {
            get { return codAttrs.Length; }
        }

        public override SymbolKind Kind
        {
            get { return SymbolKind.MapSymb; }
        }

        public MapKind MapKind
        {
            get { return definitions.First<AST<MapDecl>>().Node.MapKind; }
        }

        public bool IsPartial
        {
            get { return definitions.First<AST<MapDecl>>().Node.IsPartial; }
        }

        public override IEnumerable<AST<Node>> Definitions
        {
            get { return definitions; }
        }

        public UserSortSymb SortSymbol
        {
            get;
            internal set;
        }

        internal MapSymb(Namespace space, AST<MapDecl> def, bool isAutogen)
            : base(space, def.Node.Name, isAutogen)
        {
            definitions.Add(def);
            arity = def.Node.Dom.Count + def.Node.Cod.Count;
            domAttrs = new Tuple<bool, string>[def.Node.Dom.Count];
            codAttrs = new Tuple<bool, string>[def.Node.Cod.Count];
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
        /// Returns true if the ith arg of this map is an any-kind arg.
        /// </summary>
        public bool IsAnyArg(int index)
        {
            Contract.Requires(index >= 0 && index < Arity);
            if (index < domAttrs.Length)
            {
                return domAttrs[index].Item1;
            }
            else
            {
                return codAttrs[index - domAttrs.Length].Item1;
            }
        }

        internal override bool IsCompatibleDefinition(UserSymbol s)
        {
            var myDecl = definitions.First<AST<MapDecl>>().Node;
            AST<MapDecl> sDecl = s.Definitions.First<AST<Node>>() as AST<MapDecl>;
            if (s.Kind != Kind ||
                s.IsAutoGen != IsAutoGen ||
                sDecl == null ||
                sDecl.Node.Dom.Count != myDecl.Dom.Count ||
                sDecl.Node.Cod.Count != myDecl.Cod.Count ||
                sDecl.Node.MapKind != myDecl.MapKind ||
                sDecl.Node.IsPartial != myDecl.IsPartial)
            {
                return false;
            }

            bool isEmpty1, isEmpty2;
            using (var it1 = myDecl.Dom.GetEnumerator())
            {
                using (var it2 = sDecl.Node.Dom.GetEnumerator())
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

            using (var it1 = myDecl.Cod.GetEnumerator())
            {
                using (var it2 = sDecl.Node.Cod.GetEnumerator())
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
                definitions.Add((AST<MapDecl>)def);
            }
        }

        internal override bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            var first = definitions.First<AST<MapDecl>>();
            var result = BuildFldAttrs(flags, first.Node.Dom, true) &
                         BuildFldAttrs(flags, first.Node.Cod, false);

            int i;
            AppFreeCanUnn[] cdefs;
            foreach (var def in definitions)
            {
                i = 0;
                cdefs = new AppFreeCanUnn[Arity];
                foreach (var fld in def.Node.Dom)
                {
                    cdefs[i] = new AppFreeCanUnn(table, Factory.Instance.ToAST(fld.Type));
                    result = cdefs[i].ResolveTypes(flags, cancel) & result;
                    ++i;
                }

                foreach (var fld in def.Node.Cod)
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
            var mapDecl = Factory.Instance.MkMapDecl(Name, MapKind, IsPartial, span);
            for (int i = 0; i < domAttrs.Length; ++i)
            {
                var fld = Factory.Instance.MkField(
                    domAttrs[i].Item2,
                    CanonicalForm[i].MkTypeTerm(span, renaming),
                    domAttrs[i].Item1,
                    span);
                mapDecl = Factory.Instance.AddMapDom(mapDecl, fld);
            }

            for (int i = 0; i < codAttrs.Length; ++i)
            {
                var fld = Factory.Instance.MkField(
                    codAttrs[i].Item2,
                    CanonicalForm[i + domAttrs.Length].MkTypeTerm(span, renaming),
                    codAttrs[i].Item1,
                    span);
                mapDecl = Factory.Instance.AddMapCod(mapDecl, fld);
            }

            return mapDecl;
        }

        internal override UserSymbol CloneSymbol(Namespace space, Span span, string renaming)
        {
            return new MapSymb(space, (AST<MapDecl>)CopyCanonicalForm(span, renaming), IsAutoGen);
        }

        private bool BuildFldAttrs(List<Flag> flags, ImmutableCollection<Field> flds, bool isDom)
        {
            int i = 0, j;
            var result = true;
            var attrArr = isDom ? domAttrs : codAttrs;
            var shift = isDom ? 0 : domAttrs.Length;
            foreach (var fld in flds)
            {
                if (string.IsNullOrEmpty(fld.Name))
                {
                    attrArr[i] = new Tuple<bool, string>(fld.IsAny, string.Empty);
                }
                else if (API.ASTQueries.ASTSchema.Instance.IsId(fld.Name, false, false, false, false))
                {
                    attrArr[i] = new Tuple<bool, string>(fld.IsAny, fld.Name);
                    if (labelMap.TryFindValue(fld.Name, out j))
                    {
                        var flag = new Flag(
                                    SeverityKind.Error,
                                    fld,
                                    Constants.DuplicateDefs.ToString(
                                            string.Format("label {0}", fld.Name),
                                            string.Format("index {0}", i + shift),
                                            string.Format("index {0}", j)),
                                    Constants.DuplicateDefs.Code);
                        flags.Add(flag);
                        result = false;
                    }
                    else
                    {
                        labelMap.Add(fld.Name, i + shift);
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

            if (flds.Count == 0)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    definitions.First<AST<MapDecl>>().Node,
                    Constants.BadTypeDecl.ToString(FullName, string.Format("it has no {0}", isDom ? "domain" : "codomain")),
                    Constants.BadTypeDecl.Code);
                flags.Add(flag);
                result = false;
            }

            return result;
        }
    }
}
