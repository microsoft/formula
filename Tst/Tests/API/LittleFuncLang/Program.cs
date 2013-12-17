namespace LittleFuncLang
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Microsoft.Formula.API;    
    using Microsoft.Formula.API.Generators;
    using Microsoft.Formula.API.Nodes;

    class Program
    {
        private static string formulaFile;

        static void Main(string[] args)
        {
            var asDir = new DirectoryInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);
            formulaFile = asDir.Parent.Parent.Parent.FullName + "\\LittleFuncLang.4ml";

            List<ICSharpTerm> objects;
            AST<Model> model;

            if (LoadModelFromFile("Prog4", out objects))
            {
                ToAST("Prog4", objects, out model);

                LittleFuncLang_Root.Let letExpr = null;
                foreach (var o in objects)
                {
                    if (o is LittleFuncLang_Root.Let)
                    {
                        letExpr = (LittleFuncLang_Root.Let)o;
                        break;
                    }
                }

                if (letExpr == null)
                {
                    Console.WriteLine("Could not find a let expression");
                }
                else
                {
                    letExpr.@in = (LittleFuncLang_Root.IArgType_Let__2)letExpr.init;
                }

                ToAST("Prog4'", objects, out model);
            }
        }

        private static bool ToAST(string modelName, List<ICSharpTerm> objects, out AST<Model> model)
        {
            var result = Factory.Instance.MkModel(
                    modelName,
                    "LittleFuncLang",
                    objects,
                    out model,
                    null,
                    "LittleFuncLang.4ml");

            if (result)
            {
                model.Print(Console.Out);
                return true;
            }
            else
            {
                Console.WriteLine("Could not build model from object graph");
                return false;
            }
        }

        private static bool LoadModelFromFile(string model, out List<ICSharpTerm> objects)
        {
            objects = null;

            InstallResult res;
            var env = new Env();
            if (!env.Install(formulaFile, out res) && res == null)
            {
                throw new Exception("Could not start installation");
            }

            foreach (var f in res.Flags)
            {
                Console.WriteLine("{0} ({1}, {2}) : {3} {4} : {5}",
                    f.Item1.Node.Name,
                    f.Item2.Span.StartLine,
                    f.Item2.Span.StartCol,
                    f.Item2.Severity,
                    f.Item2.Code,
                    f.Item2.Message);
            }

            if (!res.Succeeded)
            {
                Console.WriteLine("Could not install file; exiting");
                return false;
            }

            Task<ObjectGraphResult> createTask;
            if (!LittleFuncLang_Root.CreateObjectGraph(env, new ProgramName(formulaFile), "Prog4", out createTask))
            {
                throw new Exception("Could not start object graph creation");
            }

            createTask.Wait();
            foreach (var f in createTask.Result.Flags)
            {
                Console.WriteLine("({0}, {1}) : {2} {3} : {4}",
                    f.Span.StartLine,
                    f.Span.StartCol,
                    f.Severity,
                    f.Code,
                    f.Message);
            }

            if (!createTask.Result.Succeeded)
            {
                Console.WriteLine("Could not create object graph {0}; exiting", model);
                return false;
            }

            objects = createTask.Result.Objects;
            return true;
        }
    }
}
