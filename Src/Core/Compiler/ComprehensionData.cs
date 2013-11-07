namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.ASTQueries;
    using API.Plugins;
    using API.Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Common.Rules;

    internal class ComprehensionData
    {
        private Term internalRep = null;
        private ActionSet actionSet = null;
        private Map<Term, Node> readVars = new Map<Term, Node>(Term.Compare);

        /// <summary>
        /// A term representing the disjunction of bodies on the right-hand side of the comprehension.
        /// </summary>
        public Term Representation
        {
            get
            {
                Contract.Assert(internalRep != null);
                return internalRep;
            }

            set
            {
                Contract.Assert(internalRep == null);
                internalRep = value;
            }
        }

        public Node Node
        {
            get;
            private set;
        }

        public ConstraintSystem Owner
        {
            get;
            private set;
        }

        public int Depth
        {
            get;
            private set;
        }

        public Map<Term, Node> ReadVars
        {
            get { return readVars; }
        }

        public ComprehensionData(Node node, ConstraintSystem owner, int depth)
        {
            Contract.Requires(node != null && owner != null && depth > 0);
            Contract.Requires(node.NodeKind == NodeKind.Compr);

            Node = node;
            Owner = owner;
            Depth = depth;
        }

        public bool Validate(List<Flag> flags, CancellationToken cancel)
        {
            actionSet = new ActionSet(this);
            return actionSet.Validate(flags, cancel);
        }

        public bool Compile(RuleTable rules, List<Flag> flags, CancellationToken cancel)
        {
            return actionSet.Compile(rules, flags, cancel);
        }

        public int GetNextVarId(FreshVarKind kind)
        {
            Contract.Assert(actionSet != null);
            return actionSet.GetNextVarId(kind);
        }

        /// <summary>
        /// Returns true if variable v is defined in the parent scope.
        /// </summary>
        public bool IsParentVar(Term v)
        {
            Contract.Requires(v != null && v.Symbol.IsVariable);
            Term type;
            var scope = Owner;
            while (scope != null)
            {
                if (scope.TryGetType(v, out type))
                {
                    return true;
                }

                scope = scope.Comprehension == null ? null : scope.Comprehension.Owner;
            }

            return false;
        }

        /// <summary>
        /// This method records those variables read by the comprehension
        /// that are also defined in a parent scope.
        /// 
        /// Returns true if v is not already known to be read by this comprehension
        /// and v is defined in a parent scope. Also provides the type of v as judged
        /// by the deepest parent scope.
        /// </summary>
        public bool RecordRead(Term v, Node n, out Term type)
        {
            Contract.Requires(v != null && v.Symbol.IsVariable);
            if (readVars.ContainsKey(v))
            {
                type = null;
                return false;
            }
            
            var scope = Owner;
            while (scope != null)
            {
                if (scope.TryGetType(v, out type))
                {
                    readVars.Add(v, n);
                    return true;
                }

                scope = scope.Comprehension == null ? null : scope.Comprehension.Owner;
            }

            type = null;
            return false;
        }

        public void Debug_Print()
        {
            Console.WriteLine("**** Comprehension at {0}, {1}", Node.Span.StartLine, Node.Span.StartCol);
            foreach (var kv in readVars)
            {
                Console.WriteLine(
                    "Reads {0} at {1}, {2}",
                    kv.Key.Debug_GetSmallTermString(),
                    kv.Value.Span.StartLine,
                    kv.Value.Span.StartCol);
            }
        }
    }
}
