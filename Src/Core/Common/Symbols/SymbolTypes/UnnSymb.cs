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
    using Common.Extras;
    using Compiler;
    
    public class UnnSymb : UserSymbol
    {
        private List<AST<UnnDecl>> definitions = new List<AST<UnnDecl>>();

        public override int Arity
        {
            get { return 0; }
        }

        public override SymbolKind Kind
        {
            get { return SymbolKind.UnnSymb; }
        }

        public override IEnumerable<AST<Node>> Definitions
        {
            get { return definitions; }
        }

        internal UnnSymb(Namespace space, AST<UnnDecl> def, bool isAutogen)
            : base(space, def.Node.Name, isAutogen)
        {
            definitions.Add(def);
        }

        internal override bool IsCompatibleDefinition(UserSymbol s)
        {
            return s.Kind == Kind && s.IsAutoGen == IsAutoGen;
        }

        internal override void MergeSymbolDefinition(UserSymbol s)
        {
            foreach (var def in s.Definitions)
            {
                definitions.Add((AST<UnnDecl>)def);
            }
        }

        internal override bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            var result = true;
            foreach (var def in definitions)
            {
                var cdef = new AppFreeCanUnn(table, Factory.Instance.ToAST(def.Node.Body));
                result = cdef.ResolveTypes(flags, cancel) & result;
                def.Node.CompilerData = cdef;
            }

            return result;
        }

        internal override bool Canonize(List<Flag> flags, CancellationToken cancel)
        {
            AppFreeCanUnn cdef = null, cdefp = null;
            var result = true;
            foreach (var def in definitions)
            {
                cdef = (AppFreeCanUnn)def.Node.CompilerData;
                if (cdefp == null)
                {
                    cdefp = cdef;
                }

                result = cdef.Canonize(FullName, flags, cancel, this) & result;
                if (!cdefp.IsEquivalent(cdef))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        cdef.TypeExpr.Node,
                        Constants.DuplicateDefs.ToString(
                            string.Format("type {0}", FullName),
                            cdef.TypeExpr.GetCodeLocationString(Namespace.SymbolTable.Env.Parameters),
                            cdefp.TypeExpr.GetCodeLocationString(Namespace.SymbolTable.Env.Parameters)),
                        Constants.DuplicateDefs.Code);
                    flags.Add(flag);
                    result = false;
                }
            }

            if (result)
            {
                Contract.Assert(cdef != null);
                SetCanonicalForm(new AppFreeCanUnn[]{ cdef });
            }

            return result;
        }

        internal override AST<Node> CopyCanonicalForm(Span span, string renaming)
        {
            return Factory.Instance.MkUnnDecl(Name, CanonicalForm[0].MkTypeTerm(span, renaming), span);
        }

        internal override UserSymbol CloneSymbol(Namespace space, Span span, string renaming)
        {
            return new UnnSymb(space, (AST<UnnDecl>)CopyCanonicalForm(span, renaming), IsAutoGen); 
        }
    }
}
