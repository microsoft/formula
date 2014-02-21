namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Nodes;

    internal static class Printing
    {
        private static readonly int CancelCheckReq = 500;

        private static readonly Func<Node, PrintData, TextWriter, EnvParams, IEnumerable<Tuple<Node, PrintData>>>[] printerStarts;

        private static readonly Action<Node, PrintData, TextWriter, EnvParams>[] printerEnds;

        static Printing()
        {
            printerStarts = new Func<Node, PrintData, TextWriter, EnvParams, IEnumerable<Tuple<Node, PrintData>>>[(int)NodeKind.AnyNodeKind];
            printerEnds = new Action<Node, PrintData, TextWriter, EnvParams>[(int)NodeKind.AnyNodeKind];

            printerStarts[(int)NodeKind.Folder] = StartFolder;
            printerEnds[(int)NodeKind.Folder] = EndFolder;

            printerStarts[(int)NodeKind.Program] = StartProgram;
            printerEnds[(int)NodeKind.Program] = EndProgram;

            printerStarts[(int)NodeKind.Domain] = StartDomain;
            printerEnds[(int)NodeKind.Domain] = EndDomain;

            printerStarts[(int)NodeKind.Transform] = StartTransform;
            printerEnds[(int)NodeKind.Transform] = EndTransform;

            printerStarts[(int)NodeKind.TSystem] = StartTSystem;
            printerEnds[(int)NodeKind.TSystem] = EndTSystem;

            printerStarts[(int)NodeKind.Model] = StartModel;
            printerEnds[(int)NodeKind.Model] = EndModel;

            printerStarts[(int)NodeKind.Machine] = StartMachine;
            printerEnds[(int)NodeKind.Machine] = EndMachine;

            printerStarts[(int)NodeKind.Config] = StartConfig;
            printerEnds[(int)NodeKind.Config] = EndConfig;

            printerStarts[(int)NodeKind.Setting] = StartSetting;
            printerEnds[(int)NodeKind.Setting] = EndSetting;

            printerStarts[(int)NodeKind.ModRef] = StartModRef;
            printerEnds[(int)NodeKind.ModRef] = EndModRef;

            printerStarts[(int)NodeKind.Id] = StartId;
            printerEnds[(int)NodeKind.Id] = EndId;

            printerStarts[(int)NodeKind.Cnst] = StartCnst;
            printerEnds[(int)NodeKind.Cnst] = EndCnst;

            printerStarts[(int)NodeKind.MapDecl] = StartMapDecl;
            printerEnds[(int)NodeKind.MapDecl] = EndMapDecl;

            printerStarts[(int)NodeKind.Field] = StartField;
            printerEnds[(int)NodeKind.Field] = EndField;

            printerStarts[(int)NodeKind.ConDecl] = StartConDecl;
            printerEnds[(int)NodeKind.ConDecl] = EndConDecl;

            printerStarts[(int)NodeKind.UnnDecl] = StartUnnDecl;
            printerEnds[(int)NodeKind.UnnDecl] = EndUnnDecl;

            printerStarts[(int)NodeKind.Union] = StartUnion;
            printerEnds[(int)NodeKind.Union] = EndUnion;

            printerStarts[(int)NodeKind.Enum] = StartEnum;
            printerEnds[(int)NodeKind.Enum] = EndEnum;

            printerStarts[(int)NodeKind.Range] = StartRange;
            printerEnds[(int)NodeKind.Range] = EndRange;

            printerStarts[(int)NodeKind.Rule] = StartRule;
            printerEnds[(int)NodeKind.Rule] = EndRule;

            printerStarts[(int)NodeKind.Body] = StartBody;
            printerEnds[(int)NodeKind.Body] = EndBody;

            printerStarts[(int)NodeKind.Find] = StartFind;
            printerEnds[(int)NodeKind.Find] = EndFind;

            printerStarts[(int)NodeKind.FuncTerm] = StartFuncTerm;
            printerEnds[(int)NodeKind.FuncTerm] = EndFuncTerm;

            printerStarts[(int)NodeKind.RelConstr] = StartRelConstr;
            printerEnds[(int)NodeKind.RelConstr] = EndRelConstr;

            printerStarts[(int)NodeKind.Quote] = StartQuote;
            printerEnds[(int)NodeKind.Quote] = EndQuote;

            printerStarts[(int)NodeKind.QuoteRun] = StartQuoteRun;
            printerEnds[(int)NodeKind.QuoteRun] = EndQuoteRun;

            printerStarts[(int)NodeKind.Param] = StartParam;
            printerEnds[(int)NodeKind.Param] = EndParam;

            printerStarts[(int)NodeKind.ContractItem] = StartContractItem;
            printerEnds[(int)NodeKind.ContractItem] = EndContractItem;

            printerStarts[(int)NodeKind.CardPair] = StartCardPair;
            printerEnds[(int)NodeKind.CardPair] = EndCardPair;

            printerStarts[(int)NodeKind.Step] = StartStep;
            printerEnds[(int)NodeKind.Step] = EndStep;

            printerStarts[(int)NodeKind.ModApply] = StartModApply;
            printerEnds[(int)NodeKind.ModApply] = EndModApply;

            printerStarts[(int)NodeKind.Update] = StartUpdate;
            printerEnds[(int)NodeKind.Update] = EndUpdate;

            printerStarts[(int)NodeKind.Property] = StartProperty;
            printerEnds[(int)NodeKind.Property] = EndProperty;

            printerStarts[(int)NodeKind.Compr] = StartCompr;
            printerEnds[(int)NodeKind.Compr] = EndCompr;

            printerStarts[(int)NodeKind.ModelFact] = StartModelFact;
            printerEnds[(int)NodeKind.ModelFact] = EndModelFact;
        }

        internal static void Print(
                    Node root, 
                    TextWriter wr, 
                    System.Threading.CancellationToken cancel = default(System.Threading.CancellationToken),
                    EnvParams envParams = null)
        {
            Contract.Requires(root != null);
            Contract.Requires(wr != null);

            var ctok = cancel == default(System.Threading.CancellationToken) ? null : ASTComputationBase.MkControlToken(cancel, CancelCheckReq);
            var pstack = new Stack<PrintData>();
            pstack.Push(new PrintData());

            var acomp = new ASTComputationUpDown<PrintData, bool>(
                root,
                (n, d) =>
                {
                    pstack.Push(d);
                    PrintPreamble(d, wr, cancel);
                    return printerStarts[(int)n.NodeKind](n, d, wr, envParams);
                },
                (n, v, ch) =>
                {
                    printerEnds[(int)n.NodeKind](n, pstack.Peek(), wr, envParams);
                    PrintSuffix(pstack.Pop(), wr);
                    return true;
                },
                ctok);

            acomp.Compute(pstack.Peek());
        }

        private static void PrintPreamble(
                                          PrintData data, 
                                          TextWriter wr, 
                                          System.Threading.CancellationToken cancel)
        {
            if (!data.NoIndent && data.Indentation > 0)
            {
                wr.Write(data.GetIndentString());
            }

            if (data.Prefix != null)
            {
                wr.Write(data.Prefix);
            }
        }

        private static void PrintSuffix(PrintData data, TextWriter wr)
        {
            if (data.Suffix != null)
            {
                wr.Write(data.Suffix);
            }
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartFolder(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var folder = (Folder)node;
            wr.WriteLine("//// Folder {0}", folder.Name);
            var nextData = new PrintData(null, null, data.Indentation + 1);
            foreach (var c in folder.Children)
            {
                yield return new Tuple<Node, PrintData>(c, nextData);
            }           
        }

        private static void EndFolder(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartProgram(Node node, PrintData data, TextWriter wr, EnvParams envParams) 
        {
            var program = (Program)node;
            wr.WriteLine("//// Program {0}", program.Name.ToString(envParams));

            if (program.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(program.Config, new PrintData(null, "\n", data.Indentation));
            }

            var nextData = new PrintData(null, null, data.Indentation);
            foreach (var c in program.Modules)
            {
                yield return new Tuple<Node, PrintData>(c, nextData);
            }
        }

        private static void EndProgram(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartDomain(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var dom = (Domain)node;

            if (dom.Config.Settings.Count > 0 || dom.Compositions.Count > 0)
            {
                if (dom.Compositions.Count == 0)
                {
                    wr.Write("domain {0}\n", dom.Name);
                }
                else
                {
                    wr.Write("domain {0}", dom.Name);
                }
            }
            else
            {
                wr.Write("domain {0}\n{1}{{\n", dom.Name, data.GetIndentString());
            }
            
            if (dom.ComposeKind != ComposeKind.None)
            {
                var i = 0;
                var terminator = dom.Config.Settings.Count == 0 ? string.Format("\n{0}{{\n", data.GetIndentString()) : "\n";
                wr.Write(" " + ASTQueries.ASTSchema.Instance.ToString(dom.ComposeKind));

                foreach (var cmp in dom.Compositions)
                {
                    yield return new Tuple<Node, PrintData>(cmp, new PrintData(" ", i < dom.Compositions.Count - 1 ? "," : terminator, 0));
                    ++i;
                }
            }

            if (dom.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(dom.Config, new PrintData(null, data.GetIndentString() + "{\n", data.Indentation));
            }

            var nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var td in dom.TypeDecls)
            {
                yield return new Tuple<Node, PrintData>(td, nextData);
            }

            nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var rl in dom.Rules)
            {
                yield return new Tuple<Node, PrintData>(rl, nextData);
            }

            nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var cn in dom.Conforms)
            {
                yield return new Tuple<Node, PrintData>(cn, nextData);
            }
        }

        private static void EndDomain(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(data.GetIndentString() + "}");
            wr.WriteLine();
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartTransform(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var trns = (Transform)node;

            if (trns.Inputs.Count == 0)
            {
                wr.Write("transform {0} () ", trns.Name);
            }
            else
            {
                wr.Write("transform {0} (", trns.Name);
            }

            var i = 0;
            foreach (var input in trns.Inputs)
            {
                yield return new Tuple<Node, PrintData>(input, new PrintData(null, i < trns.Inputs.Count - 1 ? ", " : ")", 0));
                ++i;
            }

            i = 0;
            string terminator = trns.Config.Settings.Count > 0 ? ")\n" : string.Format(")\n{0}{{\n", data.GetIndentString());
            foreach (var output in trns.Outputs)
            {
                yield return new Tuple<Node, PrintData>(
                    output, 
                    new PrintData(
                        i == 0 ? string.Format("\n{0}returns (", data.GetIndentString()) : null, 
                        i < trns.Outputs.Count - 1 ? ", " : terminator, 0));
                ++i;
            }

            if (trns.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(trns.Config, new PrintData(null, data.GetIndentString() + "{\n", data.Indentation));
            }

            var nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var ci in trns.Contracts)
            {
                yield return new Tuple<Node, PrintData>(ci, nextData);
            }

            foreach (var td in trns.TypeDecls)
            {
                yield return new Tuple<Node, PrintData>(td, nextData);
            }

            nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var rl in trns.Rules)
            {
                yield return new Tuple<Node, PrintData>(rl, nextData);
            }
        }

        private static void EndTransform(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(data.GetIndentString() + "}");
            wr.WriteLine();
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartTSystem(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var tsys = (TSystem)node;

            if (tsys.Inputs.Count == 0)
            {
                wr.Write("transform system {0} () ", tsys.Name);
            }
            else
            {
                wr.Write("transform system {0} (", tsys.Name);
            }

            var i = 0;
            foreach (var input in tsys.Inputs)
            {
                yield return new Tuple<Node, PrintData>(input, new PrintData(null, i < tsys.Inputs.Count - 1 ? ", " : ")", 0));
                ++i;
            }

            i = 0;
            string terminator = tsys.Config.Settings.Count > 0 ? ")\n" : string.Format(")\n{0}{{\n", data.GetIndentString());
            foreach (var output in tsys.Outputs)
            {
                yield return new Tuple<Node, PrintData>(
                    output,
                    new PrintData(
                        i == 0 ? string.Format("\n{0}returns (", data.GetIndentString()) : null,
                        i < tsys.Outputs.Count - 1 ? ", " : terminator, 0));
                ++i;
            }

            if (tsys.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(tsys.Config, new PrintData(null, data.GetIndentString() + "{\n", data.Indentation));
            }

            var nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var stp in tsys.Steps)
            {
                yield return new Tuple<Node, PrintData>(stp, nextData);
            }
        }

        private static void EndTSystem(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(data.GetIndentString() + "}");
            wr.WriteLine();
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartModel(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var model = (Model)node;
            if (model.IsPartial)
            {
                wr.Write("partial ");
            }

            wr.Write("model {0} of ", model.Name);
            var terminator = model.Config.Settings.Count == 0 && model.Compositions.Count == 0 ? "\n" + data.GetIndentString() + "{\n" : null;
            yield return new Tuple<Node, PrintData>(model.Domain, new PrintData(null, terminator, 0));

            var i = 0;
            terminator = model.Config.Settings.Count == 0 ? "\n" + data.GetIndentString() + "{\n" : "\n";
            var cmpName = model.ComposeKind == ComposeKind.Extends ? " extends " : " includes ";
            foreach (var inc in model.Compositions)
            {
                yield return new Tuple<Node, PrintData>(inc, new PrintData(i == 0 ? cmpName : null, i < model.Compositions.Count - 1 ? ", " : terminator, 0));
                ++i;
            }

            if (model.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(model.Config, new PrintData(model.Compositions.Count == 0 ? "\n" : null, data.GetIndentString() + "{\n", data.Indentation));
            }
            
            i = 0;
            foreach (var ci in model.Contracts)
            {
                yield return new Tuple<Node, PrintData>(ci, new PrintData(null, i == model.Contracts.Count - 1 ? "\n" : null, data.Indentation + 1, true));
                ++i;
            }

            i = 0;
            foreach (var f in model.Facts)
            {
                yield return new Tuple<Node, PrintData>(f, new PrintData(null, i < model.Facts.Count - 1 ? ",\n" : ".\n", data.Indentation + 1, true));
                ++i;
            }
        }

        private static void EndModel(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(data.GetIndentString() + "}");
            wr.WriteLine();
        }
      
        private static IEnumerable<Tuple<Node, PrintData>> StartMachine(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var mach = (Machine)node;
            if (mach.Inputs.Count == 0)
            {
                wr.Write("machine {0} ()", mach.Name);
            }
            else
            {
                wr.Write("machine {0} (", mach.Name);
            }

            var i = 0;
            foreach (var input in mach.Inputs)
            {
                yield return new Tuple<Node, PrintData>(input, new PrintData(null, i < mach.Inputs.Count - 1 ? ", " : ")", 0));
                ++i;
            }

            i = 0;
            var terminator = mach.Config.Settings.Count == 0 ? string.Format("\n{0}{{\n", data.GetIndentString()) : "\n";
            foreach (var st in mach.StateDomains)
            {
                yield return new Tuple<Node, PrintData>(
                    st, 
                    i < mach.StateDomains.Count - 1 ? new PrintData(null, ", ", 0) : new PrintData(" of ", terminator, 0));
                ++i;
            }

            if (mach.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(mach.Config, new PrintData(null, data.GetIndentString() + "{\n", data.Indentation));
            }

            PrintData nextData;
            foreach (var st in mach.BootSequence)
            {
                if (st.Config != null && st.Config.Settings.Count > 0)
                {
                    yield return new Tuple<Node, PrintData>(
                        st.Config, 
                        new PrintData(
                            string.Format("\n{0}", PrintData.GetIndentString(data.Indentation + 1)), 
                            null, 
                            data.Indentation + 1));

                    nextData = new PrintData(
                            string.Format(
                                "boot\n{0}",
                                PrintData.GetIndentString(data.Indentation + 2)),
                            null,
                            data.Indentation + 1);

                    yield return new Tuple<Node, PrintData>(st, nextData);
                }
                else
                {
                    nextData = new PrintData(
                            string.Format(
                                "\n{0}boot\n{1}",
                                PrintData.GetIndentString(data.Indentation + 1),
                                PrintData.GetIndentString(data.Indentation + 2)),
                            null,
                            data.Indentation + 1);

                    yield return new Tuple<Node, PrintData>(st, nextData);
                }
            }

            PrintData initData;
            foreach (var init in mach.Initials)
            {
                if (init.Config != null && init.Config.Settings.Count > 0)
                {
                    yield return new Tuple<Node, PrintData>(
                        init.Config,
                        new PrintData(
                            string.Format("\n{0}", PrintData.GetIndentString(data.Indentation + 1)),
                            null,
                            data.Indentation + 1));

                    initData = new PrintData(
                            PrintData.GetIndentString(data.Indentation + 1) + "initially\n",
                            null,
                            data.Indentation + 2,
                            true);

                    yield return new Tuple<Node, PrintData>(init, initData);
                }
                else
                {
                    initData = new PrintData(
                            "\n" + PrintData.GetIndentString(data.Indentation + 1) + "initially\n",
                            null,
                            data.Indentation + 2,
                            true);

                    yield return new Tuple<Node, PrintData>(init, initData);
                }
            }

            foreach (var nxt in mach.Nexts)
            {
                if (nxt.Config != null && nxt.Config.Settings.Count > 0)
                {
                    yield return new Tuple<Node, PrintData>(
                        nxt.Config,
                        new PrintData(
                            string.Format("\n{0}", PrintData.GetIndentString(data.Indentation + 1)),
                            null,
                            data.Indentation + 1));

                    initData = new PrintData(
                            PrintData.GetIndentString(data.Indentation + 1) + "next\n",
                            null,
                            data.Indentation + 2,
                            true);

                    yield return new Tuple<Node, PrintData>(nxt, initData);
                }
                else
                {
                    initData = new PrintData(
                            "\n" + PrintData.GetIndentString(data.Indentation + 1) + "next\n",
                            null,
                            data.Indentation + 2,
                            true);

                    yield return new Tuple<Node, PrintData>(nxt, initData);
                }
            }

            nextData = new PrintData(null, null, data.Indentation + 1, true);
            foreach (var prp in mach.Properties)
            {
                yield return new Tuple<Node, PrintData>(prp, nextData);
            }
        }

        private static void EndMachine(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(data.GetIndentString() + "}");
            wr.WriteLine();
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartConfig(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            if (((Config)node).Settings.Count == 0)
            {
                yield break;
            }

            wr.WriteLine("[");
            var conf = (Config)node;
            var i = 0;
            foreach (var s in conf.Settings)
            {
                yield return new Tuple<Node, PrintData>(s, new PrintData(null, i < conf.Settings.Count - 1 ? ",\n" : "\n", data.Indentation + 1));
                ++i;
            }
        }

        private static void EndConfig(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            if (((Config)node).Settings.Count > 0)
            {
                wr.WriteLine(data.GetIndentString() + "]");
            }
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartSetting(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var sett = (Setting)node;
            yield return new Tuple<Node, PrintData>(sett.Key, new PrintData(null, " = ", 0));
            yield return new Tuple<Node, PrintData>(sett.Value, new PrintData());
        }

        private static void EndSetting(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartModRef(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var mref = (ModRef)node;
            if (mref.Rename != null)
            {
                wr.Write("{0}:: ", mref.Rename);
            }

            wr.Write(mref.Name);

            var printKind = EnvParams.GetReferencePrintKindParameter(envParams, EnvParamKind.Printer_ReferencePrintKind);

            Uri absolutePath = null;
            //// A mod ref has an absolute path if (1) it is marked by the compiler, (2) the path appears to be absolute.
            if (mref.CompilerData is Compiler.Location)
            {
                absolutePath = ((Compiler.Location)mref.CompilerData).Program.Name.Uri;
            }
            else if (!string.IsNullOrEmpty(mref.Location))
            {
                //// Need to check if the reference is an absolute Uri. Operation may fail.
                try
                {
                    var uri = new Uri(mref.Location);
                    if (uri.IsAbsoluteUri)
                    {
                        absolutePath = uri;
                    }
                }
                catch
                {
                }
            }

            //// If this module was compiled, then read the full URI when printing.
            if (printKind != ReferencePrintKind.Verbatim && absolutePath != null)
            {
                if (printKind == ReferencePrintKind.Relative && envParams.BaseUri != null)
                {
                    try
                    {
                        var rel = envParams.BaseUri.MakeRelativeUri(absolutePath).ToString().ToLowerInvariant();
                        if (!string.IsNullOrEmpty(rel))
                        {
                            wr.Write(" at {0}", ASTQueries.ASTSchema.Instance.Encode(rel));
                        }

                        yield break;
                    }
                    catch
                    {
                    }
                }

                wr.Write(" at {0}", ASTQueries.ASTSchema.Instance.Encode(absolutePath.ToString().ToLowerInvariant()));
                yield break;
            }

            if (mref.Location != null)
            {
                wr.Write(" at {0}", ASTQueries.ASTSchema.Instance.Encode(mref.Location));
            }

            yield break;
        }

        private static void EndModRef(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartId(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.Write(((Id)node).Name);
            yield break;
        }

        private static void EndId(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartCnst(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var cn = (Cnst)node;
            switch (cn.CnstKind)
            {
                case CnstKind.String:
                    wr.Write(ASTQueries.ASTSchema.Instance.Encode((string)cn.Raw));
                    break;
                default:
                    wr.Write(cn.Raw);
                    break;
            }

            yield break;
        }

        private static void EndCnst(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartMapDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var map = (MapDecl)node;
            if (map.Config != null && map.Config.Settings.Count > 0)
            {
                wr.WriteLine();
                yield return new Tuple<Node, PrintData>(map.Config, new PrintData(null, null, data.Indentation));
            }

            string declStr = string.Format(
                "{2}{0} ::= {1} (", 
                map.Name, 
                ASTQueries.ASTSchema.Instance.ToString(map.MapKind),
                data.GetIndentString());
            int i = 0;
            foreach (var fld in map.Dom)
            {                
                yield return new Tuple<Node, PrintData>(fld, new PrintData(i == 0 ? declStr : null, i < map.Dom.Count - 1 ? ", " : map.IsPartial ? " -> " : " => ", 0));
                ++i;
            }

            i = 0;
            foreach (var fld in map.Cod)
            {
                yield return new Tuple<Node, PrintData>(fld, new PrintData(null, i < map.Cod.Count - 1 ? ", " : null, 0));
                ++i;
            }
        }

        private static void EndMapDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(").");
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartConDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var con = (ConDecl)node;
            if (con.Config != null && con.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(con.Config, new PrintData(null, null, data.Indentation));
            }
            
            int i = 0;
            foreach (var fld in con.Fields)
            {
                if (i == 0)
                {
                    yield return new Tuple<Node, PrintData>(
                        fld,
                        new PrintData(
                            string.Format("{0} ::= {1}(", con.Name, con.IsNew ? "new " : (con.IsSub ? "sub " : "")), 
                            i < con.Fields.Count - 1 ? ", " : ").\n", data.Indentation));
                }
                else
                {
                    yield return new Tuple<Node, PrintData>(fld, new PrintData(null, i < con.Fields.Count - 1 ? ", " : ").\n", 0));
                }

                ++i;
            }
        }

        private static void EndConDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }
        
        private static IEnumerable<Tuple<Node, PrintData>> StartField(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var fld = (Field)node;
            if (!string.IsNullOrEmpty(fld.Name))
            {
                wr.Write("{0}: ", fld.Name);
            }

            if (fld.IsAny)
            {
                wr.Write("any ");
            }

            yield return new Tuple<Node, PrintData>(fld.Type, default(PrintData));
        }

        private static void EndField(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartUnnDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var udec = (UnnDecl)node;
            if (udec.Config != null && udec.Config.Settings.Count > 0)
            {
                wr.WriteLine();
                yield return new Tuple<Node, PrintData>(udec.Config, new PrintData(null, null, data.Indentation));
            }

            yield return new Tuple<Node, PrintData>(udec.Body, new PrintData(string.Format("{0} ::= ", udec.Name), ".\n", data.Indentation));
        }

        private static void EndUnnDecl(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartUnion(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var unn = (Union)node;
            int i = 0;
            foreach (var u in unn.Components)
            {
                yield return new Tuple<Node, PrintData>(u, new PrintData(null, i < unn.Components.Count - 1 ? " + " : null, 0));
                ++i;
            }
        }

        private static void EndUnion(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartEnum(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var enm = (Nodes.Enum)node;
            int i = 0;
            wr.Write("{ ");
            foreach (var e in enm.Elements)
            {
                yield return new Tuple<Node, PrintData>(e, new PrintData(null, i < enm.Elements.Count - 1 ? ", " : " }", 0));
                ++i;
            }
        }

        private static void EndEnum(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartRange(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var rr = (Range)node;
            wr.Write("{0}..{1}", rr.Lower, rr.Upper);
            yield break;
        }

        private static void EndRange(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartRule(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine();
            var r = (Rule)node;
            if (r.Config != null && r.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(r.Config, new PrintData(null, null, data.Indentation));
            }

            int i = 0;
            string terminator = r.Bodies.Count > 0 ? "\n" + data.GetIndentString() + "  :-\n" : ".\n";
            foreach (var h in r.Heads)
            {
                yield return new Tuple<Node, PrintData>(h, new PrintData(data.GetIndentString(), i < r.Heads.Count - 1 ? ",\n" : terminator, 0));
                ++i;
            }

            i = 0;
            foreach (var b in r.Bodies)
            {
                yield return new Tuple<Node, PrintData>(b, new PrintData(null, i < r.Bodies.Count - 1 ? ";\n" : ".\n", data.Indentation + 2));
                ++i;
            }
        }

        private static void EndRule(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartBody(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var body = (Body)node;
            int i = 0;
            foreach (var c in body.Constraints)
            {
                yield return new Tuple<Node, PrintData>(c, new PrintData(null, i < body.Constraints.Count - 1 ? ", " : null, 0));
                ++i;
            }
        }

        private static void EndBody(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartFind(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var pat = (Find)node;
            if (pat.Binding != null)
            {
                yield return new Tuple<Node, PrintData>(pat.Binding, new PrintData(null, " is ", 0));
            }

            yield return new Tuple<Node, PrintData>(pat.Match, default(PrintData));
        }

        private static void EndFind(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartModelFact(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var pat = (ModelFact)node;
            if (pat.Config != null && pat.Config.Settings.Count > 0)
            {
                wr.WriteLine();
                yield return new Tuple<Node, PrintData>(pat.Config, new PrintData(null, null, data.Indentation));
            }

            if (pat.Binding != null)
            {
                yield return new Tuple<Node, PrintData>(pat.Binding, new PrintData(data.GetIndentString(), " is ", 0));
                yield return new Tuple<Node, PrintData>(pat.Match, default(PrintData));
            }
            else
            {
                yield return new Tuple<Node, PrintData>(pat.Match, new PrintData(data.GetIndentString(), null, 0));
            }
        }

        private static void EndModelFact(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartFuncTerm(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var fnc = (FuncTerm)node;
            string opStr;
            var style = ASTQueries.OpStyleKind.Apply;
            if (fnc.Function is OpKind)
            {                
                opStr = ASTQueries.ASTSchema.Instance.ToString((OpKind)fnc.Function, out style);
            }
            else
            {
                opStr = ((Id)fnc.Function).Name;
            }

            if (fnc.Args.Count == 0)
            {
                wr.Write(string.Format("{0}()", opStr));
            }
            else if (fnc.Args.Count > 2 || style == ASTQueries.OpStyleKind.Apply)
            {
                wr.Write(string.Format("{0}(", opStr));
                int i = 0;
                foreach (var a in fnc.Args)
                {
                    yield return new Tuple<Node, PrintData>(a, new PrintData(null, i < fnc.Args.Count - 1 ? ", " : ")", 0));
                    ++i;
                }           
            }
            else if (fnc.Args.Count == 2)
            {
                Node arg1, arg2;
                using (var it = fnc.Args.GetEnumerator())
                {
                    it.MoveNext();
                    arg1 = it.Current;
                    it.MoveNext();
                    arg2 = it.Current;
                }

                if (arg1.NodeKind == NodeKind.FuncTerm &&
                    ((FuncTerm)arg1).Function is OpKind &&
                    ASTQueries.ASTSchema.Instance.NeedsParen((OpKind)fnc.Function, (OpKind)((FuncTerm)arg1).Function))
                {
                    yield return new Tuple<Node, PrintData>(
                        arg1,
                        new PrintData("(", string.Format(") {0} ", opStr), 0));
                }
                else
                {
                    yield return new Tuple<Node, PrintData>(
                        arg1,
                        new PrintData(null, string.Format(" {0} ", opStr), 0));
                }

                if (arg2.NodeKind == NodeKind.FuncTerm &&
                    ((FuncTerm)arg2).Function is OpKind &&
                    ASTQueries.ASTSchema.Instance.NeedsParen((OpKind)fnc.Function, (OpKind)((FuncTerm)arg2).Function))
                {
                    yield return new Tuple<Node, PrintData>(
                        arg2,
                        new PrintData("(", ")", 0));
                }
                else
                {
                    yield return new Tuple<Node, PrintData>(
                        arg2,
                        new PrintData(null, null, 0));
                }
            }
            else
            {
                Node arg1;
                using (var it = fnc.Args.GetEnumerator())
                {
                    it.MoveNext();
                    arg1 = it.Current;
                }

                if (arg1.NodeKind == NodeKind.FuncTerm &&
                    ((FuncTerm)arg1).Function is OpKind &&
                    ASTQueries.ASTSchema.Instance.NeedsParen((OpKind)fnc.Function, (OpKind)((FuncTerm)arg1).Function))
                {
                    yield return new Tuple<Node, PrintData>(
                        arg1,
                        new PrintData(string.Format("{0}(", ")"), opStr, 0));
                }
                else
                {
                    yield return new Tuple<Node, PrintData>(
                        arg1,
                        new PrintData(opStr, null, 0));
                }
            }
        }

        private static void EndFuncTerm(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartCompr(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var cmpr = (Compr)node;
            wr.Write("{ ");
            int i = 0;
            foreach (var h in cmpr.Heads)
            {
                yield return new Tuple<Node, PrintData>(
                    h,
                    new PrintData(null, i < cmpr.Heads.Count - 1 ? ", " : null, 0));
                ++i;
            }

            i = 0;
            foreach (var b in cmpr.Bodies)
            {
                yield return new Tuple<Node, PrintData>(
                    b,
                    new PrintData(i == 0 ? " | " : null, i < cmpr.Bodies.Count - 1 ? ", " : null, 0));
                ++i;
            }
        }

        private static void EndCompr(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.Write(" }");
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartRelConstr(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var rc = (RelConstr)node;
            var op = ASTQueries.ASTSchema.Instance.ToString(rc.Op);
            if (rc.Op == RelKind.No && rc.Arg1.NodeKind == NodeKind.Compr &&
                ((Compr)rc.Arg1).Heads.Count > 0 &&
                ((Compr)rc.Arg1).Bodies.Count == 1 &&
                ((Compr)rc.Arg1).Bodies.First<Body>().Constraints.Count == 1 &&
                ((Compr)rc.Arg1).Bodies.First<Body>().Constraints.First<Node>().NodeKind == NodeKind.Find)
            {
                wr.Write("{0} ", op);
                yield return new Tuple<Node, PrintData>(
                    ((Compr)rc.Arg1).Bodies.First<Body>().Constraints.First<Node>(), 
                    new PrintData(null, null, 0));
            }
            else if (rc.Arg2 == null)
            {
                wr.Write("{0} ", op);
                yield return new Tuple<Node, PrintData>(rc.Arg1, new PrintData(null, null, 0));
            }
            else
            {
                yield return new Tuple<Node, PrintData>(rc.Arg1, new PrintData(null, string.Format(" {0} ", op), 0));
                yield return new Tuple<Node, PrintData>(rc.Arg2, new PrintData(null, null, 0));
            }
        }

        private static void EndRelConstr(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartQuote(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.Write("`");
            var qt = (Quote)node;
            int i = 0;
            foreach (var q in qt.Contents)
            {
                if (q.NodeKind == NodeKind.QuoteRun)
                {
                    yield return new Tuple<Node, PrintData>(q, new PrintData(null, i < qt.Contents.Count - 1 ? null : "`", 0));
                }
                else
                {
                    yield return new Tuple<Node, PrintData>(q, new PrintData("$", i < qt.Contents.Count - 1 ? "$" : "$`", 0));
                }

                ++i;
            }           
        }

        private static void EndQuote(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartQuoteRun(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.Write(((QuoteRun)node).Text);
            yield break;
        }

        private static void EndQuoteRun(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartParam(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var prm = (Param)node;
            if (prm.Type.IsTypeTerm)
            {
                wr.Write("{0}: ", prm.Name);
            }

            yield return new Tuple<Node, PrintData>(prm.Type, default(PrintData));
        }

        private static void EndParam(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartContractItem(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var ci = (ContractItem)node;
            if (ci.Config != null && ci.Config.Settings.Count > 0)
            {
                wr.WriteLine();
                yield return new Tuple<Node, PrintData>(ci.Config, new PrintData(null, null, data.Indentation));
            }

            int i = 0;
            foreach (var s in ci.Specification)
            {
                yield return new Tuple<Node, PrintData>(
                    s, 
                    new PrintData(
                        i == 0 ? string.Format("{0} ", ASTQueries.ASTSchema.Instance.ToString(ci.ContractKind)) : null, 
                        i < ci.Specification.Count - 1 ? ";\n" : ".\n", 
                        data.Indentation));
                ++i;
            }
        }

        private static void EndContractItem(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartCardPair(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var cp = (CardPair)node;
            wr.Write("{0} ", cp.Cardinality);
            yield return new Tuple<Node, PrintData>(cp.TypeId, default(PrintData));
        }

        private static void EndCardPair(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartStep(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var st = (Step)node;
            if (data.NoIndent && st.Config != null && st.Config.Settings.Count > 0)
            {
                wr.WriteLine();
                yield return new Tuple<Node, PrintData>(st.Config, new PrintData(null, null, data.Indentation));
            }

            int i = 0;
            foreach (var l in st.Lhs)
            {
                yield return new Tuple<Node, PrintData>(
                    l, 
                    new PrintData(data.NoIndent && i == 0 ? data.GetIndentString() : null, i < st.Lhs.Count - 1 ? ", " : " =\n", 0));
                ++i;
            }

            yield return new Tuple<Node, PrintData>(st.Rhs, new PrintData(null, null, data.Indentation + 2));
        }

        private static void EndStep(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            wr.WriteLine(".");
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartUpdate(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var up = (Update)node;
            wr.Write(data.GetIndentString());
            int i = 0;
            foreach (var sd in up.States)
            {
                yield return new Tuple<Node, PrintData>(sd, new PrintData(null, i < up.States.Count - 1 ? ", " : " =\n", 0));
                ++i;
            }

            i = 0;
            foreach (var c in up.Choices)
            {
                yield return new Tuple<Node, PrintData>(c, new PrintData(null, i < up.Choices.Count - 1 ? ";\n" : ".\n", data.Indentation + 1));
                ++i;
            }
        }

        private static void EndUpdate(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartModApply(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var mp = (ModApply)node;
            yield return new Tuple<Node, PrintData>(mp.Module, new PrintData(null, mp.Args.Count == 0 ? "()" : "(", 0));
            int i = 0;
            foreach (var a in mp.Args)
            {
                yield return new Tuple<Node, PrintData>(a, new PrintData(null, i < mp.Args.Count - 1 ? ", " : ")", 0));
                ++i;
            }
        }

        private static void EndModApply(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }

        private static IEnumerable<Tuple<Node, PrintData>> StartProperty(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
            var prp = (Property)node;
            wr.WriteLine();
            if (prp.Config != null && prp.Config.Settings.Count > 0)
            {
                yield return new Tuple<Node, PrintData>(prp.Config, new PrintData(null, null, data.Indentation));
            }

            var propStr = string.Format("{0}property\n{1}{2} =\n{3}", 
                data.GetIndentString(), 
                PrintData.GetIndentString(data.Indentation + 1), 
                prp.Name,
                PrintData.GetIndentString(data.Indentation + 2));

            yield return new Tuple<Node, PrintData>(prp.Definition, new PrintData(propStr, ".\n", data.Indentation + 2, true));
        }

        private static void EndProperty(Node node, PrintData data, TextWriter wr, EnvParams envParams)
        {
        }
         
        private struct PrintData
        {
            private string prefix;
            private string suffix;
            private int indentation;
            private bool noIndent;

            public string Prefix
            {
                get { return prefix; }
            }

            public string Suffix
            {
                get { return suffix; }
            }

            public int Indentation
            {
                get { return indentation; }
            }

            public bool NoIndent
            {
                get { return noIndent; }
            }

            public static string GetIndentString(int n)
            {
                return n <= 0 ? string.Empty : new string(' ', 2 * n);            
            }

            public string GetIndentString()
            {
                return indentation <= 0 ? string.Empty : new string(' ', 2 * indentation);
            }

            public PrintData(string prefix, string suffix, int indentation, bool noIndent = false)
            {
                this.prefix = prefix;
                this.suffix = suffix;
                this.indentation = indentation;
                this.noIndent = noIndent;
            }
        }
    }
}
