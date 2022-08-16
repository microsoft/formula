namespace Microsoft.Formula.CommandLine
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    
    using API;
    using Common;
    using Common.Rules;

    internal enum TaskKind { Query, Apply, Solve, Unknown };

    internal class TaskManager
    {
        private enum TaskTableCols { Id, Kind, Status, Result, Started, Duration, nCols }
        private SortedDictionary<int, TaskData> tasks = new SortedDictionary<int, TaskData>();

        /// <summary>
        /// True if StartTask should block until task is done.
        /// </summary>
        public bool IsWaitOn
        {
            get;
            set;
        }

        public TaskManager()
        {
        }

        public bool TryGetTask(int id, out Task task, out TaskKind kind)
        {
            TaskData data;
            if (!tasks.TryGetValue(id, out data))
            {
                task = null;
                kind = TaskKind.Unknown;
                return false;
            }

            kind = data.Kind;
            task = data.Task;
            return true;
        }

        public int UnloadTasks()
        {
            var unloadCount = tasks.Count;
            tasks.Clear();
            GC.Collect();
            return unloadCount;
        }

        public bool TryUnloadTask(int id)
        {
            if (!tasks.ContainsKey(id))
            {
                return false;
            }

            tasks.Remove(id);
            GC.Collect();
            return true;
        }

        public bool TryGetStatistics(int id, out ExecuterStatistics stats)
        {
            TaskData data;
            if (!tasks.TryGetValue(id, out data))
            {
                stats = null;
                return false;
            }

            stats = data.Statistics;
            return true;
        }

        public int StartTask(Task<QueryResult> task, ExecuterStatistics stats, CancellationTokenSource canceller)
        {
            Contract.Requires(task != null && stats != null && canceller != null);
            var data = new TaskData(tasks.Count, TaskKind.Query, task, stats, canceller);
            tasks.Add(data.Id, data);
            if (IsWaitOn)
            {
                task.RunSynchronously();
            }
            else
            {
                task.Start();
            }

            return data.Id;
        }

        public int StartTask(Task<ApplyResult> task, ExecuterStatistics stats, CancellationTokenSource canceller)
        {
            Contract.Requires(task != null && stats != null && canceller != null);
            var data = new TaskData(tasks.Count, TaskKind.Apply, task, stats, canceller);
            tasks.Add(data.Id, data);
            if (IsWaitOn)
            {
                task.RunSynchronously();
            }
            else
            {
                task.Start();
            }

            return data.Id;
        }

        public int StartTask(Task<SolveResult> task, ExecuterStatistics stats, CancellationTokenSource canceller)
        {
            Contract.Requires(task != null && stats != null && canceller != null);
            var data = new TaskData(tasks.Count, TaskKind.Solve, task, stats, canceller);
            tasks.Add(data.Id, data);
            if (IsWaitOn)
            {
                task.RunSynchronously();
            }
            else
            {
                task.Start();
            }

            return data.Id;
        }

        public void MkTaskTable(out List<string[]> rows, out int[] colWidths)
        {
            rows = new List<string[]>();
            colWidths = new int[(int)TaskTableCols.nCols];
            var title = new string[(int)TaskTableCols.nCols];

            title[(int)TaskTableCols.Id] = "Id";
            colWidths[(int)TaskTableCols.Id] = title[(int)TaskTableCols.Id].Length;
            
            title[(int)TaskTableCols.Kind] = "Kind";
            colWidths[(int)TaskTableCols.Kind] = title[(int)TaskTableCols.Kind].Length;

            title[(int)TaskTableCols.Status] = "Status";
            colWidths[(int)TaskTableCols.Status] = title[(int)TaskTableCols.Status].Length;

            title[(int)TaskTableCols.Result] = "Result";
            colWidths[(int)TaskTableCols.Result] = title[(int)TaskTableCols.Result].Length;

            title[(int)TaskTableCols.Started] = "Started";
            colWidths[(int)TaskTableCols.Started] = title[(int)TaskTableCols.Started].Length;

            title[(int)TaskTableCols.Duration] = "Duration";
            colWidths[(int)TaskTableCols.Duration] = title[(int)TaskTableCols.Duration].Length;


            rows.Add(title);

            foreach (var d in tasks.Values)
            {
                var row = new string[(int)TaskTableCols.nCols];
                row[(int)TaskTableCols.Id] = d.Id.ToString();
                colWidths[(int)TaskTableCols.Id] = Math.Max(row[(int)TaskTableCols.Id].Length, colWidths[(int)TaskTableCols.Id]); 

                row[(int)TaskTableCols.Kind] = d.Kind.ToString();
                colWidths[(int)TaskTableCols.Kind] = Math.Max(row[(int)TaskTableCols.Kind].Length, colWidths[(int)TaskTableCols.Kind]); 
                
                row[(int)TaskTableCols.Started] = string.Format("{0} {1}", d.StartTime.ToShortDateString(), d.StartTime.ToShortTimeString());
                colWidths[(int)TaskTableCols.Started] = Math.Max(row[(int)TaskTableCols.Started].Length, colWidths[(int)TaskTableCols.Started]);

                row[(int)TaskTableCols.Duration] = string.Format("{0:F2}s", d.Duration.TotalSeconds);
                colWidths[(int)TaskTableCols.Duration] = Math.Max(row[(int)TaskTableCols.Duration].Length, colWidths[(int)TaskTableCols.Duration]);

                row[(int)TaskTableCols.Result] = d.ResultSummary;
                colWidths[(int)TaskTableCols.Result] = Math.Max(row[(int)TaskTableCols.Result].Length, colWidths[(int)TaskTableCols.Result]);

                if (d.Canceller.IsCancellationRequested)
                {
                    row[(int)TaskTableCols.Status] = "Cancelled";
                }
                else
                {
                    row[(int)TaskTableCols.Status] = d.Task.IsCompleted ? "Done" : "Running";
                }

                colWidths[(int)TaskTableCols.Status] = Math.Max(row[(int)TaskTableCols.Status].Length, colWidths[(int)TaskTableCols.Status]);
                rows.Add(row);
            }
        }

        private class TaskData
        {
            public DateTime StartTime
            {
                get;
                private set;
            }

            public TaskKind Kind
            {
                get;
                private set;
            }

            public int Id
            {
                get;
                private set;
            }

            public Task Task
            {
                get;
                private set;
            }

            public ExecuterStatistics Statistics
            {
                get;
                private set;
            }

            public CancellationTokenSource Canceller
            {
                get;
                private set;
            }

            public TimeSpan Duration
            {
                get
                {
                    if (!Task.IsCompleted)
                    {
                        return DateTime.Now - StartTime;
                    }

                    switch (Kind)
                    {
                        case TaskKind.Query:
                            return ((Task<QueryResult>)Task).Result.StopTime - StartTime;
                        case TaskKind.Apply:
                            return ((Task<ApplyResult>)Task).Result.StopTime - StartTime;
                        case TaskKind.Solve:
                            return ((Task<SolveResult>)Task).Result.StopTime - StartTime;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            public string ResultSummary
            {
                get
                {
                    if (!Task.IsCompleted)
                    {
                        return "?";                 
                    }

                    switch (Kind)
                    {
                        case TaskKind.Query:
                            return ((Task<QueryResult>)Task).Result.Conclusion.ToString();
                        case TaskKind.Solve:
                            return ((Task<SolveResult>)Task).Result.Solvable.ToString();
                        case TaskKind.Apply:
                            var outs = ((Task<ApplyResult>)Task).Result.OutputNames;
                            var outStr = string.Empty;
                            int i = 1;
                            foreach (var id in outs)
                            {
                                if (i == outs.Count)
                                {
                                    outStr += id.Name;
                                }
                                else
                                {
                                    outStr += id.Name + ", ";                                        
                                }
                            }

                            return outStr;
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            public TaskData(int id, TaskKind kind, Task task, ExecuterStatistics stats, CancellationTokenSource canceller)
            {
                Contract.Requires(id >= 0);
                Contract.Requires(task != null && stats != null && canceller != null);
                Id = id;
                Kind = kind;
                Task = task;
                Statistics = stats;
                Canceller = canceller;
                StartTime = DateTime.Now;
            }
        }        
    }
}
