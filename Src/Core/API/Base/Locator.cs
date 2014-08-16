namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Text;
    using System.Threading;

    using Common;
    using Common.Terms;
    using Compiler;
    using Nodes;

    /// <summary>
    /// A locator assigns line numbers to a term and its subterms, including terms that were derived from rules.
    /// </summary>
    public abstract class Locator
    {
        private static readonly Locator[] EmptyArgs = new Locator[0];

        private static readonly ProgramName unknownProgram =
            new ProgramName(string.Format("{0}/unknown", ProgramName.EnvironmentScheme));

        internal static ProgramName UnknownProgram
        {
            get { return unknownProgram; }
        }

        /// <summary>
        /// The arity of the term corresponding to this locator.
        /// </summary>
        public abstract int Arity
        {
            get;
        }

        /// <summary>
        /// Gets the locators of this locators subterms
        /// </summary>
        public abstract Locator this[int index]
        {
            get;
        }

        /// <summary>
        /// A span that is related to this term.
        /// </summary>
        public abstract Span Span
        {
            get;
        }

        /// <summary>
        /// The program where the related span occurs.
        /// </summary>
        public abstract ProgramName Program
        {
            get;
        }

        public void Debug_Print(int maxDepth)
        {
            Debug_Print(0, maxDepth);
        }

        /// <summary>
        /// Chooses the lexicographically smallest program name, and then produces a span containing
        /// all the spans occuring in that program name. Returns null if there are no locators.
        /// </summary>
        internal static Tuple<Span, ProgramName> Widen(IEnumerable<Locator> locators)
        {
            if (locators == null || Common.Extras.EnumerableMethods.IsEmpty(locators))
            {
                return null;
            }

            var widened = default(Span);
            ProgramName smallest = null;
            foreach (var l in locators)
            {
                if (smallest == null || string.Compare(l.Program.ToString(), smallest.ToString()) < 0)
                {
                    smallest = l.Program;
                    widened = l.Span;
                }
            }

            foreach (var l in locators)
            {
                if (l.Program.ToString() != smallest.ToString())
                {
                    continue;
                }

                widened = new Span(
                    Math.Min(widened.StartLine, l.Span.StartLine),
                    Math.Min(widened.StartCol, l.Span.StartCol),
                    Math.Max(widened.EndLine, l.Span.EndLine),
                    Math.Max(widened.EndCol, l.Span.EndCol));
            }

            return new Tuple<Span, ProgramName>(widened, smallest);
        }

        /// <summary>
        /// Returns up-to maxPermutations. Each permutation is stored in a fresh array.
        /// </summary>
        internal static IEnumerable<Locator[]> MkPermutations(IEnumerable<LinkedList<Locator>> locators, int maxPermutations)
        {
            if (locators == null || maxPermutations <= 0)
            {
                yield break;
            }

            var size = 0;
            foreach (var l in locators)
            {
                ++size;
                if (l.Count == 0)
                {
                    yield break;
                }
            }

            //// Simple algorithm returns more diverse locations later in the permutation.
            //// TODO: Generalize enumeration scheme to select maxPermutations with less bias. 
            var enums = new IEnumerator<Locator>[size];
            int i = 0;
            foreach (var l in locators)
            {
                enums[i] = l.GetEnumerator();
                enums[i].MoveNext();
                ++i;
            }

            //// Special case for the first perumtation.
            var count = 1;
            yield return MkArray(enums);

            //// Generate the remaining permutations.
            i = size - 1; 
            while (i >= 0)
            {
                if (enums[i].MoveNext())
                {
                    while (i < size - 1)
                    {
                        ++i;
                        enums[i].Reset();
                        enums[i].MoveNext();
                    }

                    ++count;
                    if (count <= maxPermutations)
                    {
                        yield return MkArray(enums);
                    }
                    else
                    {
                        yield break;
                    }
                }
                else
                {
                    --i;
                }
            }
        }

        private static Locator[] MkArray(IEnumerator<Locator>[] enums)
        {
            var array = new Locator[enums.Length];
            for (int i = 0; i < enums.Length; ++i)
            {
                array[i] = enums[i].Current;
            }

            return array;
        }

        private void Debug_Print(int depth, int maxDepth)
        {
            if (depth >= maxDepth)
            {
                return;
            }

            var indent = (depth == 0) ? string.Empty : new string(' ', 3 * depth);
            Console.WriteLine(
                "{0}{1} ({2}, {3})", 
                indent,
                Program.ToString(),
                Span.StartLine,
                Span.StartCol);

            for (int i = 0; i < Arity; ++i)
            {
                this[i].Debug_Print(depth + 1, maxDepth);
            }
        }
    }
}
