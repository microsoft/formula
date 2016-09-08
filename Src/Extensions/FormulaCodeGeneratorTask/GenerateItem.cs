namespace FormulaCodeGeneratorTask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Formula.API;
    using Microsoft.Formula.API.ASTQueries;
    using Microsoft.Formula.API.Nodes;
    using Microsoft.Formula.API.Generators;

    internal class GenerateItem
    {
        public string InputFile
        {
            get;
            private set;
        }

        public string Namespace
        {
            get;
            private set;
        }

        public bool IsThreadSafe
        {
            get;
            private set;
        }

        public bool IsObjectGraph
        {
            get;
            private set;
        }

        public bool IsNewOnly
        {
            get;
            private set;
        }

        public GenerateItem(
            string inputFile,
            string @namespace,
            string isThreadSafe,
            string isObjectGraph,
            string isNewOnly)
        {
            InputFile = inputFile.Trim();
            Namespace = @namespace.Trim();
            IsThreadSafe = isThreadSafe.Trim().ToUpperInvariant() != "FALSE";
            IsObjectGraph = isObjectGraph.Trim().ToUpperInvariant() != "FALSE";
            IsNewOnly = isNewOnly.Trim().ToUpperInvariant() != "FALSE";
        }
        
        public bool Generate(FormulaCodeGeneratorTask genTask)
        {
            var outputFile = InputFile + ".g.cs";

            var env = new Env();
            try
            {
                InstallResult ires;
                var progName = new ProgramName(InputFile);
                env.Install(InputFile, out ires);
                PrintFlags(genTask, ires.Flags);
                if (!ires.Succeeded)
                {
                    return false;
                }

                AST<Program> program = null;
                foreach (var touched in ires.Touched)
                {
                    if (touched.Program.Node.Name.Equals(progName))
                    {
                        program = touched.Program;
                        break;
                    }
                }

                if (program == null)
                {
                    PrintError(genTask, InputFile, "Could not find input file");
                    return false;
                }

                string name;
                string modName = null;
                var moduleQuery = new NodePred[] { NodePredFactory.Instance.Star, NodePredFactory.Instance.Module };
                program.FindAll(
                    moduleQuery,
                    (ch, n) =>
                    {
                        if (n.TryGetStringAttribute(AttributeKind.Name, out name))
                        {
                            if (modName == null)
                            {
                                modName = name;
                            }
                            else
                            {
                                genTask.Log.LogWarning(
                                    string.Empty,
                                    string.Empty,
                                    string.Empty,
                                    InputFile,
                                    n.Span.StartLine,
                                    n.Span.StartCol,
                                    n.Span.EndLine,
                                    n.Span.EndCol,
                                    "Code will only be generated for module {0}; ignoring module {1}.", 
                                    modName, 
                                    name);
                            }
                        }
                    });

                if (modName == null)
                {
                    PrintWarning(genTask, InputFile, "Could not find any modules in input file.");
                    return true;
                }

                try
                {
                    var outInfo = new System.IO.FileInfo(outputFile);
                    if (outInfo.Exists)
                    {
                        outInfo.Delete();
                    }

                    using (var sw = new System.IO.StreamWriter(outputFile))
                    {
                        var opts = new GeneratorOptions(
                            GeneratorOptions.Language.CSharp,
                            IsThreadSafe,
                            IsNewOnly,
                            modName,
                            Namespace);

                        Task<GenerateResult> gres;
                        env.Generate(progName, modName, sw, opts, out gres);
                        gres.Wait();
                        PrintFlags(genTask, InputFile, gres.Result.Flags);
                        if (gres.Result.Succeeded)
                        {
                            genTask.Log.LogMessage("Transformed {0} -> {1}", InputFile, outputFile);
                        }

                        return gres.Result.Succeeded;
                    }
                }
                catch (Exception e)
                {
                    PrintError(genTask, outputFile, e.Message);
                    return false;
                }
            }
            catch (Exception e)
            {
                PrintError(genTask, InputFile, e.Message);
                return false;
            }
        }

        private void PrintError(FormulaCodeGeneratorTask task, string file, string message)
        {
            task.Log.LogError("{0}: {1}", file, message);
        }

        private void PrintWarning(FormulaCodeGeneratorTask task, string file, string message)
        {
            task.Log.LogWarning("{0}: {1}", file, message);
        }

        private void PrintFlags(FormulaCodeGeneratorTask task, string file,  IEnumerable<Flag> flags)
        {
            foreach (var f in flags)
            {
                switch (f.Severity)
                {
                    case SeverityKind.Info:
                        task.Log.LogMessage(
                            string.Empty,
                            f.Code.ToString(),
                            string.Empty,
                            file,
                            f.Span.StartLine,
                            f.Span.StartCol,
                            f.Span.EndLine,
                            f.Span.EndCol,
                            Microsoft.Build.Framework.MessageImportance.Normal,
                            f.Message);
                        break;
                    case SeverityKind.Warning:
                        task.Log.LogWarning(
                            string.Empty,
                            f.Code.ToString(),
                            string.Empty,
                            file,
                            f.Span.StartLine,
                            f.Span.StartCol,
                            f.Span.EndLine,
                            f.Span.EndCol,
                            f.Message);
                        break;
                    case SeverityKind.Error:
                        task.Log.LogError(
                            string.Empty,
                            f.Code.ToString(),
                            string.Empty,
                            file,
                            f.Span.StartLine,
                            f.Span.StartCol,
                            f.Span.EndLine,
                            f.Span.EndCol,
                            f.Message);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private void PrintFlags(
            FormulaCodeGeneratorTask task,
            IEnumerable<Tuple<AST<Microsoft.Formula.API.Nodes.Program>, Flag>> flags)
        {
            foreach (var f in flags)
            {
                switch (f.Item2.Severity)
                {
                    case SeverityKind.Info:
                        task.Log.LogMessage(
                            string.Empty,
                            f.Item2.Code.ToString(),
                            string.Empty,
                            f.Item1.Node.Name.ToString(),
                            f.Item2.Span.StartLine,
                            f.Item2.Span.StartCol,
                            f.Item2.Span.EndLine,
                            f.Item2.Span.EndCol,
                            Microsoft.Build.Framework.MessageImportance.Normal,
                            f.Item2.Message);                           
                        break;
                    case SeverityKind.Warning:
                        task.Log.LogWarning(
                            string.Empty,
                            f.Item2.Code.ToString(),
                            string.Empty,
                            f.Item1.Node.Name.ToString(),
                            f.Item2.Span.StartLine,
                            f.Item2.Span.StartCol,
                            f.Item2.Span.EndLine,
                            f.Item2.Span.EndCol,
                            f.Item2.Message);                           
                        break;
                    case SeverityKind.Error:
                        task.Log.LogError(
                            string.Empty,
                            f.Item2.Code.ToString(),
                            string.Empty,
                            f.Item1.Node.Name.ToString(),
                            f.Item2.Span.StartLine,
                            f.Item2.Span.StartCol,
                            f.Item2.Span.EndLine,
                            f.Item2.Span.EndCol,
                            f.Item2.Message);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}
