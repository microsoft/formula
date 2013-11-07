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

    internal class UnnSortSymb : UnnSymb 
    {
        public override string PrintableName
        {
            get 
            {
                return Sort.PrintableName;
            }
        }

        internal BaseSortSymb Sort
        {
            get;
            private set;
        }

        internal UnnSortSymb(Namespace space, Span span, BaseSortSymb sort)
            : base(space, MkDecl(sort, span), true)
        {
            Sort = sort;
        }

        internal override bool IsCompatibleDefinition(UserSymbol s)
        {
            var uss = s as UnnSortSymb;
            if (uss == null)
            {
                return false;
            }

            return uss.Sort == Sort;
        }

        internal override void MergeSymbolDefinition(UserSymbol s)
        {
        }

        internal override bool ResolveTypes(SymbolTable table, List<Flag> flags, CancellationToken cancel)
        {
            SetCanonicalForm(new AppFreeCanUnn[] { new AppFreeCanUnn(table, Sort) });
            return true;
        }

        internal override bool Canonize(List<Flag> flags, CancellationToken cancel)
        {
            return true;
        }

        private static AST<UnnDecl> MkDecl(BaseSortSymb sort, Span span)
        {
            string name;
            var result = API.ASTQueries.ASTSchema.Instance.TryGetSortName(sort.SortKind, out name);
            Contract.Assert(result);
            return Factory.Instance.MkUnnDecl(name, Factory.Instance.MkId(name, span), span);
        }
    }
}
