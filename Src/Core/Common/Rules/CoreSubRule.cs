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

    internal class CoreSubRule : CoreRule
    {
        public SubtermMatcher Matcher
        {
            get;
            private set;
        }

        public override RuleKind Kind
        {
            get { return RuleKind.Sub; }
        }

        /// <summary>
        /// Creates a placeholder rule for the matcher of the form:
        /// f(x_1,...,x_n) :- y is T. 
        /// 
        /// where x_1,...,x_n will be substituted by subterms satisfying the matcher,
        /// and T is the union type of values that can trigger the matcher.
        /// </summary>
        public CoreSubRule(int ruleId, Term head, Term bindVar, SubtermMatcher matcher)
            : base(ruleId, head, new FindData(bindVar, bindVar, matcher.Trigger))
        {
            Contract.Requires(matcher != null && matcher.IsTriggerable);
            Matcher = matcher;
        }

        public override CoreRule OptInlinePartialRule(CoreRule eliminator, out bool succeeded)
        {
            succeeded = false;
            return this;
        }

        public override void Debug_PrintRule()
        {
            Console.WriteLine("ID: {0}, Stratum: {1}", RuleId, stratum < 0 ? "?" : stratum.ToString());
            Console.WriteLine(Head.Debug_GetSmallTermString());
            Console.WriteLine("  :-");
            Console.WriteLine(
                "    {0}[{1}: {2}]",
                Find1.Binding.Debug_GetSmallTermString(),
                Find1.Pattern.Debug_GetSmallTermString(),
                Find1.Type.Debug_GetSmallTermString());

            int i = 0;
            foreach (var pat in Matcher.Pattern)
            {
                Console.WriteLine(
                    "    {1} [= {0}, {1} : {2}{3}", 
                    i == 0 ? Find1.Binding.Debug_GetSmallTermString() : Head.Args[i - 1].Debug_GetSmallTermString(),
                    Head.Args[i].Debug_GetSmallTermString(),
                    pat.Debug_GetSmallTermString(),
                    i < Matcher.NPatterns - 1 ? "," : string.Empty);
                ++i;
            }

            Console.WriteLine("    .");
        }

        public override CoreRule Clone(int ruleId, Predicate<Symbol> isCompr, TermIndex index, Map<Term, Term> bindingReificationCache, Map<UserSymbol, UserSymbol> symbolTransfer, string renaming)
        {
            Contract.Assert(isCompr == null && index != null && bindingReificationCache == null && string.IsNullOrEmpty(renaming));

            bool wasAdded;
            var newHeadArgs = new Term[Head.Symbol.Arity];
            var newHeadCon = symbolTransfer[(UserSymbol)Head.Symbol];
            for (int i = 0; i < newHeadArgs.Length; ++i)
            {
                newHeadArgs[i] = index.MkVar(((UserSymbol)Head.Args[i].Symbol).Name, true, out wasAdded); 
            }

            var newHead = index.MkApply(newHeadCon, newHeadArgs, out wasAdded);
            return new CoreSubRule(
                ruleId,
                newHead,
                index.MkVar(((UserSymbol)Find1.Binding.Symbol).Name, true, out wasAdded),
                Matcher.Clone(index));
        }

        public override void Execute(Term binding, int findNumber, Executer index, bool keepDerivations, Map<Term, Set<Derivation>> pending)
        {
            bool wasAdded;
            Term[] args;
            foreach (var match in Matcher.EnumerateMatches(binding))
            {
                args = new Term[match.Length];
                match.CopyTo(args, 0);
                Pend(
                    keepDerivations,
                    index,
                    pending,
                    Index.MkApply(Head.Symbol, args, out wasAdded),
                    binding,
                    Index.FalseValue);
            }
        }

        public override IEnumerable<Term> GetDirectVarDefs(Term t)
        {
            yield break;
        }
    }
}
