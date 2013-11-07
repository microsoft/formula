namespace Microsoft.Formula.Common.Composites
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using Compiler;
    using Terms;
  
    /// <summary>
    /// A thread-safe map for storing the intermediate results of a step.
    /// Models of the same domain share the same index.
    /// </summary>
    internal class StepResultMap : IDisposable
    {
        private SpinLock resultsLock = new SpinLock();

        private Map<string, Tuple<TermIndex, Mutex>> indices =
            new Map<string, Tuple<TermIndex, Mutex>>(string.Compare);

        private Map<string, FactSet> results =
            new Map<string, FactSet>(string.Compare);

        public FactSet this[string index]
        {
            get
            {
                bool gotLock = false;
                try
                {
                    resultsLock.Enter(ref gotLock);
                    return results[index];
                }
                finally
                {
                    if (gotLock)
                    {
                        resultsLock.Exit();
                    }
                }
            }
        }
        
        public StepResultMap(CoreTSystem tsys)
        {
            Contract.Requires(tsys != null);
            foreach (var kv in tsys.ModelVariables)
            {
                indices.Add(
                    kv.Key, 
                    new Tuple<TermIndex, Mutex>(
                        new TermIndex(((ModuleData)(((Domain)kv.Value.Item2.AST.Node).CompilerData)).SymbolTable), 
                        new Mutex(false)));
            }            
        }

        public void SetResult(string modelVar, FactSet facts)
        {
            Contract.Requires(facts != null);
            bool gotLock = false;
            try
            {
                resultsLock.Enter(ref gotLock);
                results.Add(modelVar, facts);
            }
            finally
            {
                if (gotLock)
                {
                    resultsLock.Exit();
                }
            }
        }

        public void SetResult(string modelVar, Namespace projectionSpace, IEnumerable<Term> terms)
        {
            Contract.Requires(projectionSpace != null && terms != null);

            var indData = indices[modelVar];
            indData.Item2.WaitOne();
            var index = indData.Item1;

            Symbol s;
            UserSymbol us;
            Namespace ns;
            var projection = new Set<Term>(Term.Compare);
            foreach (var t in terms)
            {
                s = t.Symbol;
                if (!s.IsDataConstructor)
                {
                    continue;
                }

                us = (UserSymbol)s;
                if (us.Namespace.Parent == null || us.IsAutoGen || (s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew))
                {
                    continue;
                }

                ns = us.Namespace;
                while (ns.Parent.Parent != null)
                {
                    ns = ns.Parent;
                }

                if (ns != projectionSpace)
                {
                    continue;
                }

                projection.Add(index.MkClone(t, null, null, true));
            }
           
            indData.Item2.ReleaseMutex();
            SetResult(modelVar, new FactSet(index, projection));
        }

        public void Dispose()
        {
            foreach (var kv in indices)
            {
                kv.Value.Item2.Dispose();
            }
        }
    }
}
