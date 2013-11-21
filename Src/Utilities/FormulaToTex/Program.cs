namespace FormulaToTex
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    class Program
    {
        private const string FrmIndexFile = "index.4mllay";
        private const string FrmExt = ".4ml";
        private const string TexExt = ".tex";
        private const string OptionSnippet = "-snippet";
        private const string OptionCode = "-code";
        private const string OptionInitIndex = "-initindex";

        private const string TopMargin = "\\vspace{0.5\\baselineskip}";
        private const string CodeParam = "%%CODE%%";
        private const string CmntParam = "%%CMNT%%";
        private const string LineNumParam = "%%LINENUM%%";
        private const string TopMarginParam = "%%TOPMARGIN%%";
        private const string SuffixMangler = "_suffix";

        private const char CodeIndicator = ':';
        private const char CmntIndicator = ';';
        private const char SuffixIndicator = '#';
        private static readonly char[] Indicators = new char[] { CodeIndicator, CmntIndicator, SuffixIndicator };

        private const string CmntFormat =
@"\fcolorbox{formula-background-gray}{formula-background-gray}{
\begin{minipage}{\codewidthper\textwidth}%%TOPMARGIN%%
\color{formula-comment}{\footnotesize\textsf{%%CMNT%%}}
\end{minipage}}
";

        private const string CodeFormat =
@"\fcolorbox{formula-background-gray}{formula-background-gray}{
\begin{minipage}{\codewidthper\textwidth}%%TOPMARGIN%%
 {\tiny\texttt{%%LINENUM%%}}{\small\texttt{%%CODE%%}}
