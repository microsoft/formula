namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics.Contracts;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.Nodes;
    using API.ASTQueries;

    internal class Loader
    {
        private static readonly NodePred[] queryFileRefs = 
            new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.ModRef) |
                NodePredFactory.Instance.MkPredicate(NodeKind.Setting)
            };

        private BlockingCollection<Tuple<ProgramName, string, Span>> workItems =
            new BlockingCollection<Tuple<ProgramName, string, Span>>();

        private Dictionary<ProgramName, object> programs =
            new Dictionary<ProgramName, object>();

        private AST<Program> initial;

        private Env env;

        private CancellationToken cancel;

        private InstallResult iresult;

        public Loader(Env env, AST<Program> p, InstallResult iresult, CancellationToken cancel)
        {
            Contract.Requires(env != null);
            Contract.Requires(p != null);

            this.cancel = cancel;
            this.env = env;
            this.iresult = iresult;
            initial = p;            
        }

        public bool Load()
        {
            programs.Add(initial.Node.Name, initial);
            var path = MkRootPath(initial.Node);
            initial.Node.FindAll(path, queryFileRefs, (pt, x) => EnqueuePrograms(initial, null, x), cancel);
            if (cancel.IsCancellationRequested)
            {
                return false;
            }
            else if (workItems.Count == 0)
            {
                return iresult.Succeeded;
            }

            workItems.Add(null);
            int taskCount = 1;
            foreach (var w in workItems.GetConsumingEnumerable())
            {
                if (w == null)
                {
                    --taskCount;
                    if (taskCount == 0)
                    {
                        break;
                    }
                }
                else if (programs.ContainsKey(w.Item1))
                {
                    continue;
                }
                else if (env.Programs.ContainsKey(w.Item1))
                {
                    iresult.AddTouched(new ASTConcr<Program>(env.Programs[w.Item1]), InstallKind.Cached);
                    programs.Add(w.Item1, env.Programs[w.Item1]);
                }
                else
                {
                    ++taskCount;
                    var loaderTask = Factory.Instance.ParseFile(w.Item1, w.Item2, w.Item3, cancel).ContinueWith(
                        (t) =>
                        {
                            if (cancel.IsCancellationRequested)
                            {
                                workItems.Add(null);
                                return t.Result;
                            }

                            var prog = t.Result.Program;
                            prog.FindAll(queryFileRefs, (pt, x) => EnqueuePrograms(prog, t.Result, x), cancel);
                            if (cancel.IsCancellationRequested)
                            {
                                workItems.Add(null);
                                return t.Result;
                            }

                            workItems.Add(null);
                            return t.Result;
                        });

                    programs.Add(w.Item1, loaderTask);
                }               
            }

            foreach (var kv in programs)
            {
                var t = kv.Value as Task<ParseResult>;
                if (t == null)
                {
                    continue;
                }
                else if (!t.Result.Succeeded)
                {
                    iresult.AddTouched(t.Result.Program, InstallKind.Failed);
                    iresult.Succeeded = false;
                }
                else
                {
                    iresult.AddTouched(t.Result.Program, InstallKind.Compiled);
                }

                iresult.AddFlags(t.Result);
            }

            if (cancel.IsCancellationRequested)
            {
                iresult.Succeeded = false;
            }

            return iresult.Succeeded;
        }

        private void EnqueuePrograms(AST<Program> source, ParseResult res, Node node)
        {
            if (node.NodeKind == NodeKind.ModRef) 
            {
                TryEnqueue(((ModRef)node).Location, source, node.Span, res);
            }
            else if (node.NodeKind == NodeKind.Setting)
            {
                Setting s = (Setting)node;
                if (s.Value.CnstKind != CnstKind.String)
                {
                    return;
                }
                else if (s.Key.Name == Configuration.DefaultsSetting)
                {
                    TryEnqueue(s.Value.GetStringValue(), source, s.Value.Span, res);
                }
                else if (s.Key.Fragments.Length == 2 && s.Key.Fragments[0] == Configuration.ModulesCollectionName)
                {
                    AST<ModRef> mref;
                    if (Factory.Instance.TryParseReference(s.Value.GetStringValue(), out mref))
                    {
                        TryEnqueue(mref.Node.Location, source, s.Value.Span, res);
                    }
                }
            }
        }

        private void TryEnqueue(string name, AST<Program> refSource, Span refSpan, ParseResult res)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                var progName = new ProgramName(name, refSource.Node.Name);
                workItems.Add(new Tuple<ProgramName, string, Span>(progName, refSource.Node.Name.ToString(env.Parameters), refSpan));
            }
            catch (Exception e)
            {
                var badRef = new Flag(
                    SeverityKind.Error,
                    refSpan,
                    Constants.BadFile.ToString(e.Message),
                    Constants.BadFile.Code,
                    refSource.Node.Name);

                if (res == null)
                {
                    iresult.AddFlag(refSource, badRef);
                }
                else
                {
                    res.AddFlag(badRef);
                }
            }
        }

        private static LinkedList<ChildInfo> MkRootPath(Node n)
        {
            var path = new LinkedList<ChildInfo>();
            path.AddLast(new ChildInfo(n, ChildContextKind.AnyChildContext, -1, -1));
            return path;
        }
    }
}
