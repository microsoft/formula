namespace Microsoft.Formula.Common.Rules
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
    using Extras;
    using Terms;

    /// <summary>
    /// An instance of matcher pre-allocates memory for bindings, and saves the bindings until the next match.
    /// </summary>
    internal class Matcher
    {
        private Map<Term, Term> bindings = new Map<Term, Term>(Term.Compare);
        private LinkedList<Term> bindingVars = new LinkedList<Term>();

        public Term Pattern
        {
            get;
            private set;
        }

        public IEnumerable<KeyValuePair<Term, Term>> CurrentBindings
        {
            get { return bindings; }
        }

        public Matcher(Term pattern)
        {
            Contract.Requires(pattern != null && pattern.Groundness != Groundness.Type);
            Pattern = pattern;
            pattern.Visit(
                x => x.Groundness == Groundness.Variable ? x.Args : null,
                x =>
                {
                    if (x.Symbol.IsVariable)
                    {
                        bindings[x] = null;
                    }
                });

            foreach (var v in bindings.Keys)
            {
                bindingVars.AddLast(v);
            }
        }

        /// <summary>
        /// Tries to match t with pattern and returns true if successfull. CurrentBindings
        /// holds the most recent variable bindings, which may be null if the match failed.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool TryMatch(Term t)
        {
            Contract.Requires(t != null && t.Groundness == Groundness.Ground);
            Contract.Requires(t.Owner == Pattern.Owner);
            foreach (var v in bindingVars)
            {
                bindings[v] = null;
            }

            var success = new SuccessToken();
            Pattern.Compute<Unit>(t, ExpandMatch, (x, y, ch, s) => default(Unit), success);
            return success.Result;
        }

        private Tuple<IEnumerable<Term>, IEnumerable<Term>> ExpandMatch(Term px, Term ty, SuccessToken success)
        {
            if (px.Groundness == Groundness.Ground)
            {
                if (px != ty)
                {
                    success.Failed();
                }

                return null;
            }
            else if (px.Symbol.IsVariable)
            {
                var crnt = bindings[px];
                if (crnt == null)
                {
                    bindings[px] = ty;
                }
                else if (crnt != ty)
                {
                    success.Failed();
                }

                return null;
            }
            else
            {
                Contract.Assert(px.Symbol.IsDataConstructor);
                if (px.Symbol != ty.Symbol)
                {
                    success.Failed();
                    return null;
                }

                return new Tuple<IEnumerable<Term>, IEnumerable<Term>>(px.Args, ty.Args);
            }
        }
    }
}
