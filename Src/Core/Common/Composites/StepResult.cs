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
    using Rules;
    using Terms;
  
    internal class StepResult
    {
        private Step step;
        private CoreTSystem tsystem;
        private CancellationToken cancel;
        private StepResultMap resultMap;
        private Map<string, Term> valueParams;

        public StepResultMap Results
        {
            get { return resultMap; }
        }

        /// <summary>
        /// A dummy step that completes only after all the steps of a system are completed.
        /// </summary>
        public StepResult(StepResultMap resultMap)
        {
            Contract.Requires(resultMap != null);
            this.resultMap = resultMap;
        }

        public StepResult(Step step,
                          CoreTSystem tsystem,
                          Map<string, Term> valueParams,
                          StepResultMap resultMap,
                          CancellationToken cancel)
        {
            Contract.Requires(resultMap != null);
            Contract.Requires(valueParams != null);
            Contract.Requires(step != null);
            Contract.Requires(tsystem != null);

            this.step = step;
            this.tsystem = tsystem;
            this.cancel = cancel;
            this.resultMap = resultMap;
            this.valueParams = valueParams;
        }

        public void Start()
        {
            var mod = (ModuleData)((Location)step.Rhs.Module.CompilerData).AST.Node.CompilerData;

            if (mod.Reduced.Node.NodeKind == NodeKind.Model)
            {
                var facts = ((FactSet)mod.FinalOutput).Facts;
                var copy = new Set<Term>(Term.Compare);
                var index = new TermIndex(mod.SymbolTable);
                foreach (var f in facts)
                {
                    copy.Add(index.MkClone(f));
                }

                resultMap.SetResult(step.Lhs.First().Name, new FactSet(index, copy));
            }
            else if (mod.Reduced.Node.NodeKind == NodeKind.Transform)
            {
                var transform = (Transform)mod.Reduced.Node;
                var index = new TermIndex(mod.SymbolTable);
                var copyRules = ((RuleTable)mod.FinalOutput).CloneTransformTable(index);

                var valParams = tsystem.InstantiateValueParams(step, index, valueParams);
                var modParams = tsystem.InstantiateModelParams(step, resultMap);

                var exe = new Executer(copyRules, modParams, valParams, null, false, cancel);
                exe.Execute();

                Namespace outNS;
                using (var lhsIt = step.Lhs.GetEnumerator())
                {
                    using (var outIt = transform.Outputs.GetEnumerator())
                    {
                        while (lhsIt.MoveNext() && outIt.MoveNext())
                        {
                            index.SymbolTable.Root.TryGetChild(((ModRef)outIt.Current.Type).Rename, out outNS);
                            resultMap.SetResult(lhsIt.Current.Name, outNS, exe.Fixpoint.Keys);
                        }
                    }
                }                
            }
            else if (mod.Reduced.Node.NodeKind == NodeKind.TSystem)
            {
                var transform = (TSystem)mod.Reduced.Node;
                var index = new TermIndex(mod.SymbolTable);
                var valParams = tsystem.InstantiateValueParams(step, index, valueParams);
                var modParams = tsystem.InstantiateModelParams(step, resultMap);

                var task = ((CoreTSystem)mod.FinalOutput).Execute(modParams, valParams, cancel);
                task.Wait();

                var subResults = task.Result.resultMap;
                using (var lhsIt = step.Lhs.GetEnumerator())
                {
                    using (var outIt = transform.Outputs.GetEnumerator())
                    {
                        while (lhsIt.MoveNext() && outIt.MoveNext())
                        {
                            resultMap.SetResult(lhsIt.Current.Name, subResults[((ModRef)outIt.Current.Type).Rename]);
                        }
                    }
                }

                subResults.Dispose();
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