\end{minipage}}\vspace{-3pt}
";

        private static readonly string[] keywords = new string[]
        {
            "domain", "model", "transform", "system", "includes",
            "extends", "of", "returns", "at", "machine",
            "is", "no", "new", "fun", "inj", 
            "bij", "sur", "any", "ensures", "requires",
            "conforms", "some", "atleast", "atmost", "partial",
            "initially", "next", "property", "boot", "Real",
            "Integer", "Natural", "PosInteger", "NegInteger", "String",
            "Boolean", "and", "andAll", "count", "gcd",
            "gcdAll", "impl", "isSubstring", "lcm", "lcmAll",
            "max", "maxAll", "min", "minAll", "not",
            "or", "prod", "qtnt", "rflIsMember", "rflIsSubtype",
            "rflGetArgType", "rflGetArity", "strAfter", "strBefore", "strFind",
            "strGetAt", "strJoin", "strLength", "strLower", "strReverse",
            "strUpper", "sum", "sign", "toList", "toNatural",
            "toString", "toSymbol"
        };

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            if (args.Length != 2)
            {
                Console.WriteLine("FormulaToTex: ERROR - bad arguments");
                PrintUsage();
                Environment.ExitCode = 1;
                return;
            }

            bool isSnippet;
            if (args[0] == OptionSnippet)
            {
                isSnippet = true;
            }
            else if (args[0] == OptionCode)
            {
                isSnippet = false;
            }
            else if (args[0] == OptionInitIndex)
            {
                MkIndexFile(args[1]);
                return;
            }
            else
            {
                Console.WriteLine("FormulaToTex: ERROR - bad command switch {0}", args[0]);
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var source = new FileInfo(Path.Combine(Environment.CurrentDirectory, args[1]));
                if (!source.Exists)
                {
                    Console.WriteLine("FormulaToTex: ERROR - Input file {0} does not exist.", source.FullName);
                    Environment.ExitCode = 1;
                    return;
                }
                else if (source.Extension != FrmExt)
                {
                    Console.WriteLine("FormulaToTex: ERROR - Input file {0} has wrong extension; expected {1}.", source.FullName, FrmExt);
                    Environment.ExitCode = 1;
                    return;
                }

                var outputName = source.FullName.Substring(0, source.FullName.Length - FrmExt.Length) + TexExt;
                var suffixName = source.FullName.Substring(0, source.FullName.Length - FrmExt.Length) + SuffixMangler + TexExt;

                Console.WriteLine("FormulaToTex: INFO - trying to compile {0} to {1}.", source.FullName, outputName);
                using (var sr = new StreamReader(source.FullName))
                {
                    using (var sw = new StreamWriter(outputName))
                    {
                        using (var suffixsw = new StreamWriter(suffixName))
                        {
                            if (isSnippet)
                            {
                                WriteSnippet(sr, sw);
                            }
                            else
                            {
                                WriteCode(sr, sw, suffixsw);
                            }
                        }
                    }
                }

                int callNum;
                if (GetNextCallNumber(source.Directory, out callNum))
                {
                    var outInfo = new FileInfo(outputName);
                    var suffixInfo = new FileInfo(suffixName);

                    var outputLogName = source.FullName.Substring(0, source.FullName.Length - FrmExt.Length) + callNum.ToString() + TexExt;
                    var suffixLogName = source.FullName.Substring(0, source.FullName.Length - FrmExt.Length) + SuffixMangler + callNum.ToString() + TexExt;

                    outInfo.CopyTo(outputLogName, true);
                    suffixInfo.CopyTo(suffixLogName, true);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("FormulaToTex: ERROR - {0}", e.Message);
                Environment.ExitCode = 1;
                return;
            }
        }

        private static bool GetNextCallNumber(DirectoryInfo info, out int num)
        {
            try
            {
                var indices = info.GetFiles(FrmIndexFile, SearchOption.TopDirectoryOnly);
                if (indices.Length == 0)
                {
                    num = 0;
                    return false;
                }

                using (var sr = indices[0].OpenText())
                {
                    var line = sr.ReadLine().Trim();
                    if (!int.TryParse(line, out num))
                    {
                        return false;
                    }
                }

                ++num;
                using (var sw = indices[0].CreateText())
                {
                    sw.WriteLine(num);
                }

                return true;
            }
            catch
            {
                num = 0;
                return false;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
                "USAGE: FormulaToTex.exe [{0} | {1} | {2}] file.tex",
                OptionSnippet,
                OptionCode,
                OptionInitIndex);

            var optlen = Math.Max(OptionSnippet.Length, Math.Max(OptionCode.Length, OptionInitIndex.Length));
            Console.WriteLine(
                "{0}{1}: Render contents of a FormulaSnippet environment.",
                OptionSnippet,
                new string(' ', optlen - OptionSnippet.Length + 1));
            Console.WriteLine(
                "{0}{1}: Render contents of a FormulaCode or FormulaExample environment.",
                OptionCode,
                new string(' ', optlen - OptionCode.Length + 1));
            Console.WriteLine(
                "{0}{1}: Initialize {2} file for caching of rendered output.",
                OptionInitIndex,
                new string(' ', optlen - OptionInitIndex.Length + 1),
                FrmIndexFile);
        }

        private static void MkIndexFile(string inputFile)
        {
            try
            {
                var source = new FileInfo(Path.Combine(Environment.CurrentDirectory, inputFile));
                var outdir = source.Directory;
                if (!outdir.Exists)
                {
                    Console.WriteLine("FormulaToTex: ERROR - Cannot create index file {0}; dir {1} does not exist.", FrmIndexFile, outdir.FullName);
                    Environment.ExitCode = 1;
                    return;
                }

                using (var sw = new StreamWriter(Path.Combine(outdir.FullName, FrmIndexFile), false))
                {
                    sw.WriteLine(0);
                    sw.Flush();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("FormulaToTex: ERROR - Cannot create index file {0}; {1}.", FrmIndexFile, e.Message);
                Environment.ExitCode = 1;
            }
        }

        private static void WriteSnippet(StreamReader sr, StreamWriter sw)
        {
            while (!sr.EndOfStream)
            {
                sw.WriteLine("{{\\small\\texttt{{{0}}}}}", FormatLine(sr.ReadLine()));
            }
        }

        private static void WriteCode(StreamReader sr, StreamWriter sw, StreamWriter suffixsw)
        {
            string line;
            int lineNum = 0;
            int metaDataStart;
            bool isFirstOutput = true;
            var linkDefs = new List<string>();
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine();
                metaDataStart = line.IndexOfAny(Indicators);
                if (metaDataStart < 0)
                {
                    continue;
                }
                else if (line[metaDataStart] == CmntIndicator && metaDataStart + 1 < line.Length)
                {
                    sw.Write(
                           CmntFormat.
                             Replace(CmntParam, FormatLine(line.Substring(metaDataStart + 1), true)).
                             Replace(TopMarginParam, isFirstOutput ? TopMargin : ""));
                    isFirstOutput = false;
                }
                else if (line[metaDataStart] == SuffixIndicator && metaDataStart + 1 < line.Length)
                {
                    suffixsw.WriteLine(line.Substring(metaDataStart + 1));
                }
                else if (line[metaDataStart] == CodeIndicator)
                {
                    ++lineNum;
                    var lineStr = lineNum.ToString();
                    if (metaDataStart > 0)
                    {
                        var usrLabel = line.Substring(0, metaDataStart).Trim();
                        if (usrLabel.Length > 0)
                        {
                            lineStr = string.Format("\\hypertarget{{{0}}}{{{1}}}", usrLabel, lineStr);
                            linkDefs.Add(string.Format("\\gdef \\{0} {{\\hyperlink{{{0}}}{{{1}}}}}", usrLabel, lineNum));
                        }
                    }

                    if (lineNum < 10)
                    {
                        lineStr += ":~~";
                    }
                    else if (lineNum < 100)
                    {
                        lineStr += ":~";
                    }
                    else
                    {
                        lineStr += ":";
                    }


                    if (metaDataStart + 1 < line.Length)
                    {
                        sw.Write(
                            CodeFormat.
                              Replace(LineNumParam, lineStr).
                              Replace(TopMarginParam, isFirstOutput ? TopMargin : "").
                              Replace(CodeParam, FormatLine(line.Substring(metaDataStart + 1))));
                    }
                    else
                    {
                        sw.Write(
                            CodeFormat.
                              Replace(LineNumParam, lineStr).
                              Replace(TopMarginParam, isFirstOutput ? TopMargin : "").
                              Replace(CodeParam, "~"));
                    }

                    isFirstOutput = false;
                }                
            }

            foreach (var def in linkDefs)
            {
                sw.WriteLine(def);
            }
        }

        private static string FormatLine(string s, bool isComment = false)
        {
            var escaped = TexEscape(s);
            if (!isComment)
            {
                foreach (var k in keywords)
                {
                    escaped = Regex.Replace(
                                   escaped,
                                   string.Format("\\b{0}\\b", k),
                                   string.Format("\\textcolor{{formula-keyword}}{{{0}}}", k));
                }
            }

            return escaped;
        }

        private static string TexEscape(string s)
        {
            if (s == null)
            {
                return s;
            }

            char c;
            StringBuilder bld = new StringBuilder();
            for (int i = 0; i < s.Length; ++i)
            {
                c = s[i];

                switch (c)
                {
                    case '#':
                    case '$':
                    case '%':
                    case '&':
                    case '_':
                    case '{':
                    case '}':
                        bld.Append("{\\" + c + "}");
                        break;
                    case '\'':
                        bld.Append("{\\textquotesingle}");
                        break;
                    case '`':
                        bld.Append("{\\`{}}");
                        break;
                    case '\"':
                        bld.Append("{\\textquotedbl}");
                        break;
                    case ' ':
                        bld.Append('~');
                        break;
                    case '\t':
                        bld.Append("~~~");
                        break;
                    case '<':
                        bld.Append("{\\textless}");
                        break;
                    case '>':
                        bld.Append("{\\textgreater}");
                        break;
                    case '\\':
                        bld.Append("{\\textbackslash}");
                        break;
                    case '|':
                        bld.Append("{\\textbar}");
                        break;
                    case '~':
                        bld.Append("{\\~{}}");
                        break;
                    case '^':
                        bld.Append("{\\^{}}");
                        break;
                    default:
                        bld.Append(c);
                        break;
                }
            }

            return bld.ToString();
        }
    }
}
