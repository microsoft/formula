namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Common.Extras;
    using Solver;

    using Z3Expr = Microsoft.Z3.Expr;

    /// <summary>
    /// And index of encodings from Formula to Z3 terms.
    /// </summary>
    internal partial class TermEncIndex
    {
        private Map<Term, Z3Expr> encodings = new Map<Term, Z3Expr>(Term.Compare); 

        public Solver Solver
        {
            get;
            private set;
        }

        public TermEncIndex(Solver solver)
        {
            Contract.Requires(solver != null);
            Solver = solver;
        }

        public Z3Expr GetVarEnc(Term v, Term type)
        {
            Contract.Requires(v != null && type != null && v.Symbol.IsVariable);
            Z3Expr varEnc;
            if (encodings.TryFindValue(v, out varEnc))
            {
                return varEnc;
            }

            var typEmb = Solver.TypeEmbedder.ChooseRepresentation(type);
            varEnc = Solver.Context.MkFreshConst(((UserCnstSymb)v.Symbol).FullName, typEmb.Representation);
            encodings.Add(v, varEnc);
            return varEnc;
        }

        /// <summary>
        /// Returns an encoding of this term, possibly after applying some normalizing rewrites. 
        /// </summary>
        public Z3Expr GetTerm(Term t, out Term normalizedTerm)
        {
            Contract.Requires(t != null);
            normalizedTerm = Normalize(t);
            Z3Expr enc, encp;
            if (encodings.TryFindValue(normalizedTerm, out enc))
            {
                return enc;
            }

            int i;
            bool wasAdded;
            ITypeEmbedding typEmb;
            ConstructorEmbedding conEmb;
            return normalizedTerm.Compute<Z3Expr>(
                (x, s) =>
                {
                    if (encodings.ContainsKey(x) || x.Groundness == Groundness.Ground)
                    {
                        return null;
                    }
                    else
                    {
                        return x.Args;
                    }
                },
                (x, ch, s) =>
                {
                    if (encodings.TryFindValue(x, out encp))
                    {
                        return encp;
                    }
                    else if (x.Groundness == Groundness.Ground)
                    {
                        typEmb = Solver.TypeEmbedder.ChooseRepresentation(x);
                        encp = Solver.TypeEmbedder.MkGround(x, typEmb);
                        encodings.Add(x, encp);
                        return encp;
                    }

                    //// x must be non-ground. Because variables are already encoded, then x should not be a variable
                    Contract.Assert(!x.Symbol.IsVariable);
                    if (x.Symbol.IsDataConstructor)
                    {
                        if (x.Symbol.Kind == SymbolKind.ConSymb)
                        {
                            conEmb = (ConstructorEmbedding)Solver.TypeEmbedder.GetEmbedding(
                                        Solver.Index.MkApply(((ConSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                        }
                        else
                        {
                            conEmb = (ConstructorEmbedding)Solver.TypeEmbedder.GetEmbedding(
                                        Solver.Index.MkApply(((MapSymb)x.Symbol).SortSymbol, TermIndex.EmptyArgs, out wasAdded));
                        }

                        i = 0;
                        var args = new Z3Expr[x.Symbol.Arity];
                        foreach (var a in ch)
                        {
                            typEmb = Solver.TypeEmbedder.GetEmbedding(conEmb.Z3Constructor.ConstructorDecl.Domain[i]);
                            args[i++] = typEmb.MkCoercion(a);
                        }

                        encp = conEmb.MkGround(x.Symbol, args);
                        encodings.Add(x, encp);
                        return encp;
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                });
        }

        public void Debug_Print()
        {
            foreach (var kv in encodings)
            {
                Console.WriteLine("Entry: {0}", kv.Key.Debug_GetSmallTermString());
                Console.WriteLine("   Representation: {0}", Solver.TypeEmbedder.GetEmbedding(kv.Value.Sort).Type.Debug_GetSmallTermString());
                Console.WriteLine("   Encoding: {0}", kv.Value);
            }
        }

        private Term Normalize(Term t)
        {
            return t;
        }
    }
}
