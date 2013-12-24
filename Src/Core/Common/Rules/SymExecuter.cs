namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Compiler;
    using Extras;
    using Solver;
    using Terms;

    internal class SymExecuter
    {
        /// <summary>
        /// The symbolic least fixpoint.
        /// </summary>
        private Map<Term, SymElement> symbLfp = new Map<Term, SymElement>(Term.Compare);

        private Solver solver;

        public SymExecuter(Solver solver)
        {
            Contract.Requires(solver != null);
            this.solver = solver;
            
            Set<Term> facts;
            Map<UserCnstSymb, Term> aliasMap;
            solver.PartialModel.ConvertSymbCnstsToVars(out facts, out aliasMap);
            solver.TypeEmbedder.Debug_PrintAtomsToEmbeddingsMap();

            foreach (var kv in aliasMap)
            {
                var emb = solver.TypeEmbedder.ChooseRepresentation(solver.PartialModel.GetSymbCnstType(kv.Key));

                Console.WriteLine("{0} : {1} -> {2}", 
                    kv.Key.FullName, 
                    solver.PartialModel.Index.MkDataWidenedType(solver.PartialModel.GetSymbCnstType(kv.Key)).Debug_GetSmallTermString(),
                    emb.Type.Debug_GetSmallTermString());
            }
        }
    }
}
