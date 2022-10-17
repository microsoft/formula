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
    using Common;
    using Common.Extras;
    using Compiler;

    public sealed class Namespace
    {
        private static readonly string[] EmptySuffix = new string[0];

        private Map<string, UserSymbol> symbols =
            new Map<string, UserSymbol>(string.CompareOrdinal);

        private Map<string, Namespace> children =
            new Map<string, Namespace>(string.CompareOrdinal);

        private int nextAnonSymbolicConstant = 0;

        public SymbolTable SymbolTable
        {
            get;
            private set;
        }
           
        public Namespace Parent
        {
            get;
            private set;
        }

        public IEnumerable<Namespace> Children
        {
            get { return children.Values; }
        }

        public IEnumerable<UserSymbol> Symbols
        {
            get { return symbols.Values; }
        }

        public IEnumerable<UserSymbol> DescendantSymbols
        {
            get
            {
                Namespace n;
                var queue = new Queue<Namespace>();
                queue.Enqueue(this);
                while (queue.Count > 0)
                {
                    n = queue.Dequeue();
                    foreach (var s in n.symbols.Values)
                    {
                        yield return s;
                    }

                    foreach (var m in n.children.Values)
                    {
                        queue.Enqueue(m);
                    }
                }
            }
        }

        public string Name
        {
            get;
            private set;
        }

        public string FullName
        {
            get;
            private set;
        }

        internal int Depth
        {
            get;
            private set;
        }

        internal Namespace(SymbolTable table)
        {
            SymbolTable = table;
            Name = FullName = string.Empty;
            Parent = null;
            Depth = 0;
        }

        private Namespace(string name, Namespace parent, SymbolTable table)
        {
            Contract.Requires(parent != null && table != null && !string.IsNullOrWhiteSpace(name));
            SymbolTable = table;
            Name = name;
            Parent = parent;
            Depth = parent.Depth + 1;
            FullName = string.IsNullOrEmpty(parent.Name) ? Name : parent.FullName + "." + Name;
        }

        public bool TryGetSymbol(string name, out UserSymbol symbol)
        {
            return symbols.TryFindValue(name, out symbol);
        }

        public bool TryGetChild(string name, out Namespace space)
        {
            return children.TryFindValue(name, out space);
        }

        /// <summary>
        /// Returns the deepest common ancestor of the two namespaces, if it exists.
        /// </summary>
        internal bool TryGetPrefix(Namespace n, out Namespace prefix)
        {
            if (n == this)
            {
                prefix = this;
                return true;
            }
            else if (n.Depth < Depth)
            {
                return n.TryGetPrefix(this, out prefix);
            }

            var spaces = new Set<Namespace>(Compare);
            var crnt = this;
            while (crnt != null)
            {
                spaces.Add(crnt);
                crnt = crnt.Parent;
            }

            crnt = n;
            while (crnt != null)
            {
                if (spaces.Contains(crnt))
                {
                    prefix = crnt;
                    return true;
                }

                crnt = crnt.Parent;
            }

            prefix = null;
            return false;
        }

        /// <summary>
        /// Returns true if there is a symbol in this namespace or any of its
        /// children with this name.
        /// </summary>
        internal bool ExistsSymbol(string name)
        {
            if (symbols.ContainsKey(name))
            {
                return true;
            }

            foreach (var ns in children.Values)
            {
                if (ns.ExistsSymbol(name))
                {
                    return true;
                }
            }

            return false;
        }
    
        internal bool TryAddNamespace(string name, Location loc, out Namespace child, List<Flag> flags)
        {
            if (children.TryFindValue(name, out child))
            {
                return true;
            }

            if (!API.ASTQueries.ASTSchema.Instance.IsId(name, false, false, false, false))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    loc.AST.Node,
                    Constants.BadId.ToString(name, "renaming"),
                    Constants.BadId.Code,
                    loc.Program.Name);
                flags.Add(flag);
                return false;                
            }

            child = new Namespace(name, this, SymbolTable);
            children.Add(name, child);
            return true;
        }

        /// <summary>
        /// A short-cut for registering model variables. Is only legal
        /// when is it already known that no symbol with this name exists.
        /// </summary>
        internal void AddModelConstant(Id id, int sid)
        {
            Contract.Requires(id != null && id.Fragments.Length == 1);
            var smbCnstName = "%" + id.Name;
            Contract.Assert(!symbols.ContainsKey(smbCnstName));
            var usrSymb = new UserCnstSymb(this, Factory.Instance.MkId(smbCnstName, id.Span), UserCnstSymbKind.New, true);
            usrSymb.Id = sid;
            symbols.Add(smbCnstName, usrSymb);
        }

        public UserCnstSymb AddFreshSymbolicConstant(string name, out AST<Id> id)
        {
            var symbCnstName = "%" + name;
            Contract.Assert(!symbols.ContainsKey(symbCnstName));
            id = Factory.Instance.MkId(symbCnstName, Span.Unknown);
            var usrSymb = new UserCnstSymb(this, id, UserCnstSymbKind.New, true);
            usrSymb.Id = symbols.Count;
            symbols.Add(symbCnstName, usrSymb);
            return usrSymb;
        }

        /// <summary>
        /// Creates a new anononymous model constant for _ appearing in a partial model.
        /// </summary>
        internal void AddAnonModelConstant(Id id, int sid)
        {
            Contract.Requires(id != null && id.Name == API.ASTQueries.ASTSchema.Instance.DontCareName);
            var smbCnstName = string.Format("%~sym{0}", nextAnonSymbolicConstant++);
            Contract.Assert(!symbols.ContainsKey(smbCnstName));
            var usrSymb = new UserCnstSymb(this, Factory.Instance.MkId(smbCnstName, id.Span), UserCnstSymbKind.New, true);
            usrSymb.Id = sid;            
            symbols.Add(smbCnstName, usrSymb);
            Contract.Assert(id.CompilerData == null);
            id.CompilerData = usrSymb;
        }

        internal bool TryAddSymbol(
            UserSymbol symbol, 
            Func<int> idGetter, 
            List<Flag> flags,
            SizeExpr sizeExpr = null)
        {
            Contract.Requires(symbol != null && symbol.Namespace == this && idGetter != null);
            UserSymbol existingSym;
            if (!symbols.TryFindValue(symbol.Name, out existingSym))
            {
                if (!symbol.IsAutoGen && !API.ASTQueries.ASTSchema.Instance.IsId(symbol.Name, false, false, false, false))
                {
                    var ast = symbol.Definitions.First<AST<Node>>();
                    var flag = new Flag(
                        SeverityKind.Error,
                        ast.Node,
                        Constants.BadId.ToString(symbol.Name, "symbol"),
                        Constants.BadId.Code,
                        ast.Root.NodeKind == NodeKind.Program ? ((Program)ast.Root).Name : null);
                    flags.Add(flag);
                    return false;
                }

                symbol.Id = idGetter();
                symbols.Add(symbol.Name, symbol);
                if (symbol.Kind == SymbolKind.ConSymb)
                {
                    var conSymb = ((ConSymb)symbol);
                    var usrSort = new UserSortSymb(symbol);
                    usrSort.Id = idGetter();
                    conSymb.SortSymbol = usrSort;
                    if (sizeExpr != null)
                    {
                        usrSort.Size = sizeExpr;
                    }
                }
                else if (symbol.Kind == SymbolKind.MapSymb)
                {
                    var mapSymb = ((MapSymb)symbol);
                    var usrSort = new UserSortSymb(symbol);
                    usrSort.Id = idGetter();
                    mapSymb.SortSymbol = usrSort;
                    if (sizeExpr != null)
                    {
                        usrSort.Size = sizeExpr;
                    }
                }

                return true;
            }

            if (!existingSym.IsCompatibleDefinition(symbol))
            {
                var ast1 = symbol.Definitions.First<AST<Node>>();
                var ast2 = existingSym.Definitions.First<AST<Node>>();

                var flag = new Flag(
                    SeverityKind.Error,
                    ast1.Node,
                    MessageHelpers.MkDupErrMsg(string.Format("symbol {0}", symbol.Name), ast1, ast2, SymbolTable.Env.Parameters),
                    Constants.DuplicateDefs.Code,
                    ast1.Root.NodeKind == NodeKind.Program ? ((Program)ast1.Root).Name : null);
                flags.Add(flag);
                return false;
            }

            existingSym.MergeSymbolDefinition(symbol);
            return true;
        }

        /// <summary>
        /// If this is a namespace of the form:
        /// prefix.suffix
        /// then returns prefix. 
        /// 
        /// If suffix = {} then returns n.
        /// If suffix = n then returns null.
        /// If not a suffix then returns false.
        /// </summary>
        internal bool Split(string[] suffix, out Namespace prefix)
        {
            Contract.Requires(suffix != null);
            var crnt = this;
            for (int i = suffix.Length - 1; i >= 0; --i)
            {
                if (crnt.Name != suffix[i])
                {
                    prefix = null;
                    return false;
                }

                crnt = crnt.Parent;
            }

            prefix = crnt;
            return true;
        }

        /// <summary>
        /// If this is a namespace of the form:
        /// prefix.lbl1. ... . lbln, then returns the array of strings:
        /// { lbl1, ..., lbln }.
        /// 
        /// If n = prefix, then returns an empty array of strings
        /// 
        /// If prefix is not part of this namespace, then returns null
        /// 
        /// If prefix is null, then returns the entire namespace
        /// </summary>
        internal string[] Split(Namespace prefix)
        {
            if (prefix == this)
            {
                return EmptySuffix;
            }
            else if (prefix == null)
            {
                var suffix = new string[Depth + 1];
                var crnt = this;
                var i = Depth;
                while (crnt != null)
                {
                    suffix[i--] = crnt.Name;
                    crnt = crnt.Parent;
                }

                return suffix;
            }
            else if (prefix.Depth >= Depth)
            {
                return null;
            }
            else
            {
                var crnt = this;
                var suffix = new string[Depth - prefix.Depth];
                for (int i = suffix.Length - 1; i >= 0; --i)
                {
                    suffix[i] = crnt.Name;
                    crnt = crnt.Parent;
                }

                return crnt == prefix ? suffix : null;
            }
        }

        internal static int Compare(Namespace n1, Namespace n2)
        {
            return string.CompareOrdinal(n1.FullName, n2.FullName);
        }
    }
}
