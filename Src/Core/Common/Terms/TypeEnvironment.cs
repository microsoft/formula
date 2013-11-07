namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Threading;

    using API;
    using API.Nodes;

    public sealed class TypeEnvironment
    {
        private object typesLock = new object();

        /// <summary>
        /// Maps a term to a (precanonical, canonical) type pair.
        /// The canonical form is computed on demand.
        /// </summary>
        private Map<Term, MutableTuple<Term, Term>> types =
            new Map<Term, MutableTuple<Term, Term>>(Term.Compare);

        private List<Tuple<Node, int, string, string>> coercionList =
            new List<Tuple<Node, int, string, string>>();

        /// <summary>
        /// List of child environments
        /// </summary>
        private LinkedList<TypeEnvironment> children = 
            new LinkedList<TypeEnvironment>();

        internal TermIndex Index
        {
            get;
            private set;
        }

        public Node Node
        {
            get;
            private set;
        }

        /// <summary>
        /// A set of terms whose types are recorded in this environment.
        /// </summary>
        public IEnumerable<Term> Terms
        {
            get { return types.Keys; }
        }

        /// <summary>
        /// A list of coercions. The ith arg of node is coerced by relabeling.
        /// </summary>
        public IEnumerable<Tuple<Node, int, string, string>> Coercions
        {
            get { return coercionList; }
        }

        /// <summary>
        /// A list of child environments. A child environment may further
        /// constrain the types of variables in this environment, and may
        /// have additional variables that are not visible in this environment.
        /// </summary>
        public IEnumerable<TypeEnvironment> Children
        {
            get
            {
                return children;
            }
        }

        internal TypeEnvironment(Node node, TermIndex index)
        {
            Contract.Requires(node != null && index != null);
            Node = node;
            Index = index;
        }

        /// <summary>
        /// Tries to get the canonical type of a term, if it is in the environment. 
        /// </summary>
        public bool TryGetType(Term term, out Term type)
        {
            if (term.Owner != Index)
            {
                type = null;
                return false;
            }

            lock (typesLock)
            {
                MutableTuple<Term, Term> envTypes;
                if (!types.TryFindValue(term, out envTypes))
                {
                    type = null;
                    return false;
                }
                else if (envTypes.Item2 != null)
                {
                    type = envTypes.Item2;
                    return true;
                }
                else
                {
                    type = Index.MkCanonicalForm(envTypes.Item1);
                    envTypes.Item2 = type;
                    return true;
                }
            }
        }

        internal void AddCoercion(Node app, int index, string srcPrefix, string dstPrefix)
        {
            Contract.Requires(app != null && index >= 0);
            coercionList.Add(new Tuple<Node,int,string,string>(app, index, srcPrefix, dstPrefix));
        }

        /// <summary>
        /// Sets the precanonical type of the term; term should not already be set.
        /// </summary>
        internal void SetType(Term term, Term type)
        {
            Contract.Requires(term != null && type != null);
            Contract.Requires(term.Groundness != Groundness.Type);
            Contract.Requires(type.Groundness != Groundness.Variable);
            Contract.Requires(term.Owner == Index);
            Contract.Requires(type.Owner == Index);

            types.Add(term, new MutableTuple<Term, Term>(type, null));
        }

        /// <summary>
        /// If t: t1 and t: t2 in child environments c1 and c2, then 
        /// t: t1 + t2 in this type environment.
        /// </summary>
        internal void JoinTypes()
        {
            bool wasAdded;
            var unnSymbol = Index.SymbolTable.GetOpSymbol(ReservedOpKind.TypeUnn);
            MutableTuple<Term, Term> envType;
            foreach (var c in children)
            {
                foreach (var kv in c.types)
                {
                    if (!types.TryFindValue(kv.Key, out envType))
                    {
                        types.Add(kv.Key, new MutableTuple<Term, Term>(kv.Value.Item1, null));
                    }
                    else
                    {
                        Contract.Assert(envType.Item2 == null);
                        envType.Item1 = Index.MkApply(unnSymbol, new Term[] { envType.Item1, kv.Value.Item1 }, out wasAdded);
                    }
                }
            }
        }

        /// <summary>
        /// Adds a child environment
        /// </summary>
        internal TypeEnvironment AddChild(Node node)
        {
            var child = new TypeEnvironment(node, Index);
            children.AddLast(child);
            return child;
        }
    }
}
