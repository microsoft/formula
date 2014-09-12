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
    using API.ASTQueries;
    using Compiler;
    using Common.Extras;

    public sealed class SymbolTable
    {
        /// <summary>
        /// This prefix will be used by the compiler to create symbol names that do not
        /// conflict with user defined symbols. 
        /// </summary>
        public const string ManglePrefix = "~";
        public const string NotRelCnstrName = "notRelational";
        public const string NotFunCnstrName = "notFunctional";
        public const string NotInjCnstrName = "notInjective";
        public const string NotTotalCnstrName = "notTotal";
        public const string NotInvTotalCnstrName = "notInvTotal";
        public const string ConformsName = "conforms";
        public const string RequiresName = "requires";
        public const string EnsuresName = "ensures";
        public const string SCValueName = ManglePrefix + "scValue";
        private const string SubPrefixName = ManglePrefix + "sub";
        private const string ArgPrefixName = ManglePrefix + "arg";
        private const string RelSubName = ManglePrefix + "rel";

        private static readonly char[] namespaceSep = new char[] { '.' };
        private static readonly NodePred[] QueryModelFactIds =
                    new NodePred[]
                    {
                        NodePredFactory.Instance.Star,
                        NodePredFactory.Instance.MkPredicate(NodeKind.ModelFact),
                        NodePredFactory.Instance.Star,
                        NodePredFactory.Instance.MkPredicate(ChildContextKind.Args) &
                        NodePredFactory.Instance.MkPredicate(NodeKind.Id)
                    };

        private LiftedBool isValid = LiftedBool.Unknown;
        private int nSymbols = 0;        
        
        private Map<BaseSortKind, BaseSortSymb> baseSorts = new Map<BaseSortKind, BaseSortSymb>((x, y) => (int)x - (int)y);
        private Map<OpKind, BaseOpSymb> baseOps = new Map<OpKind, BaseOpSymb>((x, y) => (int)x - (int)y);
        private Map<ReservedOpKind, BaseOpSymb> resBaseOps = new Map<ReservedOpKind, BaseOpSymb>((x, y) => (int)x - (int)y);
        private Map<RelKind, BaseOpSymb> relOps = new Map<RelKind, BaseOpSymb>((x, y) => (int)x - (int)y);
        private Map<string, BaseCnstSymb> stringCnsts = new Map<string, BaseCnstSymb>(string.CompareOrdinal);
        private Map<Rational, BaseCnstSymb> ratCnsts = new Map<Rational, BaseCnstSymb>(Rational.Compare);

        /// <summary>
        /// True if this table is generated to temporarily compose two symbol tables.
        /// </summary>
        private bool isTemporaryTable = false;

        /// <summary>
        /// These are symbols created by the compiler that can be used in the body of a rule, but cannot
        /// appear in the head of a user-defined rule.
        /// </summary>
        private Set<UserSymbol> protectedHeadSymbols = new Set<UserSymbol>(Symbol.Compare);

        /// <summary>
        /// Rules that were introduced by the compiler
        /// </summary>
        private LinkedList<AST<Rule>> introRules = new LinkedList<AST<Rule>>();

        /// <summary>
        /// A cache of the coercibility of data.
        /// </summary>
        private Set<Coercion> coercibility = new Set<Coercion>(Coercion.Compare);
        private Object coerceLock = new Object();

        /// <summary>
        /// Every module whose table was used to construct this table has an entry in this map.
        /// It maps the short name of the module to its table. This table also has an entry in this map.
        /// </summary>
        private Map<string, SymbolTable> dependentTables = new Map<string, SymbolTable>(string.CompareOrdinal);
        private Set<string> allSpaceNames = new Set<string>(string.Compare);

        /// <summary>
        /// Maps a label to the set of data constructors that have an argument with that label.
        /// </summary>
        private Map<string, Set<UserSortSymb>> invLabelMap = new Map<string, Set<UserSortSymb>>(string.CompareOrdinal);

        /// <summary>
        /// A cache of namespace relabelings.
        /// </summary>
        private Map<RelabelData, Namespace> relabelCache = new Map<RelabelData, Namespace>(RelabelData.Compare);

        /// <summary>
        /// This is the root namespace of the symbol table. It contains symbols that do not
        /// require any qualification.
        /// </summary>
        public Namespace Root
        {
            get;
            private set;
        }

        /// <summary>
        /// This is the namespace with the same name as the module. It contains 
        /// derived constants and some special types.
        /// </summary>
        public Namespace ModuleSpace
        {
            get;
            private set;
        }

        public bool IsValid
        {
            get
            {
                return (bool)isValid;
            }
        }

        public IEnumerable<BaseCnstSymb> RationalCnsts
        {
            get { return ratCnsts.Values; }
        }

        public IEnumerable<BaseCnstSymb> StringCnsts
        {
            get { return stringCnsts.Values; }
        }

        public Env Env
        {
            get { return ModuleData.Env; }
        }

        internal int NSymbols
        {
            get { return nSymbols; }
        }

        internal ModuleData ModuleData
        {
            get;
            private set;
        }

        /// <summary>
        /// A set of compiler-introduced rules.
        /// </summary>
        internal IEnumerable<AST<Rule>> IntroducedRules
        {
            get { return introRules; }
        }

        internal SymbolTable(ModuleData modData)
        {
            Contract.Requires(modData != null);           
            Root = new Namespace(this);

            string name;
            Namespace modSpace;
            modData.Source.AST.Node.TryGetStringAttribute(AttributeKind.Name, out name);
            var result = Root.TryAddNamespace(name, modData.Source, out modSpace, new List<Flag>());
            Contract.Assert(result);

            ModuleSpace = modSpace;
            ModuleData = modData;

            MkSorts();
            MkBaseOps();

            dependentTables.Add(name, this);
        }

        /// <summary>
        /// Used to build a temporary symbol table to test if values can be coerced.
        /// </summary>
        internal SymbolTable(Env env, Location[] imports, out string[] importNamespaces)
        {
            Contract.Requires(env != null && imports != null);
            isTemporaryTable = true;

            string name;
            AST<ModRef> mr;
            Root = new Namespace(this);
            var tempDom = "temp" + env.GetGuid().ToString("D16").Replace('-', '_');            
            var progName = new ProgramName(string.Format("{0}{1}.4ml", ProgramName.EnvironmentScheme, tempDom));
            var dom = Factory.Instance.MkDomain(tempDom, ComposeKind.Includes);
            importNamespaces = new string[imports.Length];
            for (int i = 0; i < imports.Length; ++i)
            {
                if (!imports[i].AST.Node.TryGetStringAttribute(AttributeKind.Name, out name))
                {
                    throw new InvalidOperationException();
                }

                importNamespaces[i] = tempDom + i.ToString();
                mr = Factory.Instance.MkModRef(name, importNamespaces[i], null);
                mr.Node.CompilerData = imports[i];
                dom = Factory.Instance.AddDomainCompose(dom, mr);
            }

            var prog = Factory.Instance.MkProgram(progName);
            prog = Factory.Instance.AddModule(prog, dom);
            var domPath = prog.FindAny(new NodePred[] { NodePredFactory.Instance.Star, NodePredFactory.Instance.MkPredicate(NodeKind.Domain) });
            Contract.Assert(domPath != null);

            Namespace modSpace; 
            ModuleData = new ModuleData(env, new Location(domPath), dom.Node, false);
            var result = Root.TryAddNamespace(tempDom, ModuleData.Source, out modSpace, new List<Flag>());
            Contract.Assert(result);

            ModuleSpace = modSpace;

            MkSorts();
            MkBaseOps();

            dependentTables.Add(tempDom, this);

            result = Compile(new List<Flag>(), default(CancellationToken));
            Contract.Assert(result);
        }

        /// <summary>
        /// Tries to resolve the namespace. If prioritySpace is non-null, then search considers spaces under
        /// priority space first. If no spaces match, then search restarts from the root namespace.
        /// In the instance of an ambiguity, a witness to the ambiguity is provided in otherResolve.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="prioritySpace"></param>
        /// <returns></returns>
        public Namespace Resolve(string name, out Namespace otherResolve, Namespace prioritySpace = null)
        {
            Contract.Requires(prioritySpace == null || prioritySpace.SymbolTable == this);
            if (string.IsNullOrEmpty(name))
            {
                otherResolve = null;
                return prioritySpace == null ? Root : prioritySpace;
            }

            var path = name.Split(namespaceSep);
            var queue = new Queue<Tuple<Namespace, int>>();

            int pathMatch;
            Namespace ns, nschild;
            Namespace match = null;
            queue.Enqueue(prioritySpace == null ? new Tuple<Namespace, int>(Root, 0) : new Tuple<Namespace, int>(prioritySpace, 0));
            while (queue.Count > 0)
            {
                ns = queue.Peek().Item1;
                pathMatch = queue.Dequeue().Item2;

                if (match != null && ns.Depth > match.Depth)
                {
                    break;
                }

                if (pathMatch == path.Length - 1)
                {
                    if (match == null)
                    {
                        ns.TryGetChild(path[pathMatch], out match);
                    }
                    else if (match != null && ns.TryGetChild(path[pathMatch], out otherResolve))
                    {
                        return match;
                    }

                    if (match == null)
                    {
                        foreach (var nsp in ns.Children)
                        {
                            queue.Enqueue(new Tuple<Namespace, int>(nsp, pathMatch));
                        }
                    }
                }
                else
                {
                    if (ns.TryGetChild(path[pathMatch], out nschild))
                    {
                        queue.Enqueue(new Tuple<Namespace, int>(nschild, pathMatch + 1));
                    }

                    foreach (var nsp in ns.Children)
                    {
                        queue.Enqueue(new Tuple<Namespace, int>(nsp, pathMatch));
                    }
                }
            }

            if (match == null && prioritySpace != null && prioritySpace != Root)
            {
                return Resolve(name, out otherResolve);
            }
            else
            {
                otherResolve = null;
                return match;
            }
        }

        /// <summary>
        /// Tries to resolve the symbol. If prioritySpace is non-null, then search considers symbols under
        /// priority space first. If no symbols match, then search restarts from the root namespace.
        /// In the instance of an ambiguity, a witness to the ambiguity is provided in otherResolve.
        /// If pred is not null, then search only considers symbols satisfying pred.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="prioritySpace"></param>
        /// <returns></returns>
        public UserSymbol Resolve(string name, out UserSymbol otherResolve, Namespace prioritySpace = null, Predicate<UserSymbol> pred = null)
        {
            Contract.Requires(name != null);
            Contract.Requires(prioritySpace == null || prioritySpace.SymbolTable == this);

            var path = name.Split(namespaceSep);
            var queue = new Queue<Tuple<Namespace, int>>();

            int pathMatch;
            Namespace ns, nschild;
            UserSymbol match = null;
            queue.Enqueue(prioritySpace == null ? new Tuple<Namespace, int>(Root, 0) : new Tuple<Namespace, int>(prioritySpace, 0));           
            while (queue.Count > 0)
            {
                ns = queue.Peek().Item1;
                pathMatch = queue.Dequeue().Item2;

                if (match != null && ns.Depth > match.Namespace.Depth)
                {
                    break;
                }

                if (pathMatch == path.Length - 1)
                {
                    if (match == null)
                    {
                        if (ns.TryGetSymbol(path[pathMatch], out match) && pred != null && !pred(match))
                        {
                            match = null;
                        }
                    }
                    else if (match != null && ns.TryGetSymbol(path[pathMatch], out otherResolve))
                    {
                        if (pred != null && !pred(otherResolve))
                        {
                            otherResolve = null;
                        }
                        else
                        {
                            return match;
                        }
                    }

                    if (match == null)
                    {
                        foreach (var nsp in ns.Children)
                        {
                            queue.Enqueue(new Tuple<Namespace, int>(nsp, pathMatch));
                        }
                    }
                }
                else
                {
                    if (ns.TryGetChild(path[pathMatch], out nschild))
                    {
                        queue.Enqueue(new Tuple<Namespace, int>(nschild, pathMatch + 1));
                    }

                    foreach (var nsp in ns.Children)
                    {
                        queue.Enqueue(new Tuple<Namespace, int>(nsp, pathMatch));
                    }
                }
            }

            if (match == null && prioritySpace != null && prioritySpace != Root)
            {
                return Resolve(name, out otherResolve, null, pred);
            }
            else
            {
                otherResolve = null;
                return match;
            }
        }

        /// <summary>
        /// Tries to find a symbol in this table located in the same namespace and with same name and arity
        /// as the forgnSymbol. Returns null if no such symbol exists. If a renaming is provided, then the 
        /// foreign symbol is treated as if it were under the renaming.
        /// If dropLastRenaming is true and forgnSymbol is called X.Y. ... .f, then resolves Y.Z. ... .f.
        /// </summary>
        public UserSymbol Resolve(UserSymbol forgnSymbol, string renaming = null, bool dropLastRenaming = false)
        {
            Contract.Requires(forgnSymbol != null);
            var forgnDepth = forgnSymbol.Namespace.Depth > 0 && dropLastRenaming ? forgnSymbol.Namespace.Depth - 1 : forgnSymbol.Namespace.Depth; 
            var path = new string[forgnDepth + (string.IsNullOrEmpty(renaming) ? 0 : 1)];
            Namespace ns = forgnSymbol.Namespace;
            int i = path.Length - 1;
            if (!dropLastRenaming)
            {
                while (ns.Parent != null)
                {
                    path[i--] = ns.Name;
                    ns = ns.Parent;
                }
            }
            else if (ns.Parent != null)
            {
                while (ns.Parent.Parent != null)
                {
                    path[i--] = ns.Name;
                    ns = ns.Parent;
                }
            }

            if (!string.IsNullOrEmpty(renaming))
            {
                Contract.Assert(i == 0);
                path[0] = renaming;
            }

            ns = Root;
            foreach (var n in path)
            {
                if (!ns.TryGetChild(n, out ns))
                {
                    return null;
                }
            }

            UserSymbol resolved;
            if (!ns.TryGetSymbol(forgnSymbol.Name, out resolved) || 
                resolved.Arity != forgnSymbol.Arity)
            {
                return null;
            }

            return resolved;
        }

        internal bool IsProtectedHeadSymbol(Symbol symbol)
        {
            if (symbol.Kind == SymbolKind.UserSortSymb)
            {
                return protectedHeadSymbols.Contains(((UserSortSymb)symbol).DataSymbol);
            }

            var us = symbol as UserSymbol;
            return us != null && protectedHeadSymbols.Contains(us);
        }

        internal bool IsCoercible(Symbol symbol, Namespace from, Namespace to, out Symbol coerced)
        {
            Contract.Requires(symbol != null && from != null && to != null);
            Contract.Requires(symbol.Kind == SymbolKind.UserSortSymb || symbol.Kind == SymbolKind.UserCnstSymb);
            Contract.Requires(symbol.Kind != SymbolKind.UserCnstSymb || symbol.IsDerivedConstant || ((UserCnstSymb)symbol).IsTypeConstant);
     
            lock (coerceLock)
            {
                var c = new Coercion(symbol, from, to);
                if (IsCoercibleLocked(c))
                {
                    c.TryGetToSymbol(out coerced);
                    return true;
                }
                else
                {
                    coerced = null;
                    return false;
                }
            }
        }

        internal bool Compile(List<Flag> flags, CancellationToken cancel)
        {
            Contract.Assert(isValid == LiftedBool.Unknown);
            isValid = LiftedBool.True;
            //// Step 1. Try to import/collect all the symbols in the source file.
            isValid = ImportSymbols(flags, cancel);
            if (!IsValid)
            {
                return false;
            }

            isValid = CollectSourceSymbols(flags, cancel);
            if (!IsValid)
            {
                return false;
            }
                        
            //// Step 2. Try to resolve the type names appearing in all type definitions.
            foreach (var s in Root.DescendantSymbols)
            {
                if (s.CanonicalForm != null)
                {
                    continue;
                }

                isValid = s.ResolveTypes(this, flags, cancel) & isValid;
            }

            foreach (var kv in invLabelMap)
            {
                if (allSpaceNames.Contains(kv.Key))
                {
                    flags.Add(new Flag(
                                SeverityKind.Error,
                                kv.Value.GetSomeElement().DataSymbol.Definitions.First<AST<Node>>().Node,
                                Constants.LabelClashError.ToString(kv.Key),
                                Constants.LabelClashError.Code));
                    isValid = false;
                }
            }

            if (!IsValid)
            {
                return false;
            }

            //// Step 3. Try to create canonical forms for all the user symbols.
            foreach (var s in Root.DescendantSymbols)
            {
                if (s.CanonicalForm != null)
                {
                    continue;
                }

                if (!s.Canonize(flags, cancel))
                {
                    isValid = false;
                }
                else if (s.Kind == SymbolKind.ConSymb && ((ConSymb)s).IsNew || s.Kind == SymbolKind.MapSymb)
                {
                    isValid = CheckNewness((UserSymbol)s, flags) & isValid;
                }
                else if (s.Kind == SymbolKind.ConSymb && ((ConSymb)s).IsSub)
                {
                    isValid = CheckNewness((UserSymbol)s, flags) & isValid;
                }
            }

            if (!IsValid)
            {
                return false;
            }

            //// Step 4. Verify that all type definitions are productive.
            UserSymbol other;
            var trueCnst = (UserCnstSymb)Resolve(ASTSchema.Instance.ConstNameTrue, out other);
            var prodGraph = new ProductivityGraph(this, trueCnst);

            if (!prodGraph.CheckProductivity(flags, cancel))
            {
                isValid = false;
            }
            else if (!MkUserSortSizes(flags) || !MkCardContracts(flags))
            {
                isValid = false;
            }

            return IsValid && !cancel.IsCancellationRequested;
        }

        /// <summary>
        /// Returns true if any prefix fragment of Id coincides with a legal namespace.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        internal bool HasRenamingPrefix(Id id)
        {
            Contract.Requires(id != null);
            for (int i = 0; i < id.Fragments.Length - 1; ++i)
            {
                if (allSpaceNames.Contains(id.Fragments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        internal BaseOpSymb GetOpSymbol(OpKind op)
        {
            return baseOps[op];
        }

        internal BaseOpSymb GetOpSymbol(RelKind op)
        {
            return relOps[op];
        }

        internal BaseOpSymb GetOpSymbol(ReservedOpKind op)
        {
            return resBaseOps[op];
        }

        internal BaseSortSymb GetSortSymbol(BaseSortKind kind)
        {
            return baseSorts[kind];
        }

        internal BaseCnstSymb GetCnstSymbol(Rational r)
        {
            BaseCnstSymb symb;
            if (ratCnsts.TryFindValue(r, out symb))
            {
                return symb;
            }

            symb = new BaseCnstSymb(r);
            symb.Id = IncSymbolCount();
            ratCnsts.Add(r, symb);
            return symb;
        }

        internal BaseCnstSymb GetCnstSymbol(string s)
        {
            BaseCnstSymb symb;
            if (stringCnsts.TryFindValue(s, out symb))
            {
                return symb;
            }

            symb = new BaseCnstSymb(s);
            symb.Id = IncSymbolCount();
            stringCnsts.Add(s, symb);
            return symb;
        }

        /// <summary>
        /// Relabels the target namespace by substituting the from-prefix of 
        /// the target with the to-prefix. Throws an exception if this substitution does
        /// not produce a well-defined namespace. Type checking should have guaranteed that this 
        /// is impossible.
        /// </summary>
        internal Namespace Relabel(string from, string to, Namespace target)
        {
            Contract.Requires(from != null && to != null && target != null);

            Namespace space;
            var rlbl = new RelabelData(from, to, target);
            if (relabelCache.TryFindValue(rlbl, out space))
            {
                return space;
            }

            space = Root;
            var path = from.Split(namespaceSep, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < path.Length; ++i)
            {
                if (!space.TryGetChild(path[i], out space))
                {
                    throw new Impossible();
                }
            }

            var suffix = target.Split(space);
            if (suffix == null)
            {
                throw new Impossible();
            }

            
            space = Root;
            path = to.Split(namespaceSep, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < path.Length; ++i)
            {
                if (!space.TryGetChild(path[i], out space))
                {
                    throw new Impossible();
                }
            }

            for (int i = 0; i < suffix.Length; ++i)
            {
                if (!space.TryGetChild(suffix[i], out space))
                {
                    throw new Impossible();
                }
            }

            relabelCache.Add(rlbl, space);

            return space;
        }

        /// <summary>
        /// Returns all the users sorts that have label defined. If label is not
        /// defined, then returns false.
        /// </summary>
        internal bool InverseLabelLookup(string label, out Set<UserSortSymb> symbols)
        {
            return invLabelMap.TryFindValue(label, out symbols);
        }

        internal void RegisterLabel(string label, UserSortSymb symb)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(label));
            Contract.Requires(symb != null);

            Set<UserSortSymb> symbols;
            if (!invLabelMap.TryFindValue(label, out symbols))
            {
                symbols = new Set<UserSortSymb>(Symbol.Compare);
                invLabelMap.Add(label, symbols);
            }

            symbols.Add(symb);
        }

        private static bool IsAllCapsName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            bool hasLetter = false;
            for (int i = 0; i < name.Length; ++i)
            {
                if (char.IsLetter(name, i))
                {
                    hasLetter = true;
                    if (char.IsLower(name, i))
                    {
                        return false;
                    }
                }
            }

            return hasLetter;
        }

        private bool CheckTotality(UserSortSymb s, SizeExpr expr, int ndex, List<Flag> flags)
        {
            if (s.DataSymbol.Kind != SymbolKind.MapSymb || expr.Kind != SizeExprKind.Infinity)
            {
                return true;
            }
          
            var map = (MapSymb)s.DataSymbol;
            if (map.MapKind == MapKind.Bij || 
                (!map.IsPartial && ndex < map.DomArity) ||
                (map.MapKind == MapKind.Sur && ndex >= map.DomArity))
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    s.DataSymbol.Definitions.First<AST<Node>>().Node,
                    Constants.TotalityError.ToString(s.DataSymbol.FullName, ndex + 1),
                    Constants.TotalityError.Code));
                return false;
            }

            return true;
        }

        private void MkContractRule(int nContracts, bool isRequires)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var body = Factory.Instance.MkBody(span);
            var contractName = isRequires ? RequiresName : EnsuresName;
            if (nContracts == 0)
            {
                body = Factory.Instance.AddConjunct(
                    body,
                    Factory.Instance.MkRelConstr(
                        RelKind.Eq,
                        Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span),
                        Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span),
                        span));
            }

            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Model)
            {
                var model = (Model)ModuleData.Reduced.Node;
                if (isRequires && !ModuleData.IsQueryContainer)
                {
                    body = Factory.Instance.AddConjunct(body, MkDerConjunct(model.Domain.Name + "." + ConformsName, false, span));
                }

                if (model.ComposeKind == ComposeKind.Extends)
                {
                    string space;
                    foreach (var mr in model.Compositions)
                    {
                        space = string.IsNullOrEmpty(mr.Rename) ? mr.Name : mr.Rename + "." + mr.Name;
                        body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + contractName, false, span));
                    }
                }
            }

            for (int i = 0; i < nContracts; ++i)
            {
                body = Factory.Instance.AddConjunct(
                    body,
                    MkDerConjunct(string.Format("{0}.{1}{2}{3}", ModuleSpace.FullName, ManglePrefix, contractName, i), false, span));
            }

            var rule = Factory.Instance.MkRule(span);
            rule = Factory.Instance.AddHead(rule, Factory.Instance.MkId(ModuleSpace.FullName + "." + contractName, span));
            rule = Factory.Instance.AddBody(rule, body);
            introRules.AddLast(rule);

            //// rule.Print(Console.Out);
        }

        private void MkConformsRule(int nConforms)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var body = Factory.Instance.MkBody(span);
            body = Factory.Instance.AddConjunct(body, MkDerConjunct(ModuleSpace.FullName + "." + NotRelCnstrName, true, span));
            body = Factory.Instance.AddConjunct(body, MkDerConjunct(ModuleSpace.FullName + "." + NotTotalCnstrName, true, span));
            body = Factory.Instance.AddConjunct(body, MkDerConjunct(ModuleSpace.FullName + "." + NotInjCnstrName, true, span));
            body = Factory.Instance.AddConjunct(body, MkDerConjunct(ModuleSpace.FullName + "." + NotInvTotalCnstrName, true, span));
            body = Factory.Instance.AddConjunct(body, MkDerConjunct(ModuleSpace.FullName + "." + NotFunCnstrName, true, span));

            for (int i = 0; i < nConforms; ++i)
            {
                body = Factory.Instance.AddConjunct(
                    body, 
                    MkDerConjunct(string.Format("{0}.{1}{2}{3}", ModuleSpace.FullName, ManglePrefix, ConformsName, i), false, span));
            }

            var dom = ((Domain)ModuleData.Source.AST.Node);
            if (dom.ComposeKind == ComposeKind.Extends)
            {
                string space;
                foreach (var mr in dom.Compositions)
                {
                    space = string.IsNullOrEmpty(mr.Rename) ? mr.Name : mr.Rename + "." + mr.Name;
                    body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + ConformsName, false, span));
                }
            }
            else
            {
                foreach (var mr in dom.Compositions)
                {
                    if (!(mr.CompilerData is Location))
                    {
                        continue;
                    }

                    var md = ((Location)mr.CompilerData).AST.Node.CompilerData as ModuleData;
                    if (md == null || md.SymbolTable == null)
                    {
                        continue;
                    }

                    body = AddRelationConstraints(body, md.SymbolTable.Root, mr.Rename, span);
                }
            }

            var rule = Factory.Instance.MkRule(span);
            rule = Factory.Instance.AddHead(rule, Factory.Instance.MkId(ModuleSpace.FullName + "." + ConformsName, span));
            rule = Factory.Instance.AddBody(rule, body);
            introRules.AddLast(rule);

            //// rule.Print(Console.Out);
        }

        private void AddPrHdSymb(UserSymbol s)
        {
            if (!s.IsFullyConstructed)
            {
                return;
            }

            protectedHeadSymbols.Add(s);
        }

        private AST<Body> AddRelationConstraints(AST<Body> body, Namespace ns, string renaming, Span span)
        {
            UserSymbol symbol;
            if (ns.TryGetSymbol(NotRelCnstrName, out symbol))
            {
                var space = string.IsNullOrEmpty(renaming) ? ns.FullName : renaming + "." + ns.FullName;
                body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + NotRelCnstrName, true, span));
                body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + NotTotalCnstrName, true, span));
                body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + NotInjCnstrName, true, span));
                body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + NotInvTotalCnstrName, true, span));
                body = Factory.Instance.AddConjunct(body, MkDerConjunct(space + "." + NotFunCnstrName, true, span));
            }

            foreach (var nsp in ns.Children)
            {
                body = AddRelationConstraints(body, nsp, renaming, span);
            }

            return body;
        }

        private AST<Node> MkDerConjunct(string cnstName, bool isNegated, Span span)
        {
            var find = Factory.Instance.MkFind(null, Factory.Instance.MkId(cnstName, span), span);
            if (!isNegated)
            {
                return find;
            }

            var body = Factory.Instance.MkBody(span);
            body = Factory.Instance.AddConjunct(body, find);

            var compr = Factory.Instance.MkComprehension(span);
            compr = Factory.Instance.AddHead(compr, Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span));
            compr = Factory.Instance.AddBody(compr, body);

            return Factory.Instance.MkNo(compr, span);
        }

        private void MkMapConstraints(MapSymb map, SizeExpr[] argSizes, List<Flag> flags)
        {
            introRules.AddLast(MkFuncConstr(map, flags));

            if (map.MapKind == MapKind.Inj || map.MapKind == MapKind.Bij)
            {
                introRules.AddLast(MkInjConstr(map, flags));
            }

            if (!map.IsPartial || map.MapKind == MapKind.Bij)
            {
                introRules.AddLast(MkTotalityConstr(map, argSizes, flags));
            }

            if (map.MapKind == MapKind.Sur || map.MapKind == MapKind.Bij)
            {
                introRules.AddLast(MkInvTotalityConstr(map, argSizes, flags));
            }
        }

        private AST<Rule> MkInjConstr(MapSymb map, List<Flag> flags)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var rule = Factory.Instance.MkRule(span);
            for (int i = 0; i < map.DomArity; ++i)
            {
                var app1Args = new AST<Node>[map.Arity];
                var app2Args = new AST<Node>[map.Arity];
                for (int j = 0; j < map.DomArity; ++j)
                {
                    if (j == i)
                    {
                        app1Args[j] = MkArgVar(j, false, span, flags);
                        app2Args[j] = MkArgVar(j, true, span, flags);
                    }
                    else
                    {
                        app1Args[j] = Factory.Instance.MkId("_", span);
                        app2Args[j] = Factory.Instance.MkId("_", span);
                    }
                }

                for (int j = 0; j < map.CodArity; ++j)
                {
                    app1Args[j + map.DomArity] = MkArgVar(j + map.DomArity, false, span, flags);
                    app2Args[j + map.DomArity] = MkArgVar(j + map.DomArity, false, span, flags);
                }

                var find1 = Factory.Instance.MkFuncTerm(
                    Factory.Instance.MkId(map.FullName, span),
                    span,
                    app1Args);

                var find2 = Factory.Instance.MkFuncTerm(
                    Factory.Instance.MkId(map.FullName, span),
                    span,
                    app2Args);

                var neq = Factory.Instance.MkRelConstr(
                    RelKind.Neq,
                    MkArgVar(i, false, span, flags),
                    MkArgVar(i, true, span, flags),
                    span);

                var body = Factory.Instance.MkBody(span);
                body = Factory.Instance.AddConjunct(body, Factory.Instance.MkFind(null, find1, span));
                body = Factory.Instance.AddConjunct(body, Factory.Instance.MkFind(null, find2, span));
                body = Factory.Instance.AddConjunct(body, neq);
                rule = Factory.Instance.AddBody(rule, body);
            }

            return Factory.Instance.AddHead(
                rule, 
                Factory.Instance.MkId(ModuleSpace.Name + "." + NotInjCnstrName, span));
        }

        private bool MkContracts(List<Flag> flags, CancellationToken cancel)
        {
            ContractItem cntrItm;
            UserCnstSymb cntrCnst;
            string contractName;
            int contractIdIndex;
            var nextCntrIds = new int[] { 0, 0, 0 };
            AST<Id> contractId;
            bool result = true;
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Module,
                    NodePredFactory.Instance.MkPredicate(NodeKind.ContractItem)
                },
                (path, x) =>
                {
                    cntrItm = (ContractItem)x;
                    switch (cntrItm.ContractKind)
                    {
                        case ContractKind.RequiresAtLeast:
                        case ContractKind.RequiresAtMost:
                        case ContractKind.RequiresSome:
                            if (ModuleData.Reduced.Node.NodeKind != NodeKind.Model)
                            {
                                result = false;
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    x,
                                    Constants.BadSyntax.ToString("Only models can have cardinality requirements"),
                                    Constants.BadSyntax.Code));
                            }
                            else if (cntrItm.ContractKind == ContractKind.RequiresSome && !((Model)ModuleData.Reduced.Node).IsPartial)
                            {
                                result = false;
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    x,
                                    Constants.BadSyntax.ToString("Only partial models can have \"requires some...\""),
                                    Constants.BadSyntax.Code));
                            }

                            contractName = RequiresName;
                            contractIdIndex = 0;
                            break;
                        case ContractKind.RequiresProp:
                            contractName = RequiresName;
                            contractIdIndex = 0;
                            break;
                        case ContractKind.EnsuresProp:
                            contractName = EnsuresName;
                            contractIdIndex = 1;
                            break;
                        case ContractKind.ConformsProp:
                            contractName = ConformsName;
                            contractIdIndex = 2;
                            break;
                        default:
                            throw new NotImplementedException();
                    }

                    if (nextCntrIds[contractIdIndex] == 0)
                    {
                        cntrCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(contractName, x.Span), UserCnstSymbKind.Derived, true);                        
                        result = ModuleSpace.TryAddSymbol(cntrCnst, IncSymbolCount, flags) && result;
                        AddPrHdSymb(cntrCnst);
                    }

                    contractId = Factory.Instance.MkId(string.Format("{0}{1}{2}", ManglePrefix, contractName, nextCntrIds[contractIdIndex]++), x.Span);
                    cntrCnst = new UserCnstSymb(ModuleSpace, contractId, UserCnstSymbKind.Derived, true);
                    result = ModuleSpace.TryAddSymbol(cntrCnst, IncSymbolCount, flags) && result;
                    AddPrHdSymb(cntrCnst);

                    if (cntrItm.ContractKind == ContractKind.RequiresAtLeast ||
                        cntrItm.ContractKind == ContractKind.RequiresAtMost ||
                        cntrItm.ContractKind == ContractKind.RequiresSome)
                    {
                        cntrItm.CompilerData = Factory.Instance.MkId(ModuleSpace.FullName + "." + contractId.Node.Name, x.Span);
                    }
                    else
                    {
                        var rule = Factory.Instance.MkRule(x.Span);
                        rule = Factory.Instance.AddHead(rule, Factory.Instance.MkId(ModuleSpace.FullName + "." + contractId.Node.Name, x.Span));
                        foreach (var b in cntrItm.Bodies)
                        {
                            rule = Factory.Instance.AddBody(rule, (AST<Body>)Factory.Instance.ToAST(b));
                        }

                        introRules.AddLast(rule);
                    }
                },
                cancel);

            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Domain)
            {
                MkConformsRule(nextCntrIds[2]);
            }
            else if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Model)
            {
                MkContractRule(nextCntrIds[0], true);
                MkContractRule(nextCntrIds[1], false);
            }
            else if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform)
            {
                MkContractRule(nextCntrIds[0], true);
                MkContractRule(nextCntrIds[1], false);
            }
            
            return result;
        }

        private bool MkCardContractRule(ContractItem ctrItem, List<Flag> flags)
        {
            var success = new SuccessToken();
            int isAtMost;
            switch (ctrItem.ContractKind)
            {
                case ContractKind.RequiresAtMost:
                    isAtMost = 1;
                    break;
                case ContractKind.RequiresAtLeast:
                    isAtMost = -1;
                    break;
                case ContractKind.RequiresSome:
                    isAtMost = 0;
                    break;
                default:
                    throw new NotImplementedException();
            }

            CardPair cp;
            UserSymbol symb;
            AST<Body> ruleBody = Factory.Instance.MkBody(ctrItem.Span);
            var auxVarName = MkArgVar(0, false, ctrItem.Span, flags).Node.Name;
            foreach (var s in ctrItem.Specification)
            {
                cp = (CardPair)s;
                if (!ResolveTypename(cp.TypeId.Name, cp.TypeId, out symb, flags))
                {
                    success.Failed();
                    continue;
                }

                foreach (var c in EnumerateNewConstructors(cp.TypeId, symb, success, flags))
                {
                    if (isAtMost == 0)
                    {
                        //// The only purpose of this loop is to validate that the requires some is well-formed.
                        continue;
                    }

                    var compr = Factory.Instance.MkComprehension(cp.Span);
                    compr = Factory.Instance.AddHead(compr, Factory.Instance.MkId(auxVarName, cp.Span));

                    var body = Factory.Instance.MkBody(cp.Span);
                    body = Factory.Instance.AddConjunct(
                        body,
                        Factory.Instance.MkFind(
                            Factory.Instance.MkId(auxVarName, cp.Span),
                            Factory.Instance.MkId((string)c.FullName, cp.Span)));

                    var count = Factory.Instance.MkFuncTerm(OpKind.Count, cp.Span);
                    count = Factory.Instance.AddArg(count, Factory.Instance.AddBody(compr, body));

                    var conj = Factory.Instance.MkRelConstr(
                        isAtMost > 0 ? RelKind.Le : RelKind.Ge,
                        count,
                        Factory.Instance.MkCnst(cp.Cardinality, cp.Span),
                        cp.Span);

                    ruleBody = Factory.Instance.AddConjunct(ruleBody, conj);
                }
            }

            var ctrId = (AST<Id>)ctrItem.CompilerData;
            var rule = Factory.Instance.MkRule(ctrItem.Span);
            rule = Factory.Instance.AddHead(rule, Factory.Instance.MkId(ctrId.Node.Name, ctrItem.Span));
            rule = Factory.Instance.AddBody(rule, ruleBody);

            introRules.AddLast(rule);
            return success.Result;
        }

        private IEnumerable<UserSymbol> EnumerateNewConstructors(Node n, UserSymbol symb, SuccessToken success, List<Flag> flags)
        {
            ConSymb conSymb;
            UserSortSymb usrSymb;
            switch (symb.Kind)
            {
                case SymbolKind.MapSymb:
                    yield return symb;
                    break;
                case SymbolKind.ConSymb:
                    conSymb = (ConSymb)symb;
                    if (!conSymb.IsNew)
                    {
                        success.Failed();
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            n,
                            Constants.CardNewnessError.ToString(conSymb.FullName),
                            Constants.CardNewnessError.Code));
                    }
                    else
                    {
                        yield return conSymb;
                    }

                    break;
                case SymbolKind.UnnSymb:
                    foreach (var s in symb.CanonicalForm[0].NonRangeMembers)
                    {
                        switch (s.Kind)
                        {
                            case SymbolKind.UserSortSymb:
                                usrSymb = (UserSortSymb)s;
                                if (usrSymb.DataSymbol.Kind == SymbolKind.ConSymb && !((ConSymb)usrSymb.DataSymbol).IsNew)
                                {
                                    success.Failed();
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        n,
                                        Constants.CardNewnessError.ToString(usrSymb.DataSymbol.FullName),
                                        Constants.CardNewnessError.Code));
                                }
                                else
                                {
                                    yield return usrSymb.DataSymbol;
                                }

                                break;
                            default:
                                if (s.IsDerivedConstant)
                                {
                                    success.Failed();
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        n,
                                        Constants.CardNewnessError.ToString(s.PrintableName),
                                        Constants.CardNewnessError.Code));
                                }
                                else
                                {
                                    flags.Add(new Flag(
                                        SeverityKind.Warning,
                                        n,
                                        Constants.CardContractWarning.ToString(s.PrintableName),
                                        Constants.CardContractWarning.Code));
                                }

                                break;
                        }
                    }

                    if (!symb.CanonicalForm[0].RangeMembers.IsEmpty())
                    {
                        string intName;
                        ASTSchema.Instance.TryGetSortName(BaseSortKind.Integer, out intName);
                        flags.Add(new Flag(
                            SeverityKind.Warning,
                            n,
                            Constants.CardContractWarning.ToString(intName),
                            Constants.CardContractWarning.Code));
                    }

                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private AST<Rule> MkTotalityConstr(MapSymb map, SizeExpr[] argSizes, List<Flag> flags)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var auxVarName = MkArgVar(0, false, span, flags).Node.Name;

            var domProduct = argSizes[0].ToAST(auxVarName, span);
            for (int i = 1; i < map.DomArity; ++i)
            {
                domProduct = Factory.Instance.MkFuncTerm(
                    OpKind.Mul,
                    span,
                    new AST<Node>[] { domProduct, argSizes[i].ToAST(auxVarName, span) });
            }

            var neqConstr = Factory.Instance.MkRelConstr(
                RelKind.Neq,
                new SizeExpr(map.FullName).ToAST(auxVarName, span),
                domProduct);

            var body = Factory.Instance.MkBody(span);
            body = Factory.Instance.AddConjunct(body, neqConstr);

            var rule = Factory.Instance.MkRule(span);
            rule = Factory.Instance.AddBody(rule, body);

            return Factory.Instance.AddHead(
                rule, 
                Factory.Instance.MkId(ModuleSpace.Name + "." + NotTotalCnstrName, span));
        }

        private AST<Rule> MkInvTotalityConstr(MapSymb map, SizeExpr[] argSizes, List<Flag> flags)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var auxVarName = MkArgVar(0, false, span, flags).Node.Name;

            var codProduct = argSizes[map.DomArity].ToAST(auxVarName, span);
            for (int i = 1; i < map.CodArity; ++i)
            {
                codProduct = Factory.Instance.MkFuncTerm(
                    OpKind.Mul,
                    span,
                    new AST<Node>[] { codProduct, argSizes[i + map.DomArity].ToAST(auxVarName, span) });
            }

            var neqConstr = Factory.Instance.MkRelConstr(
                RelKind.Neq,
                new SizeExpr(map.FullName).ToAST(auxVarName, span),
                codProduct);

            var body = Factory.Instance.MkBody(span);
            body = Factory.Instance.AddConjunct(body, neqConstr);

            var rule = Factory.Instance.MkRule(span);
            rule = Factory.Instance.AddBody(rule, body);

            return Factory.Instance.AddHead(
                rule, 
                Factory.Instance.MkId(ModuleSpace.Name + "." + NotInvTotalCnstrName, span));
        }

        /// <summary>
        /// Does nothing if no relational constraints
        /// </summary>
        private void MkRelConstraints(UserSymbol symb, List<Flag> flags)
        {
            var span = ModuleData.Source.AST.Node.Span;
            //// The indices where relational constraints need to be placed.
            Func<int, bool> isAnyFun;
            UserSortSymb sortSymb;
            if (symb.Kind == SymbolKind.ConSymb)
            {
                var con = (ConSymb)symb;
                if (!con.IsNew)
                {
                    return;
                }

                sortSymb = con.SortSymbol;
                isAnyFun = con.IsAnyArg;
            }
            else if (symb.Kind == SymbolKind.MapSymb)
            {
                var map = ((MapSymb)symb);
                sortSymb = map.SortSymbol;
                isAnyFun = map.IsAnyArg;
            }
            else
            {
                return;
            }

            var relIndices = new LinkedList<int>();
            for (int i = 0; i < symb.Arity; ++i)
            {
                if (isAnyFun(i))
                {
                    continue;
                }
                else if (symb.CanonicalForm[i].UserSorts.IsEmpty())
                {
                    continue;
                }
                else
                {
                    relIndices.AddLast(i);
                }
            }

            UserSymbol subSymb;
            var subSymbName = RelSubName + ManglePrefix + symb.FullName;
            var result = symb.Namespace.TryGetSymbol(subSymbName, out subSymb);
            if (relIndices.Count == 0)
            {
                if (subSymb != null)
                {
                    ((ConSymb)subSymb).DoNotGenSubRule();
                }

                return;
            }

            Contract.Assert(result);
            //// If there is already a sub symbol, then it doesn't need to be redefined.

            foreach (var p in relIndices)
            {
                foreach (var s in symb.CanonicalForm[p].UserSorts)
                {
                    var symbApp = Factory.Instance.MkFuncTerm(Factory.Instance.MkId(symb.FullName, span), span);
                    for (int i = 0; i < symb.Arity; ++i)
                    {
                        if (i != p)
                        {
                            symbApp = Factory.Instance.AddArg(symbApp, Factory.Instance.MkId("_", span));
                        }
                        else
                        {
                            symbApp = Factory.Instance.AddArg(symbApp, MkArgVar(p, false, span, flags));
                        }
                    }

                    symbApp = Factory.Instance.AddArg(
                                     Factory.Instance.MkFuncTerm(Factory.Instance.MkId(subSymb.FullName, span), span),
                                     symbApp);

                    var body = Factory.Instance.MkBody(span);
                    body = Factory.Instance.AddConjunct(body, Factory.Instance.MkFind(null, symbApp, span));
                    body = Factory.Instance.AddConjunct(
                        body, 
                        Factory.Instance.MkRelConstr(
                            RelKind.Typ,
                            MkArgVar(p, false, span, flags),
                            Factory.Instance.MkId(s.DataSymbol.FullName, span),
                            span));

                    var negbody = Factory.Instance.MkBody(span);
                    negbody = Factory.Instance.AddConjunct(
                        negbody, 
                        Factory.Instance.MkFind(MkArgVar(p, true, span, flags), Factory.Instance.MkId(s.DataSymbol.FullName, span), span));

                    negbody = Factory.Instance.AddConjunct(
                        negbody,
                        Factory.Instance.MkRelConstr(RelKind.Eq, MkArgVar(p, true, span, flags), MkArgVar(p, false, span, flags), span));

                    var compr = Factory.Instance.MkComprehension(span);
                    compr = Factory.Instance.AddBody(compr, negbody);
                    compr = Factory.Instance.AddHead(compr, MkArgVar(p, true, span, flags));

                    body = Factory.Instance.AddConjunct(body, Factory.Instance.MkNo(compr, span));
                    var rule = Factory.Instance.MkRule(span);
                    rule = Factory.Instance.AddBody(rule, body);
                    rule = Factory.Instance.AddHead(rule, Factory.Instance.MkId(ModuleSpace.FullName + "." + NotRelCnstrName, span));
                    introRules.AddLast(rule);
                }
            }            
        }

        private AST<Rule> MkFuncConstr(MapSymb map, List<Flag> flags)
        {
            var span = ModuleData.Source.AST.Node.Span;
            var rule = Factory.Instance.MkRule(span);
            for (int i = 0; i < map.CodArity; ++i)
            {
                var app1Args = new AST<Node>[map.Arity];
                var app2Args = new AST<Node>[map.Arity];
                for (int j = 0; j < map.DomArity; ++j)
                {
                    app1Args[j] = MkArgVar(j, false, span, flags);
                    app2Args[j] = MkArgVar(j, false, span, flags); 
                }

                for (int j = 0; j < map.CodArity; ++j)
                {
                    if (j == i)
                    {
                        app1Args[j + map.DomArity] = MkArgVar(j + map.DomArity, false, span, flags);
                        app2Args[j + map.DomArity] = MkArgVar(j + map.DomArity, true, span, flags);
                    }
                    else
                    {
                        app1Args[j + map.DomArity] = Factory.Instance.MkId("_", span);
                        app2Args[j + map.DomArity] = Factory.Instance.MkId("_", span);
                    }
                }

                var find1 = Factory.Instance.MkFuncTerm(
                    Factory.Instance.MkId(map.FullName, span),
                    span,
                    app1Args);

                var find2 = Factory.Instance.MkFuncTerm(
                    Factory.Instance.MkId(map.FullName, span),
                    span,
                    app2Args);

                var neq = Factory.Instance.MkRelConstr(
                    RelKind.Neq,
                    MkArgVar(i + map.DomArity, false, span, flags),
                    MkArgVar(i + map.DomArity, true, span, flags),
                    span);

                var body = Factory.Instance.MkBody(span);
                body = Factory.Instance.AddConjunct(body, Factory.Instance.MkFind(null, find1, span));
                body = Factory.Instance.AddConjunct(body, Factory.Instance.MkFind(null, find2, span));
                body = Factory.Instance.AddConjunct(body, neq);
                rule = Factory.Instance.AddBody(rule, body);
            }

            return Factory.Instance.AddHead(
                rule, 
                Factory.Instance.MkId(ModuleSpace.Name + "." + NotFunCnstrName, span));
        }

        private bool ResolveTypename(
                        string id,
                        Node n,
                        out UserSymbol symbol,
                        List<Flag> flags)
        {
            UserSymbol other = null;
            symbol = Resolve(id, out other);
            if (symbol == null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.UndefinedSymbol.ToString("typename", id),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }
            else if (other != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.AmbiguousSymbol.ToString(
                        "identifier",
                        id,
                        string.Format("({0}, {1}): {2}",
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                symbol.FullName),
                        string.Format("({0}, {1}): {2}",
                                other.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                other.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                other.FullName)),
                    Constants.AmbiguousSymbol.Code);
                flags.Add(flag);
                return false;
            }
            else if (symbol.Kind != SymbolKind.ConSymb &&
                     symbol.Kind != SymbolKind.MapSymb &&
                     symbol.Kind != SymbolKind.UnnSymb)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.UndefinedSymbol.ToString("typename", id),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
                return false;
            }

            return true;
        }

        private bool CheckNewness(UserSymbol s, List<Flag> flags)        
        {
            AppFreeCanUnn u;
            UserSymbol dataSymbol;
            var result = true;

            Constants.MessageString errMsg;
            if (s.Kind == SymbolKind.ConSymb && ((ConSymb)s).IsSub)
            {
                errMsg = Constants.SubArgNewnessError;
            }
            else
            {
                errMsg = Constants.ArgNewnessError;
            }

            for (int i = 0; i < s.Arity; ++i)
            {
                u = s.CanonicalForm[i];
                foreach (var e in u.NonRangeMembers)
                {
                    switch (e.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                        case SymbolKind.BaseSortSymb:
                            break;
                        case SymbolKind.UserSortSymb:
                            dataSymbol = ((UserSortSymb)e).DataSymbol;
                            if (dataSymbol.Kind == SymbolKind.ConSymb && !((ConSymb)dataSymbol).IsNew)
                            {
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    s.Definitions.First<AST<Node>>().Node,
                                    errMsg.ToString(s.Name, dataSymbol.Name, i + 1),
                                    errMsg.Code));
                                result = false;
                            }

                            break;
                        case SymbolKind.UserCnstSymb:
                            if (e.IsDerivedConstant)
                            {
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    s.Definitions.First<AST<Node>>().Node,
                                    errMsg.ToString(s.Name, ((UserSymbol)e).Name, i + 1),
                                    errMsg.Code));
                                result = false;
                            }

                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return result;
        }

        private bool MkCardContracts(List<Flag> flags)
        {
            if (ModuleData.Source.AST.Node.NodeKind != NodeKind.Model)
            {
                return true;
            }

            var model = (Model)ModuleData.Reduced.Node;
            bool success = true;
            foreach (var c in model.Contracts)
            {
                if (c.ContractKind != ContractKind.RequiresAtLeast &&
                    c.ContractKind != ContractKind.RequiresAtMost &&
                    c.ContractKind != ContractKind.RequiresSome)
                {
                    continue;
                }

                success = MkCardContractRule(c, flags) && success;
            }

            return success;
        }

        private bool MkUserSortSizes(List<Flag> flags)
        {            
            //// Step 1. Build a dependency graph of types.
            var deps = new DependencyCollection<UserSortDepNode, bool>(
                UserSortDepNode.Compare, 
                (x, y) => (x == y) ? 0 : (x ? 1 : -1));

            bool isAny;
            ConSymb cn;
            MapSymb mp;
            UserSortSymb uss, ussp;
            UserSortDepNode n, m;
            AppFreeCanUnn unn;
            foreach (var s in Root.DescendantSymbols)
            {
                if (s.Kind == SymbolKind.ConSymb)
                {
                    cn = (ConSymb)s;
                    uss = cn.SortSymbol;
                    if (uss.Size != null)
                    {
                        continue;
                    }

                    n = new UserSortDepNode(uss);
                    for (var i = 0; i < s.Arity; ++i)
                    {
                        m = new UserSortDepNode(uss, i);
                        isAny = !cn.IsNew || cn.IsAnyArg(i);
                        deps.Add(m, n, isAny);
                        unn = s.CanonicalForm[i];
                        foreach (var sp in unn.NonRangeMembers)
                        {
                            if (sp.Kind == SymbolKind.UserSortSymb)
                            {
                                ussp = (UserSortSymb)sp;
                                if (ussp.Size != null)
                                {
                                    continue;
                                }

                                deps.Add(new UserSortDepNode(ussp), m, isAny);
                            }
                        }
                    }
                }
                else if (s.Kind == SymbolKind.MapSymb)
                {
                    mp = (MapSymb)s;
                    uss = mp.SortSymbol;
                    if (uss.Size != null)
                    {
                        continue;
                    }

                    n = new UserSortDepNode(uss);
                    for (var i = 0; i < s.Arity; ++i)
                    {
                        m = new UserSortDepNode(uss, i);
                        isAny = mp.IsAnyArg(i);
                        deps.Add(m, n, isAny);
                        unn = s.CanonicalForm[i];
                        foreach (var sp in unn.NonRangeMembers)
                        {
                            if (sp.Kind == SymbolKind.UserSortSymb)
                            {
                                ussp = (UserSortSymb)sp;
                                if (ussp.Size != null)
                                {
                                    continue;
                                }

                                deps.Add(new UserSortDepNode(ussp), m, isAny);
                            }
                        }
                    }
                }
            }

            //// Step 2. Topologically sort and construct upper bounds on arg sizes.
            /*
            deps.Debug_PrintCollection(
                x => x.Arg == UserSortDepNode.SymbolDefinition ? x.Symbol.DataSymbol.FullName : string.Format("{0}[{1}]", x.Symbol.DataSymbol.FullName, x.Arg),
                x => x ? "" : "REL",
                false);
            */

            int pos;
            int nComps;
            bool isInfinite;
            var result = true;
            SizeExpr sizeExpr;
            var sort = deps.GetTopologicalSort(out nComps);
            UserSymbol dataSymb;
            foreach (var scc in sort)
            {
                if (scc.Kind == DependencyNodeKind.Normal)
                {
                    n = scc.Resource;
                    if (n.Arg != UserSortDepNode.SymbolDefinition)
                    {
                        continue;
                    }

                    isInfinite = false;
                    dataSymb = n.Symbol.DataSymbol;
                    var argSizes = new SizeExpr[dataSymb.Arity];
                    foreach (var e in scc.Requests)
                    {
                        pos = e.Target.Resource.Arg;
                        sizeExpr = dataSymb.CanonicalForm[pos].MkSize(!e.Role);
                        result = CheckTotality(n.Symbol, sizeExpr, pos, flags) && result;
                        if (sizeExpr.Kind == SizeExprKind.Infinity)
                        {
                            isInfinite = true;
                        }

                        argSizes[pos] = sizeExpr;
                    }

                    if (n.Symbol.Size == null)
                    {
                        n.Symbol.Size = isInfinite ? SizeExpr.Infinity : new SizeExpr(argSizes);
                        MkRelConstraints(dataSymb, flags);
                        if (dataSymb.Kind == SymbolKind.MapSymb)
                        {
                            MkMapConstraints((MapSymb)dataSymb, argSizes, flags);
                        }
                    }
                }
                else
                {
                    foreach (var dn in scc.InternalNodes)
                    {
                        n = dn.Resource;
                        if (n.Arg != UserSortDepNode.SymbolDefinition)
                        {
                            continue;
                        }

                        isInfinite = false;
                        dataSymb = n.Symbol.DataSymbol;
                        var argSizes = new SizeExpr[dataSymb.Arity];
                        foreach (var e in dn.Requests)
                        {
                            pos = e.Target.Resource.Arg;
                            if (scc.ContainsResource(e.Target.Resource))
                            {
                                if (!e.Role)
                                {
                                    flags.Add(new Flag(
                                        SeverityKind.Error,
                                        dataSymb.Definitions.First<AST<Node>>().Node,
                                        Constants.RelationalError.ToString(dataSymb.FullName, pos + 1),
                                        Constants.RelationalError.Code));
                                    result = false;
                                }

                                isInfinite = true;
                                result = CheckTotality(n.Symbol, SizeExpr.Infinity, pos, flags) && result;
                                argSizes[pos] = SizeExpr.Infinity;
                            }
                            else
                            {
                                sizeExpr = dataSymb.CanonicalForm[pos].MkSize(!e.Role);
                                result = CheckTotality(n.Symbol, sizeExpr, pos, flags) && result;
                                if (sizeExpr.Kind == SizeExprKind.Infinity)
                                {
                                    isInfinite = true;
                                }

                                argSizes[pos] = sizeExpr;
                            }
                        }

                        if (n.Symbol.Size == null)
                        {
                            n.Symbol.Size = isInfinite ? SizeExpr.Infinity : new SizeExpr(argSizes);
                            MkRelConstraints(dataSymb, flags);
                            if (dataSymb.Kind == SymbolKind.MapSymb)
                            {
                                MkMapConstraints((MapSymb)dataSymb, argSizes, flags);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private bool CollectSourceSymbols(List<Flag> flags, CancellationToken cancel)
        {
            bool result = true;
            var modSpan = ModuleData.Source.AST.Node.Span;
            var schemaInst = ASTSchema.Instance;
            var trueSymb = new UserCnstSymb(Root, Factory.Instance.MkId(schemaInst.ConstNameTrue, modSpan), UserCnstSymbKind.New, false);
            var falseSymb = new UserCnstSymb(Root, Factory.Instance.MkId(schemaInst.ConstNameFalse, modSpan), UserCnstSymbKind.New, false);
            result = Root.TryAddSymbol(trueSymb, IncSymbolCount, flags) & Root.TryAddSymbol(falseSymb, IncSymbolCount, flags);

            //// Step 0. Introduce derived constants for contract statements.
            MkContracts(flags, cancel);

            //// Step 1. Collect user constants in type definitions
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.TypeDecl,
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.MkPredicate(NodeKind.Enum),
                    NodePredFactory.Instance.MkPredicate(NodeKind.Id)
                },
                (path, x) =>
                {
                    if (!schemaInst.IsId(((Id)x).Name, false, false, false, false))
                    {
                        //// Qualified and/or type constants are defined elsewhere.
                        return;
                    }

                    result = Root.TryAddSymbol(
                            new UserCnstSymb(Root, (AST<Id>)Factory.Instance.FromAbsPositions(ModuleData.Reduced.Root, path), UserCnstSymbKind.New, false),
                            IncSymbolCount,
                            flags) & result;
                },
                cancel);

            //// Step 2. Collect user type decls
            bool isTransform = ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform;
            UserSymbol newTypeSymb;
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.TypeDecl,
                },
                (path, x) =>
                    {
                        newTypeSymb = MkUserSymbol(Root, Factory.Instance.FromAbsPositions(ModuleData.Reduced.Root, path), false);
                        if (Root.TryAddSymbol(newTypeSymb, IncSymbolCount, flags))
                        {
                            result = Root.TryAddSymbol(
                                new UserCnstSymb(Root, Factory.Instance.MkId(string.Format("#{0}", newTypeSymb.Name), x.Span), UserCnstSymbKind.New, true), 
                                IncSymbolCount, 
                                flags) & result;

                            for (int i = 0; i < newTypeSymb.Arity; ++i)
                            {
                                result = Root.TryAddSymbol(
                                    new UserCnstSymb(Root, Factory.Instance.MkId(string.Format("#{0}[{1}]", newTypeSymb.Name, i), x.Span), UserCnstSymbKind.New, true),
                                    IncSymbolCount,
                                    flags) & result;
                            }

                            if (isTransform && (newTypeSymb.Kind == SymbolKind.MapSymb || (newTypeSymb.Kind == SymbolKind.ConSymb && ((ConSymb)newTypeSymb).IsNew)))
                            {
                                flags.Add(new Flag(
                                    SeverityKind.Error,
                                    x,
                                    Constants.TransNewnessError.ToString(),
                                    Constants.TransNewnessError.Code));
                                result = false;
                            }
                        }
                        else
                        {
                            result = false;
                        }
                    },
                cancel);

            //// Step 3. Collect base constants
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.MkPredicate(NodeKind.Cnst),
                },
                (path, x) => AddBaseCnst((Cnst)x),
                cancel);

            //// Step 4. Collect derived constants
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.MkPredicate(NodeKind.Rule),
                    NodePredFactory.Instance.MkPredicate(NodeKind.Id)
                },
                (path, x) =>
                    {
                        if (!schemaInst.IsId(((Id)x).Name, false, false, false, false))
                        {
                            return;
                        }

                        result = ModuleSpace.TryAddSymbol(
                                        new UserCnstSymb(ModuleSpace, (AST<Id>)Factory.Instance.FromAbsPositions(ModuleData.Reduced.Root, path), UserCnstSymbKind.Derived, false),
                                        IncSymbolCount,
                                        flags) & result;
                    },
                cancel);

            //// Step 5. Mark other ids as arguments to operators as variables.
            bool isIdQualified, isIdTypeCnst, isIdSymbCnst, isIdDontCare, hasTableFrag;
            allSpaceNames = GetAllNames(Root, new Set<string>(string.Compare));
            ModuleData.Reduced.FindAll(
                new NodePred[]
                {
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.MkPredicate(NodeKind.Body),
                    NodePredFactory.Instance.Star,
                    NodePredFactory.Instance.MkPredicate(NodeKind.Id)
                },
                (path, x) =>
                {
                    var id = (Id)x;
                    if (!ASTSchema.Instance.IsId(
                        id.Name, 
                        true, 
                        true, 
                        true,
                        true,
                        allSpaceNames.Contains,
                        out isIdQualified, 
                        out isIdTypeCnst,
                        out isIdSymbCnst,
                        out isIdDontCare,
                        out hasTableFrag) || (isIdDontCare && id.Fragments.Length > 1))
                    {
                        flags.Add(new Flag(
                                    SeverityKind.Error,
                                    id,
                                    Constants.BadId.ToString(id.Name, "variable / selector"),
                                    Constants.BadId.Code));
                        result = false;
                        return;
                    }
                    else if (isIdDontCare || isIdTypeCnst || isIdSymbCnst || hasTableFrag)
                    {
                        return;
                    }

                    UserSymbol existingSymb;
                    string name = id.Fragments[0];

                    //// In this case, then constant is being legally used
                    if (Root.TryGetSymbol(name, out existingSymb) && 
                        existingSymb.Kind == SymbolKind.UserCnstSymb &&
                        id.Fragments.Length == 1)
                    {
                        return;
                    }

                    //// In this case, the symbol is the legal use of a typename.
                    var last = ((LinkedList<ChildInfo>)path).Last.Value;
                    if (last.Context == ChildContextKind.Operator ||
                        last.Context == ChildContextKind.Match)
                    {
                        return;
                    }

                    var prev = ((LinkedList<ChildInfo>)path).Last.Previous.Value;
                    if (prev.Node.NodeKind == NodeKind.Rule)
                    {
                        return;
                    }

                    if (prev.Node.NodeKind == NodeKind.RelConstr &&
                        ((RelConstr)prev.Node).Op == RelKind.Typ &&
                        last.RelativePos == 1)
                    {
                        return;
                    }

                    var varSymbol = new UserCnstSymb(
                        Root, 
                        (AST<Id>)Factory.Instance.FromAbsPositions(ModuleData.Reduced.Root, path), 
                        UserCnstSymbKind.Variable, 
                        false);

                    if (IsAllCapsName(varSymbol.Name))
                    {
                        flags.Add(
                            new Flag(
                                SeverityKind.Warning,
                                x,
                                Constants.DataCnstLikeVarWarning.ToString(varSymbol.Name),
                                Constants.DataCnstLikeVarWarning.Code));
                    }

                    result = Root.TryAddSymbol(
                             varSymbol,
                             IncSymbolCount,
                             flags) & result;
                },
                cancel);

            //// Step 6. Create convenience types
            result = MkConvenienceTypes(flags) & result;

            //// Step 7. Handle specific types.
            if (ModuleData.Reduced.Node.NodeKind == NodeKind.Transform ||
                ModuleData.Reduced.Node.NodeKind == NodeKind.TSystem)
            {
                result = MkTransformParams(flags, cancel) & result;
            }
            else if (ModuleData.Reduced.Node.NodeKind == NodeKind.Model)
            {
                Id modCnst;
                UserSymbol modSymb;
                var model = (Model)ModuleData.Reduced.Node;
                var path = new LinkedList<ChildInfo>();
                var mfCount = 1;
                foreach (var mf in model.Facts)
                {
                    if (mfCount % Node.CancelCheckFreq == 0)
                    {
                        if (cancel.IsCancellationRequested)
                        {
                            result = false;
                            break;
                        }

                        mfCount = 1;
                    }
                    else
                    {
                        ++mfCount;
                    }

                    if (mf.Binding != null)
                    {
                        if (!ASTSchema.Instance.IsId(mf.Binding.Name, false, false, false, true))
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                mf.Binding,
                                Constants.BadId.ToString(mf.Binding.Name, "model alias"),
                                Constants.BadId.Code));
                            result = false;
                        }
                        else if (mf.Binding.Name == ASTSchema.Instance.DontCareName)
                        {
                            ModuleSpace.AddAnonModelConstant(mf.Binding, IncSymbolCount());
                        }
                        else if (!ModuleSpace.TryGetSymbol("%" + mf.Binding.Name, out modSymb))
                        {
                            if (IsAllCapsName(mf.Binding.Name))
                            {
                                flags.Add(
                                    new Flag(
                                        SeverityKind.Warning,
                                        mf.Binding,
                                        Constants.DataCnstLikeSymbWarning.ToString(mf.Binding.Name),
                                        Constants.DataCnstLikeSymbWarning.Code));
                            }

                            ModuleSpace.AddModelConstant(mf.Binding, IncSymbolCount());
                        }
                    }

                    path.AddFirst(new ChildInfo(mf, ChildContextKind.AnyChildContext, -1, -1));                    
                    mf.FindAll(
                        path, 
                        QueryModelFactIds,
                        (ch, x) =>
                            {
                                modCnst = (Id)x;
                                if (!ASTSchema.Instance.IsId(modCnst.Name, true, true, false, true, out isIdQualified, out isIdTypeCnst, out isIdSymbCnst, out isIdDontCare))
                                {
                                    flags.Add(new Flag(
                                                SeverityKind.Error,
                                                modCnst,
                                                Constants.BadId.ToString(modCnst.Name, "constant / alias"),
                                                Constants.BadId.Code));
                                    result = false;
                                    return;
                                }
                                else if (isIdDontCare)
                                {
                                    ModuleSpace.AddAnonModelConstant(modCnst, IncSymbolCount());
                                    return;
                                }
                                else if (isIdTypeCnst || isIdQualified)
                                {
                                    return;
                                }
                                else if (Root.TryGetSymbol(modCnst.Name, out modSymb) && modSymb.IsNewConstant)
                                {
                                    return;
                                }
                                else if (Root.ExistsSymbol("%" + modCnst.Name))
                                {
                                    return;
                                }

                                if (IsAllCapsName(modCnst.Name))
                                {
                                    flags.Add(
                                        new Flag(
                                            SeverityKind.Warning,
                                            modCnst,
                                            Constants.DataCnstLikeSymbWarning.ToString(modCnst.Name),
                                            Constants.DataCnstLikeSymbWarning.Code));
                                }

                                ModuleSpace.AddModelConstant(modCnst, IncSymbolCount());
                            });

                    path.RemoveLast();
                    Contract.Assert(path.Count == 0);
                }
            }

            return result && !cancel.IsCancellationRequested;
        }

        /// <summary>
        /// Returns a set of all unqualified namespace names at or below space.
        /// </summary>
        private Set<string> GetAllNames(Namespace space, Set<string> names)
        {
            if (!string.IsNullOrEmpty(space.Name))
            {
                names.Add(space.Name);
            }

            foreach (var child in space.Children)
            {
                GetAllNames(child, names);
            }

            return names;
        }

        private UserSymbol MkUserSymbol(Namespace nspace, AST<Node> ast, bool isAutoGen)
        {
            Contract.Requires(ast.Node.IsTypeDecl);
            Contract.Requires(nspace != null);
            switch (ast.Node.NodeKind)
            {
                case NodeKind.UnnDecl:
                    return new UnnSymb(nspace, (AST<UnnDecl>)ast, isAutoGen);
                case NodeKind.ConDecl:
                    return new ConSymb(nspace, (AST<ConDecl>)ast, isAutoGen);
                case NodeKind.MapDecl:
                    return new MapSymb(nspace, (AST<MapDecl>)ast, isAutoGen);
                default:
                    throw new NotImplementedException();                                  
            }
        }

        private void AddBaseCnst(Cnst cnst)
        {
            Contract.Requires(cnst != null);

            switch (cnst.CnstKind)
            {
                case CnstKind.String:
                    GetCnstSymbol((string)cnst.Raw);
                    break;
                case CnstKind.Numeric:
                    GetCnstSymbol((Rational)cnst.Raw);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private void MkSorts()
        {
            AddSort(new BaseSortSymb(BaseSortKind.NegInteger));
            AddSort(new BaseSortSymb(BaseSortKind.PosInteger));
            AddSort(new BaseSortSymb(BaseSortKind.Natural));
            AddSort(new BaseSortSymb(BaseSortKind.Integer));
            AddSort(new BaseSortSymb(BaseSortKind.Real));
            AddSort(new BaseSortSymb(BaseSortKind.String));
        }

        private bool MkConvenienceTypes(List<Flag> flags)
        {
            var result = true;
            var span = ModuleData.Source.AST.Node.Span;

            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Domain)
            {
                //// ---------- Create derived constants for relational constraints.
                result = MkRelSubSymbols(span, flags) & result;

                var userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(ConformsName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(NotRelCnstrName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(NotFunCnstrName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(NotInjCnstrName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(NotTotalCnstrName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(NotInvTotalCnstrName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);
            }

            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Domain ||
                ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform ||
                ModuleData.Source.AST.Node.NodeKind == NodeKind.TSystem)
            {
                //// ---------- Create type constants for the convenience types.
                result = Root.TryAddSymbol(
                    new UserCnstSymb(Root, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameBoolean), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) & result;
                result = ModuleSpace.TryAddSymbol(
                    new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameAny), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) & result;
                result = ModuleSpace.TryAddSymbol(
                    new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameData), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) & result;
                result = ModuleSpace.TryAddSymbol(
                    new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameConstant), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) & result;
                Contract.Assert(result);

                //// ---------- The boolean type
                var boolBody = Factory.Instance.MkEnum(span);
                boolBody = Factory.Instance.AddElement(boolBody, Factory.Instance.MkId(ASTSchema.Instance.ConstNameTrue, span));
                boolBody = Factory.Instance.AddElement(boolBody, Factory.Instance.MkId(ASTSchema.Instance.ConstNameFalse, span));
                var boolSymb = new UnnSymb(Root, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameBoolean, boolBody, span), false);
                result = Root.TryAddSymbol(boolSymb, IncSymbolCount, flags) & result;

                //// ---------- Any Part 1
                var anyBody = MkUserOpUnion(Factory.Instance.MkUnion(span), Root, false, span);

                //// ---------- Constant
                var usrCnsts = Factory.Instance.MkEnum(span);
                usrCnsts = MkUserConstants(usrCnsts, Root, true, span);

                string name;
                ASTSchema.Instance.TryGetSortName(BaseSortKind.Real, out name);
                var cnstsBody = Factory.Instance.MkUnion(span);
                cnstsBody = Factory.Instance.AddUnnCmp(cnstsBody, Factory.Instance.MkId(name, span));
                anyBody = Factory.Instance.AddUnnCmp(anyBody, Factory.Instance.MkId(name, span));

                ASTSchema.Instance.TryGetSortName(BaseSortKind.String, out name);
                cnstsBody = Factory.Instance.AddUnnCmp(cnstsBody, Factory.Instance.MkId(name, span));
                anyBody = Factory.Instance.AddUnnCmp(anyBody, Factory.Instance.MkId(name, span));
                cnstsBody = Factory.Instance.AddUnnCmp(cnstsBody, usrCnsts);

                var cnstsSymb = new UnnSymb(ModuleSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameConstant, cnstsBody, span), false);
                result = ModuleSpace.TryAddSymbol(cnstsSymb, IncSymbolCount, flags) & result;

                //// ---------- Data
                var dataBody = MkUserOpUnion(Factory.Instance.MkUnion(span), Root, true, span);
                dataBody = Factory.Instance.AddUnnCmp(dataBody, Factory.Instance.MkId(ModuleSpace.Name + "." + ASTSchema.Instance.TypeNameConstant, span));
                var dataSymb = new UnnSymb(ModuleSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameData, dataBody, span), false);
                result = ModuleSpace.TryAddSymbol(dataSymb, IncSymbolCount, flags) & result;

                //// ---------- Any Part 2
                usrCnsts = Factory.Instance.MkEnum(span);
                usrCnsts = MkUserConstants(usrCnsts, Root, false, span);
                anyBody = Factory.Instance.AddUnnCmp(anyBody, usrCnsts);
                var anySymb = new UnnSymb(ModuleSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameAny, anyBody, span), false);
                result = ModuleSpace.TryAddSymbol(anySymb, IncSymbolCount, flags) & result;
            }

            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform ||
                ModuleData.Source.AST.Node.NodeKind == NodeKind.Model)
            {
                var userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(RequiresName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                userCnst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId(EnsuresName, span), UserCnstSymbKind.Derived, true);
                result = ModuleSpace.TryAddSymbol(userCnst, IncSymbolCount, flags) & result;
                AddPrHdSymb(userCnst);

                //// --------- Create the scValue auxiliary constructor
                var spaceName = ModuleData.Source.AST.Node.NodeKind == NodeKind.Model ?
                    ((Model)ModuleData.Source.AST.Node).Domain.Name :
                    ModuleSpace.Name;

                var scValueDecl = Factory.Instance.MkConDecl(SCValueName, false, span);
                scValueDecl = Factory.Instance.AddField(scValueDecl, Factory.Instance.MkField(
                    null,
                    Factory.Instance.MkId(spaceName + "." + ASTSchema.Instance.TypeNameAny, span),
                    false,
                    span));
                scValueDecl = Factory.Instance.AddField(scValueDecl, Factory.Instance.MkField(
                    null,
                    Factory.Instance.MkId(spaceName + "." + ASTSchema.Instance.TypeNameAny, span),
                    false,
                    span));

                var scValue = new ConSymb(Root, scValueDecl, true);
                result = Root.TryAddSymbol(scValue, IncSymbolCount, flags) & result;
                AddPrHdSymb(scValue);
            }

            return result;
        }

        private bool MkRelSubSymbols(Span span, List<Flag> flags)
        {
            UserSymbol subSymb;
            string subSymbName;
            bool hasRelConstraints;
            var defs = new LinkedList<AST<ConDecl>>();
            foreach (var symb in Root.Symbols)
            {
                hasRelConstraints = false;
                if (symb.Kind == SymbolKind.ConSymb)
                {
                    var con = ((AST<ConDecl>)symb.Definitions.First()).Node;
                    if (!con.IsNew)
                    {
                        continue;
                    }

                    foreach (var f in con.Fields)
                    {
                        if (!f.IsAny)
                        {
                            hasRelConstraints = true;
                            break;
                        }
                    }
                }
                else if (symb.Kind == SymbolKind.MapSymb)
                {
                    var map = ((AST<MapDecl>)symb.Definitions.First()).Node;
                    foreach (var f in map.Dom)
                    {
                        if (!f.IsAny)
                        {
                            hasRelConstraints = true;
                            break;
                        }
                    }

                    if (!hasRelConstraints)
                    {
                        foreach (var f in map.Cod)
                        {
                            if (!f.IsAny)
                            {
                                hasRelConstraints = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    continue;
                }

                if (!hasRelConstraints)
                {
                    continue;
                }

                subSymbName = RelSubName + ManglePrefix + symb.FullName;
                if (Root.TryGetSymbol(subSymbName, out subSymb))
                {
                    continue;
                }

                defs.AddLast(
                    Factory.Instance.AddField(
                        Factory.Instance.MkSubDecl(subSymbName, span), 
                        Factory.Instance.MkField(
                            null,
                            Factory.Instance.MkId(symb.FullName, span),
                            false,
                            span)));
            }

            bool result = true;
            foreach (var def in defs)
            {
                subSymb = new ConSymb(Root, def, true);
                result = Root.TryAddSymbol(subSymb, IncSymbolCount, flags) && result;
                AddPrHdSymb(subSymb);
            }

            return result;
        }

        private AST<Id> MkArgVar(int index, bool primed, Span span, List<Flag> flags)
        {
            UserSymbol argSymb;
            var argName = string.Format("{0}{1}{2}", ArgPrefixName, index + 1, primed ? "'" : "");
            var argId = Factory.Instance.MkId(argName, span);
            if (!Root.TryGetSymbol(argName, out argSymb))
            {
                argSymb = new UserCnstSymb(Root, argId, UserCnstSymbKind.Variable, true);
                var result = Root.TryAddSymbol(argSymb, IncSymbolCount, flags);
                Contract.Assert(result);
                result = argSymb.ResolveTypes(this, flags, default(CancellationToken));
                Contract.Assert(result);
            }

            return argId;
        }

        private AST<Union> MkUserOpUnion(AST<Union> union, Namespace space, bool onlyNew, Span span)
        {
            var result = union;
            foreach (var s in space.Symbols)
            {
                if (s.Kind == SymbolKind.ConSymb && (!onlyNew || ((ConSymb)s).IsNew))
                {
                    result = Factory.Instance.AddUnnCmp(result, Factory.Instance.MkId(s.FullName, span));
                }
                else if (s.Kind == SymbolKind.MapSymb)
                {
                    result = Factory.Instance.AddUnnCmp(result, Factory.Instance.MkId(s.FullName, span));
                }
            }

            foreach (var child in space.Children)
            {
                result = MkUserOpUnion(result, child, onlyNew, span);
            }

            return result;
        }

        private AST<API.Nodes.Enum> MkUserConstants(AST<API.Nodes.Enum> enm, Namespace space, bool onlyNew, Span span)
        {
            var result = enm;
            UserCnstSymb cnstSymb;
            foreach (var s in space.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                cnstSymb = (UserCnstSymb)s;
                if (cnstSymb.UserCnstKind == UserCnstSymbKind.Variable || 
                    cnstSymb.IsSymbolicConstant || 
                    (onlyNew && cnstSymb.UserCnstKind == UserCnstSymbKind.Derived))
                {
                    continue;
                }

                result = Factory.Instance.AddElement(result, Factory.Instance.MkId(s.FullName, span));
            }

            foreach (var child in space.Children)
            {
                result = MkUserConstants(result, child, onlyNew, span);
            }

            return result;
        }


        private void MkBaseOps()
        {
            AddBaseOp(new BaseOpSymb(
                OpKind.Add, 
                2,
                OpLibrary.ValidateUse_Add, 
                OpLibrary.TypeApprox_Add_Up,
                OpLibrary.TypeApprox_Add_Down,
                OpLibrary.Evaluator_Add));

            AddBaseOp(new BaseOpSymb(
                OpKind.And, 
                2, 
                OpLibrary.ValidateUse_And,
                OpLibrary.TypeApprox_And_Up,
                OpLibrary.TypeApprox_And_Down,
                OpLibrary.Evaluator_And));

            AddBaseOp(new BaseOpSymb(
                OpKind.AndAll,
                2,
                OpLibrary.ValidateUse_AndAll,
                OpLibrary.TypeApprox_AndAll_Up,
                OpLibrary.TypeApprox_AndAll_Down,
                OpLibrary.Evaluator_AndAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Count, 
                1, 
                OpLibrary.ValidateUse_Count,
                OpLibrary.TypeApprox_Count_Up,
                OpLibrary.TypeApprox_Count_Down,
                OpLibrary.Evaluator_Count));

            AddBaseOp(new BaseOpSymb(
                OpKind.Div, 
                2, 
                OpLibrary.ValidateUse_Div,
                OpLibrary.TypeApprox_Div_Up,
                OpLibrary.TypeApprox_Div_Down,
                OpLibrary.Evaluator_Div,
                OpLibrary.AppConstrainer_BinArg2NonZero));

            AddBaseOp(new BaseOpSymb(
                OpKind.GCD, 
                2, 
                OpLibrary.ValidateUse_GCD,
                OpLibrary.TypeApprox_GCD_Up,
                OpLibrary.TypeApprox_GCD_Down,
                OpLibrary.Evaluator_GCD));

            AddBaseOp(new BaseOpSymb(
                OpKind.GCDAll,
                2,
                OpLibrary.ValidateUse_GCDAll,
                OpLibrary.TypeApprox_GCDAll_Up,
                OpLibrary.TypeApprox_GCDAll_Down,
                OpLibrary.Evaluator_GCDAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Impl, 
                2, 
                OpLibrary.ValidateUse_Impl,
                OpLibrary.TypeApprox_Impl_Up,
                OpLibrary.TypeApprox_Impl_Down,
                OpLibrary.Evaluator_Impl));

            AddBaseOp(new BaseOpSymb(
                OpKind.LCM, 
                2, 
                OpLibrary.ValidateUse_LCM,
                OpLibrary.TypeApprox_LCM_Up,
                OpLibrary.TypeApprox_LCM_Down,
                OpLibrary.Evaluator_LCM));

            AddBaseOp(new BaseOpSymb(
                OpKind.LCMAll,
                2,
                OpLibrary.ValidateUse_LCMAll,
                OpLibrary.TypeApprox_LCMAll_Up,
                OpLibrary.TypeApprox_LCMAll_Down,
                OpLibrary.Evaluator_LCMAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Max,
                2,
                OpLibrary.ValidateUse_Max,
                OpLibrary.TypeApprox_Max_Up,
                OpLibrary.TypeApprox_Max_Down,
                OpLibrary.Evaluator_Max));

            AddBaseOp(new BaseOpSymb(
                OpKind.MaxAll, 
                2, 
                OpLibrary.ValidateUse_MaxAll,
                OpLibrary.TypeApprox_Unconstrained2_Up,
                OpLibrary.TypeApprox_Unconstrained2_Down,
                OpLibrary.Evaluator_MaxAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Min,
                2,
                OpLibrary.ValidateUse_Min,
                OpLibrary.TypeApprox_Min_Up,
                OpLibrary.TypeApprox_Min_Down,
                OpLibrary.Evaluator_Min));

            AddBaseOp(new BaseOpSymb(
                OpKind.MinAll, 
                2, 
                OpLibrary.ValidateUse_MinAll,
                OpLibrary.TypeApprox_Unconstrained2_Up,
                OpLibrary.TypeApprox_Unconstrained2_Down,
                OpLibrary.Evaluator_MinAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Mod, 
                2, 
                OpLibrary.ValidateUse_Mod,
                OpLibrary.TypeApprox_Mod_Up,
                OpLibrary.TypeApprox_Mod_Down,
                OpLibrary.Evaluator_Mod,
                OpLibrary.AppConstrainer_BinArg2NonZero));

            AddBaseOp(new BaseOpSymb(
                OpKind.Mul, 
                2, 
                OpLibrary.ValidateUse_Mul,
                OpLibrary.TypeApprox_Mul_Up,
                OpLibrary.TypeApprox_Mul_Down,
                OpLibrary.Evaluator_Mul));

            AddBaseOp(new BaseOpSymb(
                OpKind.Neg, 
                1, 
                OpLibrary.ValidateUse_Neg,
                OpLibrary.TypeApprox_Neg_Up,
                OpLibrary.TypeApprox_Neg_Down,
                OpLibrary.Evaluator_Neg));

            AddBaseOp(new BaseOpSymb(
                OpKind.Not, 
                1, 
                OpLibrary.ValidateUse_Not,
                OpLibrary.TypeApprox_Not_Up,
                OpLibrary.TypeApprox_Not_Down,
                OpLibrary.Evaluator_Not));

            AddBaseOp(new BaseOpSymb(
                OpKind.Or, 
                2, 
                OpLibrary.ValidateUse_Or,
                OpLibrary.TypeApprox_Or_Up,
                OpLibrary.TypeApprox_Or_Down,
                OpLibrary.Evaluator_Or));

            AddBaseOp(new BaseOpSymb(
                OpKind.OrAll,
                2,
                OpLibrary.ValidateUse_OrAll,
                OpLibrary.TypeApprox_OrAll_Up,
                OpLibrary.TypeApprox_OrAll_Down,
                OpLibrary.Evaluator_OrAll));

            AddBaseOp(new BaseOpSymb(
                OpKind.Prod, 
                2, 
                OpLibrary.ValidateUse_Prod,
                OpLibrary.TypeApprox_Prod_Up,
                OpLibrary.TypeApprox_Prod_Down,
                OpLibrary.Evaluator_Prod));

            AddBaseOp(new BaseOpSymb(
                OpKind.Qtnt,
                2,
                OpLibrary.ValidateUse_Qtnt,
                OpLibrary.TypeApprox_Qtnt_Up,
                OpLibrary.TypeApprox_Qtnt_Down,
                OpLibrary.Evaluator_Qtnt,
                OpLibrary.AppConstrainer_BinArg2NonZero));

            AddBaseOp(new BaseOpSymb(
                OpKind.Sub, 
                2, 
                OpLibrary.ValidateUse_Sub,
                OpLibrary.TypeApprox_Sub_Up,
                OpLibrary.TypeApprox_Sub_Down,
                OpLibrary.Evaluator_Sub));

            AddBaseOp(new BaseOpSymb(
                OpKind.Sum, 
                2, 
                OpLibrary.ValidateUse_Sum,
                OpLibrary.TypeApprox_Sum_Up,
                OpLibrary.TypeApprox_Sum_Down,
                OpLibrary.Evaluator_Sum));

            AddBaseOp(new BaseOpSymb(
                OpKind.Sign,
                1,
                OpLibrary.ValidateUse_Sign,
                OpLibrary.TypeApprox_Sign_Up,
                OpLibrary.TypeApprox_Sign_Down,
                OpLibrary.Evaluator_Sign));

            AddBaseOp(new BaseOpSymb(
                OpKind.LstLength,
                2,
                OpLibrary.ValidateUse_LstLength,
                OpLibrary.TypeApprox_LstLength_Up,
                OpLibrary.TypeApprox_LstLength_Down,
                OpLibrary.Evaluator_LstLength));

            AddBaseOp(new BaseOpSymb(
                OpKind.LstReverse,
                2,
                OpLibrary.ValidateUse_LstReverse,
                OpLibrary.TypeApprox_LstReverse_Up,
                OpLibrary.TypeApprox_LstReverse_Down,
                OpLibrary.Evaluator_LstReverse));

            AddBaseOp(new BaseOpSymb(
                OpKind.RflIsMember,
                2,
                OpLibrary.ValidateUse_RflIsMember,
                OpLibrary.TypeApprox_RflIsMember_Up,
                OpLibrary.TypeApprox_RflIsMember_Down,
                OpLibrary.Evaluator_RflIsMember));

            AddBaseOp(new BaseOpSymb(
                OpKind.RflIsSubtype,
                2,
                OpLibrary.ValidateUse_RflIsSubtype,
                OpLibrary.TypeApprox_RflIsSubtype_Up,
                OpLibrary.TypeApprox_RflIsSubtype_Down,
                OpLibrary.Evaluator_RflIsSubtype));

            AddBaseOp(new BaseOpSymb(
                OpKind.RflGetArgType,
                2,
                OpLibrary.ValidateUse_RflGetArgType,
                OpLibrary.TypeApprox_RflGetArgType_Up,
                OpLibrary.TypeApprox_RflGetArgType_Down,
                OpLibrary.Evaluator_RflGetArgType,
                OpLibrary.AppConstrainer_RflGetArgType));

            AddBaseOp(new BaseOpSymb(
                OpKind.RflGetArity,
                1,
                OpLibrary.ValidateUse_RflGetArity,
                OpLibrary.TypeApprox_RflGetArity_Up,
                OpLibrary.TypeApprox_RflGetArity_Down,
                OpLibrary.Evaluator_RflGetArity));

            AddBaseOp(new BaseOpSymb(
                OpKind.IsSubstring, 
                2,
                OpLibrary.ValidateUse_IsSubstring,
                OpLibrary.TypeApprox_IsSubstring_Up,
                OpLibrary.TypeApprox_IsSubstring_Down,
                OpLibrary.Evaluator_IsSubstring));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrAfter,
                2,
                OpLibrary.ValidateUse_StrAfter,
                OpLibrary.TypeApprox_StrAfter_Up,
                OpLibrary.TypeApprox_StrAfter_Down,
                OpLibrary.Evaluator_StrAfter));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrBefore,
                2,
                OpLibrary.ValidateUse_StrBefore,
                OpLibrary.TypeApprox_StrBefore_Up,
                OpLibrary.TypeApprox_StrBefore_Down,
                OpLibrary.Evaluator_StrBefore));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrFind,
                3,
                OpLibrary.ValidateUse_StrFind,
                OpLibrary.TypeApprox_StrFind_Up,
                OpLibrary.TypeApprox_StrFind_Down,
                OpLibrary.Evaluator_StrFind));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrGetAt,
                2,
                OpLibrary.ValidateUse_StrGetAt,
                OpLibrary.TypeApprox_StrGetAt_Up,
                OpLibrary.TypeApprox_StrGetAt_Down,
                OpLibrary.Evaluator_StrGetAt));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrJoin,
                2,
                OpLibrary.ValidateUse_StrJoin,
                OpLibrary.TypeApprox_StrJoin_Up,
                OpLibrary.TypeApprox_StrJoin_Down,
                OpLibrary.Evaluator_StrJoin));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrLength,
                1,
                OpLibrary.ValidateUse_StrLength,
                OpLibrary.TypeApprox_StrLength_Up,
                OpLibrary.TypeApprox_StrLength_Down,
                OpLibrary.Evaluator_StrLength));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrLower,
                1,
                OpLibrary.ValidateUse_StrLower,
                OpLibrary.TypeApprox_StrLower_Up,
                OpLibrary.TypeApprox_StrLower_Down,
                OpLibrary.Evaluator_StrLower));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrReverse,
                1,
                OpLibrary.ValidateUse_StrReverse,
                OpLibrary.TypeApprox_StrReverse_Up,
                OpLibrary.TypeApprox_StrReverse_Down,
                OpLibrary.Evaluator_StrReverse));

            AddBaseOp(new BaseOpSymb(
                OpKind.StrUpper,
                1,
                OpLibrary.ValidateUse_StrUpper,
                OpLibrary.TypeApprox_StrUpper_Up,
                OpLibrary.TypeApprox_StrUpper_Down,
                OpLibrary.Evaluator_StrUpper));

            AddBaseOp(new BaseOpSymb(
                OpKind.ToList,
                3,
                OpLibrary.ValidateUse_ToList,
                OpLibrary.TypeApprox_ToList_Up,
                OpLibrary.TypeApprox_ToList_Down,
                OpLibrary.Evaluator_ToList,
                OpLibrary.AppConstrainer_ToList));
             
            AddBaseOp(new BaseOpSymb(
                OpKind.ToNatural,
                1,
                OpLibrary.ValidateUse_ToNatural,
                OpLibrary.TypeApprox_ToNatural_Up,
                OpLibrary.TypeApprox_ToNatural_Down,
                OpLibrary.Evaluator_ToNatural));

            AddBaseOp(new BaseOpSymb(
                OpKind.ToString,
                1,
                OpLibrary.ValidateUse_ToString,
                OpLibrary.TypeApprox_ToString_Up,
                OpLibrary.TypeApprox_ToString_Down,
                OpLibrary.Evaluator_ToString));

            AddBaseOp(new BaseOpSymb(
                OpKind.ToSymbol,
                1,
                OpLibrary.ValidateUse_ToSymbol,
                OpLibrary.TypeApprox_ToSymbol_Up,
                OpLibrary.TypeApprox_ToSymbol_Down,
                OpLibrary.Evaluator_ToSymbol));

            AddBaseOp(new BaseOpSymb(
                RelKind.No, 
                1, 
                OpLibrary.ValidateUse_No,
                OpLibrary.TypeApprox_No_Up,
                OpLibrary.TypeApprox_No_Down,
                OpLibrary.Evaluator_No));

            AddBaseOp(new BaseOpSymb(
                RelKind.Eq, 
                2, 
                OpLibrary.ValidateUse_Eq,
                OpLibrary.TypeApprox_Eq_Up,
                OpLibrary.TypeApprox_Eq_Down,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                RelKind.Neq, 
                2, 
                OpLibrary.ValidateUse_Neq,
                OpLibrary.TypeApprox_NEq_Up,
                OpLibrary.TypeApprox_NEq_Down,
                OpLibrary.Evaluator_Neq));

            AddBaseOp(new BaseOpSymb(
                RelKind.Le, 
                2, 
                OpLibrary.ValidateUse_Le,
                OpLibrary.TypeApprox_Le_Up,
                OpLibrary.TypeApprox_Le_Down,
                OpLibrary.Evaluator_Le));

            AddBaseOp(new BaseOpSymb(
                RelKind.Lt, 
                2, 
                OpLibrary.ValidateUse_Lt,
                OpLibrary.TypeApprox_Lt_Up,
                OpLibrary.TypeApprox_Lt_Down,
                OpLibrary.Evaluator_Lt));

            AddBaseOp(new BaseOpSymb(
                RelKind.Ge, 
                2, 
                OpLibrary.ValidateUse_Ge,
                OpLibrary.TypeApprox_Ge_Up,
                OpLibrary.TypeApprox_Ge_Down,
                OpLibrary.Evaluator_Ge));

            AddBaseOp(new BaseOpSymb(
                RelKind.Gt, 
                2, 
                OpLibrary.ValidateUse_Gt,
                OpLibrary.TypeApprox_Gt_Up,
                OpLibrary.TypeApprox_Gt_Down,
                OpLibrary.Evaluator_Gt));

            AddBaseOp(new BaseOpSymb(
                RelKind.Typ, 
                2, 
                OpLibrary.ValidateUse_Typ,
                OpLibrary.TypeApprox_Reserved,
                OpLibrary.TypeApprox_Reserved,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Find, 
                3, 
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Conj,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.ConjR,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Disj,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Compr,
                3,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Proj,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.PRule,
                3,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.CRule,
                3,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Rule,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Range,
                2,
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Select, 
                2, 
                OpLibrary.ValidateUse_Reserved,
                OpLibrary.TypeApprox_Sel_Up,
                OpLibrary.TypeApprox_Sel_Down,
                OpLibrary.Evaluator_Select));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.Relabel, 
                3, 
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Relabel));

            AddBaseOp(new BaseOpSymb(
                ReservedOpKind.TypeUnn, 
                2, 
                OpLibrary.ValidateUse_Reserved,
                null,
                null,
                OpLibrary.Evaluator_Reserved));
        }

        private void AddSort(BaseSortSymb sort)
        {
            var flags = new List<Flag>();
            sort.Id = IncSymbolCount();
            baseSorts.Add(sort.SortKind, sort);
            var userSymb = new UnnSortSymb(Root, ModuleData.Source.AST.Node.Span, sort);
            var result = Root.TryAddSymbol(userSymb, IncSymbolCount, flags);
            Contract.Assert(result);

            result = Root.TryAddSymbol(
                           new UserCnstSymb(Root, Factory.Instance.MkId(string.Format("#{0}", userSymb.Name), ModuleData.Source.AST.Node.Span), UserCnstSymbKind.New, true),
                           IncSymbolCount,         
                           flags);
            Contract.Assert(result);
        }

        private void AddBaseOp(BaseOpSymb bop)
        {
            bop.Id = IncSymbolCount();
            if (bop.OpKind is OpKind)
            {
                baseOps.Add((OpKind)bop.OpKind, bop);
            }
            else if (bop.OpKind is ReservedOpKind)
            {
                resBaseOps.Add((ReservedOpKind)bop.OpKind, bop);
            }
            else if (bop.OpKind is RelKind)
            {
                relOps.Add((RelKind)bop.OpKind, bop);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private bool ImportSymbols(List<Flag> flags, CancellationToken cancel)
        {
            if (!isTemporaryTable && !ValidateCompositions(flags, cancel))
            {
                return false;
            }

            NodePred[] importQuery;
            switch (ModuleData.Source.AST.Node.NodeKind)
            {
                case NodeKind.Domain:
                    importQuery = new NodePred[]
                    {
                        NodePredFactory.Instance.MkPredicate(NodeKind.Domain),
                        NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
                    };

                    break;
                case NodeKind.Transform:
                    importQuery = new NodePred[]
                    {
                        NodePredFactory.Instance.MkPredicate(NodeKind.Transform),
                        NodePredFactory.Instance.MkPredicate(NodeKind.Param),
                        NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
                    };

                    break;
                case NodeKind.TSystem:
                    importQuery = new NodePred[]
                    {
                        NodePredFactory.Instance.MkPredicate(NodeKind.TSystem),
                        NodePredFactory.Instance.MkPredicate(NodeKind.Param),
                        NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
                    };

                    break;
                case NodeKind.Model:
                    importQuery = new NodePred[]
                    {
                        NodePredFactory.Instance.MkPredicate(NodeKind.Model),
                        NodePredFactory.Instance.MkPredicate(NodeKind.ModRef)
                    };

                    break;
                default:
                    throw new NotImplementedException();
            }

            bool result = true;
            ModuleData.Source.AST.FindAll(
                importQuery,
                (path, x) => result = ImportSymbols((ModRef)x, flags, cancel) & result,
                cancel);

            return result;
        }

        private bool ValidateCompositions(List<Flag> flags, CancellationToken cancel)
        {
            bool succeeded = true;
            Location loc;
            switch (ModuleData.Source.AST.Node.NodeKind)
            {
                case NodeKind.Domain:
                    var dom = (Domain)ModuleData.Source.AST.Node;
                    foreach (var mr in dom.Compositions)
                    {
                        loc = (Location)mr.CompilerData;
                        if (loc.AST.Node.NodeKind != NodeKind.Domain)
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                mr,
                                Constants.BadComposition.ToString(mr.Name, "module must be a domain"),
                                Constants.BadComposition.Code));
                            succeeded = false;
                        }
                    }

                    break;
                case NodeKind.Transform:
                    var trans = (Transform)ModuleData.Source.AST.Node;
                    foreach (var pr in trans.Inputs.Concat<Param>(trans.Outputs))
                    {
                        if (pr.IsValueParam)
                        {
                            continue;
                        }

                        var mr = (ModRef)pr.Type;
                        loc = (Location)(mr.CompilerData);
                        if (loc.AST.Node.NodeKind != NodeKind.Domain)
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                pr,
                                Constants.BadComposition.ToString(mr.Name, "module must be a domain"),
                                Constants.BadComposition.Code));
                            succeeded = false;
                        }
                    }

                    break;
                case NodeKind.TSystem:
                    var tsys = (TSystem)ModuleData.Source.AST.Node;
                    foreach (var pr in tsys.Inputs.Concat<Param>(tsys.Outputs))
                    {
                        if (pr.IsValueParam)
                        {
                            continue;
                        }

                        var mr = (ModRef)pr.Type;
                        loc = (Location)(mr.CompilerData);
                        if (loc.AST.Node.NodeKind != NodeKind.Domain)
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                pr,
                                Constants.BadComposition.ToString(mr.Name, "module must be a domain"),
                                Constants.BadComposition.Code));
                            succeeded = false;
                        }
                    }

                    break;
                case NodeKind.Model:
                    var model = (Model)ModuleData.Source.AST.Node;
                    loc = (Location)model.Domain.CompilerData;
                    if (loc.AST.Node.NodeKind != NodeKind.Domain)
                    {
                        flags.Add(new Flag(
                            SeverityKind.Error,
                            model.Domain,
                            Constants.BadComposition.ToString(model.Domain.Name, "module must be a domain"),
                            Constants.BadComposition.Code));
                        succeeded = false;
                    }

                    foreach (var mr in model.Compositions)
                    {
                        loc = (Location)mr.CompilerData;
                        if (loc.AST.Node.NodeKind != NodeKind.Model)
                        {
                            flags.Add(new Flag(
                                SeverityKind.Error,
                                mr,
                                Constants.BadComposition.ToString(mr.Name, "Module must be a model"),
                                Constants.BadComposition.Code));
                            succeeded = false;
                        }
                    }

                    break;
                default:
                    throw new NotImplementedException();
            }

            return succeeded;
        }

        private bool MkTransformParams(List<Flag> flags, CancellationToken cancel)
        {
            if (ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform)
            {
                var transform = (Transform)(ModuleData.Source.AST.Node);
                var paramNames = new Map<string, Node>(string.CompareOrdinal);
                return MkTransformParams(true, transform.Inputs, paramNames, flags, cancel) &
                       MkTransformParams(false, transform.Outputs, paramNames, flags, cancel);
            }
            else if (ModuleData.Source.AST.Node.NodeKind == NodeKind.TSystem)
            {
                var transform = (TSystem)(ModuleData.Source.AST.Node);
                var paramNames = new Map<string, Node>(string.CompareOrdinal);
                return MkTransformParams(true, transform.Inputs, paramNames, flags, cancel) &
                       MkTransformParams(false, transform.Outputs, paramNames, flags, cancel);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private bool MkTransformParams(
            bool isInputParams,
            IEnumerable<Param> transParams, 
            Map<string, Node> paramNames,
            List<Flag> flags, 
            CancellationToken cancel)
        {
            Node otherDef;
            var result = true;
            string name;
            foreach (var p in transParams)
            {
                if (p.Type.NodeKind == NodeKind.ModRef)
                {
                    name = ((ModRef)p.Type).Rename;
                }
                else
                {
                    name = p.Name;
                }

                if (!ASTSchema.Instance.IsId(name, false, false, false, false))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        p,
                        Constants.BadId.ToString(name, "parameter"),
                        Constants.BadId.Code);
                    flags.Add(flag);
                    result = false;
                }
                else if (paramNames.TryFindValue(name, out otherDef))
                {
                    var flag = new Flag(
                        SeverityKind.Error,
                        p,
                        Constants.DuplicateDefs.ToString(
                            "parameter " + name,
                            p.GetCodeLocationString(Env.Parameters),
                            otherDef.GetCodeLocationString(Env.Parameters)),
                        Constants.DuplicateDefs.Code);
                    flags.Add(flag);
                    result = false;
                }
                else
                {
                    paramNames.Add(name, p);
                }

                if (p.Type.NodeKind != NodeKind.ModRef)
                {
                    if (isInputParams)
                    {
                        var prmType = Factory.Instance.MkUnnDecl("%" + name + "~Type", Factory.Instance.ToAST(p.Type), p.Type.Span);
                        var prmSymb = new UnnSymb(ModuleSpace, prmType, true);
                        result = ModuleSpace.TryAddSymbol(prmSymb, IncSymbolCount, flags) & result;

                        var symbConst = new UserCnstSymb(ModuleSpace, Factory.Instance.MkId("%" + name, p.Span), UserCnstSymbKind.New, true);
                        result = ModuleSpace.TryAddSymbol(symbConst, IncSymbolCount, flags) & result;
                    }
                    else
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            p,
                            Constants.BadTransform.ToString("parameter " + name, "only model outputs are supported"),
                            Constants.BadTransform.Code);
                        flags.Add(flag);
                        result = false;
                    }
                }
            }

            return result;
        }

        private bool ImportSymbols(ModRef modRef, List<Flag> flags, CancellationToken cancel)
        {
            //// Try to get the symbol table of the modRef.
            if (!(modRef.CompilerData is Location))
            {
                return false;
            }

            var modData = ((Location)modRef.CompilerData).AST.Node.CompilerData as ModuleData;
            if (modData == null || modData.SymbolTable == null || !modData.SymbolTable.IsValid)
            {
                return false;
            }

            var renaming = modRef.Rename;
            if (!string.IsNullOrEmpty(renaming) && !ASTSchema.Instance.IsId(renaming, false, false, false, false))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    modRef,
                    Constants.BadId.ToString(renaming, "renaming"),
                    Constants.BadId.Code);
                flags.Add(flag);
                return false;
            }

            foreach (var str in modData.SymbolTable.stringCnsts.Keys)
            {
                GetCnstSymbol(str);
            }

            foreach (var rat in modData.SymbolTable.ratCnsts.Keys)
            {
                GetCnstSymbol(rat);
            }

            SymbolTable otherTable;
            foreach (var kv in modData.SymbolTable.dependentTables)
            {
                if (dependentTables.TryFindValue(kv.Key, out otherTable))
                {
                    if (otherTable != kv.Value)
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            modRef,
                            Constants.BadComposition.ToString(
                                kv.Key,
                                "cannot depend on two distinct modules with the same short name"),
                            Constants.BadComposition.Code);
                        flags.Add(flag);
                        return false;
                    }
                }
                else
                {
                    dependentTables.Add(kv.Key, kv.Value);
                }
            }

            var result = true;
            Namespace space;
            UserSymbol clone;
            foreach (var s in modData.SymbolTable.Root.DescendantSymbols)
            {
                if (string.IsNullOrEmpty(renaming) && s is UnnSortSymb)
                {
                    continue;
                }

                if (s.Kind == SymbolKind.ConSymb)
                {
                    if (s.Name == SCValueName)
                    {
                        //// The helper constructor does not get imported, but instead redefined.
                        continue; 
                    }

                    result = (GetNamespace(s.Namespace, flags, out space, renaming) &&
                              space.TryAddSymbol(
                                s.CloneSymbol(space, modRef.Span, renaming), 
                                IncSymbolCount, 
                                flags,
                                ((ConSymb)s).SortSymbol.Size.Clone(renaming))) & result;
                }
                else if (s.Kind == SymbolKind.MapSymb)
                {
                    result = (GetNamespace(s.Namespace, flags, out space, renaming) &&
                              space.TryAddSymbol(
                                s.CloneSymbol(space, modRef.Span, renaming),
                                IncSymbolCount,
                                flags,
                                ((MapSymb)s).SortSymbol.Size.Clone(renaming))) & result;
                }
                else if (s.Kind == SymbolKind.UnnSymb)
                {
                    result = (GetNamespace(s.Namespace, flags, out space, renaming) &&
                              space.TryAddSymbol(s.CloneSymbol(space, modRef.Span, renaming), IncSymbolCount, flags)) & result;
                }
                else if (s.Kind == SymbolKind.UserCnstSymb)
                {
                    var cnst = (UserCnstSymb)s;
                    if ((cnst.UserCnstKind == UserCnstSymbKind.New && !cnst.IsTypeConstant && !cnst.IsSymbolicConstant) ||
                        cnst.UserCnstKind == UserCnstSymbKind.Variable)
                    {
                        if (!isTemporaryTable || cnst.UserCnstKind != UserCnstSymbKind.Variable)
                        {
                            result = Root.TryAddSymbol(cnst.CloneSymbol(Root, modRef.Span, renaming), IncSymbolCount, flags) & result;
                        }
                    }
                    else
                    {
                        if (GetNamespace(s.Namespace, flags, out space, renaming))
                        {
                            clone = s.CloneSymbol(space, modRef.Span, renaming);
                            result = space.TryAddSymbol(clone, IncSymbolCount, flags) & result;
                            if (modData.SymbolTable.protectedHeadSymbols.Contains(s))
                            {
                                AddPrHdSymb(clone);
                            }
                        }
                        else 
                        {
                            result = false;
                        }                          
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            if (!string.IsNullOrEmpty(renaming) &&
                GetNamespace(modData.SymbolTable.Root, flags, out space, renaming) &&
                (ModuleData.Source.AST.Node.NodeKind == NodeKind.Domain ||
                 ModuleData.Source.AST.Node.NodeKind == NodeKind.Transform ||
                 ModuleData.Source.AST.Node.NodeKind == NodeKind.TSystem))
            {
                UnnSymb cType, dType, aType;
                MkRenamedConvenienceTypes(
                    space,
                    modData.SymbolTable,
                    modRef.Span,
                    out cType,
                    out dType,
                    out aType);
                result = space.TryAddSymbol(cType, IncSymbolCount, flags) &
                         space.TryAddSymbol(dType, IncSymbolCount, flags) & 
                         space.TryAddSymbol(aType, IncSymbolCount, flags);
            }

            return result;
        }

        private int IncSymbolCount()
        {
            return nSymbols++;
        }

        private void MkRenamedConvenienceTypes(
                            Namespace dstSpace,
                            SymbolTable srcTable,      
                            Span span,          
                            out UnnSymb constantType,
                            out UnnSymb dataType,
                            out UnnSymb anyType)
        {
            var ctUnn = Factory.Instance.MkUnion(span);
            var dtUnn = Factory.Instance.MkUnion(span);
            var atUnn = Factory.Instance.MkUnion(span);

            UserCnstSymb cnstSymb;
            UserSymbol sCt, sDt, sAt, s;
            foreach (var ns in srcTable.Root.Children)
            {
                sCt = srcTable.Resolve(ns.Name + "." + ASTSchema.Instance.TypeNameConstant, out s) as UnnSymb;
                Contract.Assert(sCt != null && s == null);

                sDt = srcTable.Resolve(ns.Name + "." + ASTSchema.Instance.TypeNameData, out s) as UnnSymb;
                Contract.Assert(sDt != null && s == null);

                sAt = srcTable.Resolve(ns.Name + "." + ASTSchema.Instance.TypeNameAny, out s) as UnnSymb;
                Contract.Assert(sAt != null && s == null);
                foreach (var e in sCt.CanonicalForm[0].NonRangeMembers)
                {
                    if (e.Kind == SymbolKind.BaseCnstSymb)
                    {
                        var enm = Factory.Instance.MkEnum(span);
                        enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        ctUnn = Factory.Instance.AddUnnCmp(ctUnn, enm);
                    }
                    else if (e.Kind == SymbolKind.UserCnstSymb)
                    {
                        cnstSymb = (UserCnstSymb)e;
                        var enm = Factory.Instance.MkEnum(span);
                        if (cnstSymb.IsTypeConstant)
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(dstSpace.FullName + "." + GetName(e), span));
                        }
                        else
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        }

                        ctUnn = Factory.Instance.AddUnnCmp(ctUnn, enm);
                    }
                    else
                    {
                        ctUnn = Factory.Instance.AddUnnCmp(ctUnn, Factory.Instance.MkId(GetName(e), span));
                    }
                }

                foreach (var e in sDt.CanonicalForm[0].NonRangeMembers)
                {
                    if (e.Kind == SymbolKind.BaseCnstSymb)
                    {
                        var enm = Factory.Instance.MkEnum(span);
                        enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        dtUnn = Factory.Instance.AddUnnCmp(dtUnn, enm);
                    }
                    else if (e.Kind == SymbolKind.UserCnstSymb)
                    {
                        cnstSymb = (UserCnstSymb)e;
                        var enm = Factory.Instance.MkEnum(span);
                        if (cnstSymb.IsTypeConstant)
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(dstSpace.FullName + "." + GetName(e), span));
                        }
                        else
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        }

                        dtUnn = Factory.Instance.AddUnnCmp(dtUnn, enm);
                    }
                    else if (e.Kind == SymbolKind.UserSortSymb)
                    {
                        dtUnn = Factory.Instance.AddUnnCmp(dtUnn, Factory.Instance.MkId(dstSpace.FullName + "." + GetName(e), span));
                    }
                    else
                    {
                        dtUnn = Factory.Instance.AddUnnCmp(dtUnn, Factory.Instance.MkId(GetName(e), span));
                    }
                }

                foreach (var e in sAt.CanonicalForm[0].NonRangeMembers)
                {
                    if (e.Kind == SymbolKind.BaseCnstSymb)
                    {
                        var enm = Factory.Instance.MkEnum(span);
                        enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        atUnn = Factory.Instance.AddUnnCmp(atUnn, enm);
                    }
                    else if (e.Kind == SymbolKind.UserCnstSymb)
                    {
                        cnstSymb = (UserCnstSymb)e;
                        var enm = Factory.Instance.MkEnum(span);
                        if (cnstSymb.UserCnstKind == UserCnstSymbKind.Derived || cnstSymb.IsTypeConstant)
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(dstSpace.FullName + "." + GetName(e), span));
                        }
                        else
                        {
                            enm = Factory.Instance.AddElement(enm, Factory.Instance.MkId(GetName(e), span));
                        }

                        atUnn = Factory.Instance.AddUnnCmp(atUnn, enm);
                    }
                    else if (e.Kind == SymbolKind.UserSortSymb)
                    {
                        atUnn = Factory.Instance.AddUnnCmp(atUnn, Factory.Instance.MkId(dstSpace.FullName + "." + GetName(e), span));
                    }
                    else
                    {
                        atUnn = Factory.Instance.AddUnnCmp(atUnn, Factory.Instance.MkId(GetName(e), span));
                    }
                }
            }

            constantType = new UnnSymb(dstSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameConstant, ctUnn, span), false);
            dataType = new UnnSymb(dstSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameData, dtUnn, span), false);
            anyType = new UnnSymb(dstSpace, Factory.Instance.MkUnnDecl(ASTSchema.Instance.TypeNameAny, atUnn, span), false);

            var flags = new List<Flag>();
            var result = dstSpace.TryAddSymbol(
                    new UserCnstSymb(dstSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameAny), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) &&
                dstSpace.TryAddSymbol(
                    new UserCnstSymb(dstSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameData), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags) &&
                dstSpace.TryAddSymbol(
                    new UserCnstSymb(dstSpace, Factory.Instance.MkId(string.Format("#{0}", ASTSchema.Instance.TypeNameConstant), span), UserCnstSymbKind.New, true),
                    IncSymbolCount,
                    flags);
            Contract.Assert(result);
        }

        /// <summary>
        /// Gets the name of this symbol from the user's perspective.
        /// </summary>
        private string GetName(Symbol s)
        {
            switch (s.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                    var bcnst = (BaseCnstSymb)s;
                    switch (bcnst.CnstKind)
                    {
                        case CnstKind.Numeric:
                            return bcnst.Raw.ToString();
                        case CnstKind.String:
                            return string.Format("\"{0}\"", bcnst.Raw);
                        default:
                            throw new NotImplementedException();
                    }
                case SymbolKind.BaseOpSymb:
                    var bop = (BaseOpSymb)s;
                    if (bop.OpKind is OpKind)
                    {
                        OpStyleKind os;
                        return ASTSchema.Instance.ToString((OpKind)bop.OpKind, out os);
                    }
                    else if (bop.OpKind is RelKind)
                    {
                        return ASTSchema.Instance.ToString((RelKind)bop.OpKind);
                    }
                    else if (bop.OpKind is ReservedOpKind)
                    {
                        throw new InvalidOperationException();
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                case SymbolKind.BaseSortSymb:
                    string name;
                    ASTSchema.Instance.TryGetSortName(((BaseSortSymb)s).SortKind, out name);
                    return name;
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                case SymbolKind.UnnSymb:
                case SymbolKind.UserCnstSymb:
                    return ((UserSymbol)s).FullName;
                case SymbolKind.UserSortSymb:
                    return ((UserSortSymb)s).DataSymbol.FullName;
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Tries to get a descendant namespace with the same name as forgn (under an optional renaming).
        /// Creates all necessary subnamespaces in the process.
        /// </summary>        
        private bool GetNamespace(Namespace forgn, List<Flag> flags, out Namespace result, string renaming = null)
        {
            var path = new string[(string.IsNullOrEmpty(renaming) ? 0 : 1) + forgn.Depth];
            var crnt = forgn;
            var i = path.Length - 1;
            while (i >= 0)
            {
                path[i--] = crnt.Name;
                crnt = crnt.Parent;
            }

            if (!string.IsNullOrEmpty(renaming))
            {
                path[0] = renaming;
            }

            crnt = Root;
            for (i = 0; i < path.Length; ++i)
            {
                if (!crnt.TryAddNamespace(path[i], ModuleData.Source, out result, flags))
                {
                    result = null;
                    return false;
                }

                crnt = result;
            }

            result = crnt;
            return true;
        }

        private bool IsCoercibleLocked(Coercion coercion)
        {
            Coercion cout;
            if (coercibility.Contains(coercion, out cout))
            {
                //// If this test is already being performed, then
                //// treat it as successful (i.e. cout.IsCoercible == LiftedBool.Unknown).
                return cout.IsCoercible != LiftedBool.False;
            }

            coercibility.Add(coercion);

            //// Step 1. Need to check that From is meaningful and that there is
            //// a corresponding symbol satisfying the renaming From -> To.
            Symbol toSort;
            if (!coercion.TryGetToSymbol(out toSort))
            {
                coercion.IsCoercible = false;
                return false;
            }
            else if (coercion.Symbol == toSort)
            {
                coercion.IsCoercible = true;
                return true;
            }

            //// Step 2. Check that every argument in the original function can be coerced.
            //// Store pending coercions that still need to be validated 
            var srcSymb = coercion.UserSymbol;
            var dstSymb = Coercion.ToUserSymbol(toSort);
            var pending = new Set<Coercion>(Coercion.Compare);
            Coercion pc;
            Symbol toArgSort;

            for (int i = 0; i < srcSymb.Arity; ++i)
            {
                foreach (var r in srcSymb.CanonicalForm[i].RangeMembers)
                {
                    if (!dstSymb.CanonicalForm[i].AcceptsConstants(r.Key, r.Value))
                    {
                        coercion.IsCoercible = false;
                        return false;
                    }
                }

                foreach (var e in srcSymb.CanonicalForm[i].NonRangeMembers)
                {
                    switch (e.Kind)
                    {
                        case SymbolKind.BaseCnstSymb:
                            if (!dstSymb.CanonicalForm[i].AcceptsConstant(e))
                            {
                                coercion.IsCoercible = false;
                                return false;
                            }

                            break;
                        case SymbolKind.UserCnstSymb:
                            if (e.IsDerivedConstant || ((UserCnstSymb)e).IsTypeConstant)
                            {
                                pc = new Coercion(e, coercion.From, coercion.To);
                                if (!pc.TryGetToSymbol(out toArgSort) ||
                                    !dstSymb.CanonicalForm[i].Contains(toArgSort))
                                {
                                    coercion.IsCoercible = false;
                                    return false;
                                }
                            }
                            else if (!dstSymb.CanonicalForm[i].AcceptsConstant(e))
                            {
                                coercion.IsCoercible = false;
                                return false;
                            }

                            break;
                        case SymbolKind.BaseSortSymb:
                            if (!dstSymb.CanonicalForm[i].AcceptsConstants((BaseSortSymb)e))
                            {
                                coercion.IsCoercible = false;
                                return false;
                            }

                            break;
                        case SymbolKind.UserSortSymb:
                            pc = new Coercion((UserSortSymb)e, coercion.From, coercion.To);
                            if (!pc.TryGetToSymbol(out toArgSort) ||
                                !dstSymb.CanonicalForm[i].Contains(toArgSort))
                            {
                                coercion.IsCoercible = false;
                                return false;
                            }

                            pending.Add(pc);
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }
            
            //// Step 3. Recursively validate pending coercions.
            foreach (var pend in pending)
            {
                if (!IsCoercibleLocked(pend))
                {
                    coercion.IsCoercible = false;
                    break;
                }
            }

            if (coercion.IsCoercible == false)
            {
                return false;
            }
            else
            {
                coercion.IsCoercible = true;
                return true;
            }
        }

        private class RelabelData
        {
            public string From
            {
                get;
                private set;
            }

            public string To
            {
                get;
                private set;
            }

            public Namespace Target
            {
                get;
                private set;
            }

            public RelabelData(string from, string to, Namespace target)
            {
                Contract.Assert(from != null && to != null && target != null);
                From = from;
                To = to;
                Target = target;
            }

            public static int Compare(RelabelData d1, RelabelData d2)
            {
                var cmp = string.Compare(d1.From, d2.From);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = string.Compare(d1.To, d2.To);
                if (cmp != 0)
                {
                    return cmp;
                }

                return Namespace.Compare(d1.Target, d2.Target);
            }
        }

        private class Coercion
        {
            private LiftedBool isCoercible = LiftedBool.Unknown;

            public Symbol Symbol
            {
                get;
                private set;
            }

            public Namespace From
            {
                get;
                private set;
            }

            public Namespace To
            {
                get;
                private set;
            }

            public UserSymbol UserSymbol
            {
                get { return ToUserSymbol(Symbol); }
            }

            public LiftedBool IsCoercible
            {
                get
                {
                    return isCoercible;
                }

                set
                {
                    Contract.Assert(isCoercible == LiftedBool.Unknown);
                    isCoercible = value;
                }
            }

            public Coercion(Symbol symbol, Namespace from, Namespace to)
            {
                Contract.Requires(symbol != null && from != null && to != null);
                Contract.Requires(symbol.Kind == SymbolKind.UserSortSymb || symbol.Kind == SymbolKind.UserCnstSymb);
                Contract.Requires(symbol.Kind != SymbolKind.UserCnstSymb || symbol.IsDerivedConstant || ((UserCnstSymb)symbol).IsTypeConstant);

                Symbol = symbol;
                From = from;
                To = to;
            }

            public static int Compare(Coercion c1, Coercion c2)
            {
                Contract.Requires(c1 != null && c2 != null);
                var cmp = Terms.Symbol.Compare(c1.Symbol, c2.Symbol);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = Namespace.Compare(c1.From, c2.From);
                if (cmp != 0)
                {
                    return cmp;
                }

                return Namespace.Compare(c1.To, c2.To);
            }

            public static UserSymbol ToUserSymbol(Symbol s)
            {
                switch (s.Kind)
                {
                    case SymbolKind.UserCnstSymb:
                        return ((UserSymbol)s);
                    case SymbolKind.UserSortSymb:
                        return ((UserSortSymb)s).DataSymbol;
                    default:
                        throw new Impossible();
                }
            }

            /// <summary>
            /// If From.p.f exists and to.p.f exists and is compatible with From.p.f, then
            /// the sort of to.p.f is returned.
            /// </summary>
            public bool TryGetToSymbol(out Symbol toSymb)
            {
                toSymb = null;
                string[] suffix;
                var uss = UserSymbol;
                if ((suffix = uss.Namespace.Split(From)) == null)
                {
                    return false;
                }

                var dstNs = To;
                for (int i = 0; i < suffix.Length; ++i)
                {
                    if (!dstNs.TryGetChild(suffix[i], out dstNs))
                    {
                        return false;
                    }
                }

                UserSymbol dstSymb;
                if (!dstNs.TryGetSymbol(uss.Name, out dstSymb) || dstSymb.Arity != uss.Arity)
                {
                    return false;
                }


                if (dstSymb.Kind == SymbolKind.UserCnstSymb)
                {
                    var cnstSymb = (UserCnstSymb)dstSymb;
                    if (cnstSymb.IsDerivedConstant || cnstSymb.IsTypeConstant)
                    {
                        toSymb = dstSymb;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    toSymb = dstSymb.Kind == SymbolKind.ConSymb ? ((ConSymb)dstSymb).SortSymbol : ((MapSymb)dstSymb).SortSymbol;
                }

                return true;
            }
        }

        private class ProductivityGraph
        {
            private SymbolTable table;
            private UserCnstSymb cnstSymb;
            private Map<Symbol, ProductivityNode> nodes = new Map<Symbol, ProductivityNode>(Symbol.Compare);

            public ProductivityGraph(SymbolTable table, UserCnstSymb cnstSymb)
            {
                Contract.Requires(table != null && cnstSymb != null);
                this.table = table;
                this.cnstSymb = cnstSymb;
            }

            public bool CheckProductivity(List<Flag> flags, CancellationToken cancel)
            {
                //// Step 1. Construct a dependency graph
                var cnstNode = new ProductivityNode(cnstSymb);
                nodes.Add(cnstSymb, cnstNode);
                foreach (var s in table.Root.DescendantSymbols)
                {
                    if (s.Kind != SymbolKind.MapSymb && s.Kind != SymbolKind.ConSymb)
                    {
                        continue;
                    }

                    var fnode = GetFuncNode(s);
                    for (int i = 0; i < s.Arity; ++i)
                    {
                        var argNode = new ProductivityNode(s, i);
                        argNode.AddMayProduce(fnode);

                        if (s.CanonicalForm[i].ContainsConstants)
                        {
                            cnstNode.AddMayProduce(argNode);
                        }
                        else
                        {
                            foreach (var g in s.CanonicalForm[i].UserSorts)
                            {
                                GetFuncNode(g.DataSymbol).AddMayProduce(argNode);
                            }
                        }
                    }
                }

                //// Step 2. Mark all functions reachable from the constant node. 
                //// These are the ones that can be produced.
                var stack = new Stack<ProductivityNode>();
                stack.Push(cnstNode);
                while (stack.Count > 0)
                {
                    var top = stack.Pop();
                    foreach (var n in top.Produces)
                    {
                        if (n.FoundProduction())
                        {
                            stack.Push(n);
                        }
                    }
                }

                //// Step 3. All unmarked nodes are errors.
                var result = true;
                foreach (var kv in nodes)
                {
                    if (!kv.Value.IsProductive)
                    {
                        var symb = (UserSymbol)kv.Key;
                        var flag = new Flag(
                                        SeverityKind.Error,
                                        symb.Definitions.First<AST<Node>>().Node,
                                        Constants.BadTypeDecl.ToString(symb.FullName, "it does not accept any finite terms"),
                                        Constants.BadTypeDecl.Code);
                        flags.Add(flag);
                        result = false;
                    }
                }

                return result;
            }

            private ProductivityNode GetFuncNode(Symbol s)
            {
                Contract.Requires(s != null);
                Contract.Requires(s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb);

                ProductivityNode n;
                if (!nodes.TryFindValue(s, out n))
                {
                    n = new ProductivityNode(s);
                    nodes.Add(s, n);
                }

                return n;
            }
              
            private class ProductivityNode
            {
                private LinkedList<ProductivityNode> produces = new LinkedList<ProductivityNode>();

                /// <summary>
                /// This is the number of nodes that must be productive 
                /// in order for this node to be productive.
                /// </summary>
                private int nRequires;

                /// <summary>
                /// These are the nodes that may be productive if this node is productive.
                /// </summary>
                public IEnumerable<ProductivityNode> Produces
                {
                    get { return produces; }
                }

                /// <summary>
                /// True if this node is known to be productive.
                /// </summary>
                public bool IsProductive
                {
                    get { return nRequires <= 0; }
                }
              
                public Symbol Symbol
                {
                    get;
                    private set;
                }

                public int Index
                {
                    get;
                    private set;
                }
               
                public ProductivityNode(Symbol s, int index = -1)
                {
                    Symbol = s;
                    Index = index;
                    if (s.Kind == SymbolKind.UserCnstSymb)
                    {
                        nRequires = 0;
                    }
                    else
                    {
                        nRequires = index < 0 ? s.Arity : 1;
                    }                
                }

                public static int Compare(ProductivityNode n1, ProductivityNode n2)
                {
                    if (n1.Symbol.Id != n2.Symbol.Id)
                    {
                        return n1.Symbol.Id - n2.Symbol.Id;
                    }

                    return n1.Index - n2.Index;
                }

                /// <summary>
                /// Indicates that this node may be used in the production of n.
                /// </summary>
                public void AddMayProduce(ProductivityNode n)
                {
                    produces.AddLast(n);
                }

                /// <summary>
                /// Indicates that some dependency of this node is known to be productive. Returns
                /// true if enough dependencies are known to be productive that this node must also
                /// be productive.
                /// </summary>
                public bool FoundProduction()
                {
                    --nRequires;
                    return nRequires == 0;
                }
            }
        }

        /// <summary>
        /// A node to represent a data type. If arg != SymbolDefinition, then this
        /// node stands for the implicit union type at position arg of an F constructor.
        /// Otherwise, the nodes stands for the definition of Symbol.
        /// </summary>
        private class UserSortDepNode
        {
            public const int SymbolDefinition = -1;

            public int Arg
            {
                get;
                private set;
            }

            public UserSortSymb Symbol
            {
                get;
                private set;
            }

            public UserSortDepNode(UserSortSymb symbol, int arg = SymbolDefinition)
            {
                Arg = arg;
                Symbol = symbol;
            }

            public static int Compare(UserSortDepNode d1, UserSortDepNode d2)
            {
                var cmp = Terms.Symbol.Compare(d1.Symbol, d2.Symbol);
                if (cmp != 0)
                {
                    return cmp;
                }

                return d1.Arg - d2.Arg;
            }
        }
    }
}
