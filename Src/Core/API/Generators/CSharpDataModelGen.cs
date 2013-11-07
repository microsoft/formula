namespace Microsoft.Formula.API.Generators
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Nodes;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Compiler;

    internal class CSharpDataModelGen
    {
        private static readonly Set<string> CSharpKeywords;
        private static readonly char[] DotDelim = new char[] { '.' };

        private const int IndentAmount = 3;
        private const string GroundTermClassName = "GroundTerm";
        private const string UserCnstKindName = "UserCnstKind";
        private const string TypeCnstKindName = "TypeCnstKind";

        private const string UserCnstNamesName = "UserCnstNames";
        private const string TypeCnstNamesName = "TypeCnstNames";
        private const string RootSuffix = "_Root";
        private const string ConstructorMapName = "ConstructorMap";

        public const string QuotationClassName = "Quotation";
        public const string UserCnstClassName = "UserCnst";
        public const string StringCnstClassName = "StringCnst";

        private SymbolTable table;
        private TextWriter writer;
        private GeneratorOptions options;
        private GenerateResult result;

        private string indentStr = string.Empty;
        private Set<string> argInterfaces = new Set<String>(string.CompareOrdinal);
        private Map<Symbol, Set<string>> inheritsMap = new Map<Symbol, Set<string>>(Symbol.Compare);
        private Map<UserSymbol, MinimalElement[]> defaultValueMap = new Map<UserSymbol, MinimalElement[]>(Symbol.Compare);

        private UserSymbol theTrueSymbol;
        private BaseSortSymb theRealSymbol;
        private BaseSortSymb theStringSymbol;

        static CSharpDataModelGen()
        {
            CSharpKeywords = new Set<string>(string.CompareOrdinal);
            CSharpKeywords.Add("abstract");
            CSharpKeywords.Add("as");
            CSharpKeywords.Add("base");
            CSharpKeywords.Add("bool");
            CSharpKeywords.Add("break");
            CSharpKeywords.Add("byte");
            CSharpKeywords.Add("case");
            CSharpKeywords.Add("catch");
            CSharpKeywords.Add("char");
            CSharpKeywords.Add("checked");
            CSharpKeywords.Add("class");
            CSharpKeywords.Add("const");
            CSharpKeywords.Add("continue");
            CSharpKeywords.Add("decimal");
            CSharpKeywords.Add("default");
            CSharpKeywords.Add("delegate");
            CSharpKeywords.Add("do");
            CSharpKeywords.Add("double");
            CSharpKeywords.Add("else");
            CSharpKeywords.Add("enum");
            CSharpKeywords.Add("event");
            CSharpKeywords.Add("explicit");
            CSharpKeywords.Add("extern");
            CSharpKeywords.Add("false");
            CSharpKeywords.Add("finally");
            CSharpKeywords.Add("fixed");
            CSharpKeywords.Add("float");
            CSharpKeywords.Add("for");
            CSharpKeywords.Add("foreach");
            CSharpKeywords.Add("goto");
            CSharpKeywords.Add("if");
            CSharpKeywords.Add("implicit");
            CSharpKeywords.Add("in");
            CSharpKeywords.Add("int");
            CSharpKeywords.Add("interface");
            CSharpKeywords.Add("internal");
            CSharpKeywords.Add("is");
            CSharpKeywords.Add("lock");
            CSharpKeywords.Add("long");
            CSharpKeywords.Add("namespace");
            CSharpKeywords.Add("new");
            CSharpKeywords.Add("null");
            CSharpKeywords.Add("object");
            CSharpKeywords.Add("operator");
            CSharpKeywords.Add("out");
            CSharpKeywords.Add("override");
            CSharpKeywords.Add("params");
            CSharpKeywords.Add("private");
            CSharpKeywords.Add("protected");
            CSharpKeywords.Add("public");
            CSharpKeywords.Add("readonly");
            CSharpKeywords.Add("ref");
            CSharpKeywords.Add("return");
            CSharpKeywords.Add("sbyte");
            CSharpKeywords.Add("sealed");
            CSharpKeywords.Add("short");
            CSharpKeywords.Add("sizeof");
            CSharpKeywords.Add("stackalloc");
            CSharpKeywords.Add("static");
            CSharpKeywords.Add("string");
            CSharpKeywords.Add("struct");
            CSharpKeywords.Add("switch");
            CSharpKeywords.Add("this");
            CSharpKeywords.Add("throw");
            CSharpKeywords.Add("true");
            CSharpKeywords.Add("try");
            CSharpKeywords.Add("typeof");
            CSharpKeywords.Add("uint");
            CSharpKeywords.Add("ulong");
            CSharpKeywords.Add("unchecked");
            CSharpKeywords.Add("unsafe");
            CSharpKeywords.Add("ushort");
            CSharpKeywords.Add("using");
            CSharpKeywords.Add("virtual");
            CSharpKeywords.Add("void");
            CSharpKeywords.Add("volatile");
            CSharpKeywords.Add("while");
            CSharpKeywords.Add("add");
            CSharpKeywords.Add("alias");
            CSharpKeywords.Add("ascending");
            CSharpKeywords.Add("async");
            CSharpKeywords.Add("await");
            CSharpKeywords.Add("descending");
            CSharpKeywords.Add("dynamic");
            CSharpKeywords.Add("from");
            CSharpKeywords.Add("get");
            CSharpKeywords.Add("global");
            CSharpKeywords.Add("group");
            CSharpKeywords.Add("into");
            CSharpKeywords.Add("join");
            CSharpKeywords.Add("let");
            CSharpKeywords.Add("orderby");
            CSharpKeywords.Add("partial");
            CSharpKeywords.Add("remove");
            CSharpKeywords.Add("select");
            CSharpKeywords.Add("set");
            CSharpKeywords.Add("value");
            CSharpKeywords.Add("var");
            CSharpKeywords.Add("where");
            CSharpKeywords.Add("yield");            
        }

        public CSharpDataModelGen(
            SymbolTable table, 
            TextWriter writer, 
            GeneratorOptions options,
            GenerateResult result)
        {
            Contract.Requires(table != null && writer != null && options != null && result != null);

            this.table = table;
            this.writer = writer;
            this.options = options;
            this.result = result;

            UserSymbol other;
            theTrueSymbol = table.Resolve(ASTQueries.ASTSchema.Instance.ConstNameTrue, out other);
            Contract.Assert(theTrueSymbol != null && other == null);
            theRealSymbol = table.GetSortSymbol(BaseSortKind.Real);
            theStringSymbol = table.GetSortSymbol(BaseSortKind.String);
        }

        public void Generate()
        {
            BuildInheritsMaps(table.Root);

            var indent = 0;
            OpenNamespace(options.Namespace, ref indent);
            PrintUsings(indent);
            OpenStaticClass(EscapeIdentifier(options.Classname) + RootSuffix, ref indent);
            PrintDataModel(ref indent);

            CloseBlock(ref indent, false);
            CloseNamespace(options.Namespace, ref indent);
        }

        private void OpenStaticClass(string name, ref int indent)
        {
            WriteLine(string.Format("public static partial class {0}", EscapeIdentifier(name)), indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenClass(string name, ref int indent, IEnumerable<string> inherits = null, bool isAbstract = false)
        {
            string header = isAbstract ? "public abstract partial class" : "public partial class";
            if (inherits != null && !inherits.IsEmpty<string>())
            {
                string prev = null;
                WriteLine(string.Format("{0} {1} :", header, EscapeIdentifier(name)), indent);
                foreach (var inh in inherits)
                {
                    if (prev != null)
                    {
                        WriteLine(EscapeIdentifier(prev) + ",", indent + 1);
                    }

                    prev = inh;
                }

                WriteLine(EscapeIdentifier(prev), indent + 1);
            }
            else
            {
                WriteLine(string.Format("{0} {1}", header, EscapeIdentifier(name)), indent);
            }

            WriteLine("{", indent);
            ++indent;
        }

        private void OpenInterface(string name, ref int indent, IEnumerable<string> inherits = null)
        {
            if (inherits != null && !inherits.IsEmpty<string>())
            {
                string prev = null;
                WriteLine(string.Format("public interface {0} :", EscapeIdentifier(name)), indent);
                foreach (var inh in inherits)
                {
                    if (prev != null)
                    {
                        WriteLine(EscapeIdentifier(prev) + ",", indent + 1);
                    }

                    prev = inh;
                }

                WriteLine(EscapeIdentifier(prev), indent + 1);
            }
            else
            {
                WriteLine(string.Format("public interface {0}", EscapeIdentifier(name)), indent);
            }

            WriteLine("{", indent);
            ++indent;
        }

        private void OpenFunction(string preParameters, ref int indent, IEnumerable<string> parameters = null, string typeConstraint = null)
        {
            if (parameters != null && !parameters.IsEmpty<string>())
            {
                string prev = null;
                Write(preParameters + "(", indent);
                foreach (var p in parameters)
                {
                    if (prev != null)
                    {
                        Write(prev + ", ", 0);
                    }

                    prev = p;
                }

                WriteLine(prev + ")", 0);
            }
            else
            {
                WriteLine(preParameters + "()", indent);
            }

            if (!string.IsNullOrWhiteSpace(typeConstraint))
            {
                WriteLine("where " + typeConstraint, indent + 1);
            }

            WriteLine("{", indent);
            ++indent;
        }

        private void OpenLambda(ref int indent, IEnumerable<string> parameters = null)
        {
            if (parameters != null && !parameters.IsEmpty<string>())
            {
                string prev = null;
                Write("(", indent);
                foreach (var p in parameters)
                {
                    if (prev != null)
                    {
                        Write(prev + ", ", 0);
                    }

                    prev = p;
                }

                WriteLine(prev + ") =>", 0);
            }
            else
            {
                WriteLine("() =>", indent);
            }

            WriteLine("{", indent);
            ++indent;
        }

        private void OpenITE(string condition, bool isElse, ref int indent)
        {
            if (isElse)
            {
                if (string.IsNullOrWhiteSpace(condition))
                {
                    WriteLine("else", indent);
                }
                else
                {
                    WriteLine(string.Format("else if ({0})", condition), indent);
                }
            }
            else
            {
                WriteLine(string.Format("if ({0})", condition), indent);
            }

            WriteLine("{", indent);
            ++indent;
        }

        private void CloseBlock(ref int indent, bool addBlank = true)
        {
            --indent;
            if (addBlank)
            {
                WriteLine("}\n", indent);
            }
            else 
            {
                WriteLine("}", indent);
            }
        }

        private void OpenNamespace(string name, ref int indent)
        {
            if (name == null)
            {
                return;
            }

            WriteLine(string.Format("namespace {0}", EscapeIdentifier(name)), indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenEnum(string name, ref int indent)
        {
            WriteLine(string.Format("public enum {0}", EscapeIdentifier(name)), indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenProperty(string signature, ref int indent)
        {
            WriteLine(signature, indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenTry(ref int indent)
        {
            WriteLine("try", indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenFinally(ref int indent)
        {
            WriteLine("finally", indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenGetter(ref int indent)
        {
            WriteLine("get", indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenSetter(ref int indent)
        {
            WriteLine("set", indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void OpenSwitch(string test, ref int indent)
        {
            WriteLine(string.Format("switch ({0})", test), indent);
            WriteLine("{", indent);
            ++indent;
        }

        private void CloseNamespace(string name, ref int indent)
        {
            if (name == null)
            {
                return;
            }

            --indent;
            WriteLine("}", indent);
        }

        private void PrintDataModel(ref int indent)
        {
            string realName, stringName;
            ASTQueries.ASTSchema.Instance.TryGetSortName(BaseSortKind.Real, out realName);
            ASTQueries.ASTSchema.Instance.TryGetSortName(BaseSortKind.String, out stringName);
            realName += "Cnst";
            stringName += "Cnst";

            WriteLine(
                string.Format(
                    "private static readonly Dictionary<string, Func<{0}[], {0}>> {1} = new Dictionary<string, Func<{0}[], {0}>>();",
                    typeof(ICSharpTerm).Name,
                    ConstructorMapName),
                indent);

            WriteLine(string.Format("static {0}()", EscapeIdentifier(options.Classname) + RootSuffix), indent);
            WriteLine("{", indent);
            ++indent;
            PrintConstructMap(table.Root, indent);
            CloseBlock(ref indent);

            PrintUserConstantEnums(table.Root, indent);
            PrintUserConstantNames(table.Root, indent);

            WriteLine("public static string Namespace { get { return \"\"; } }", indent);
            WriteLine();

            OpenFunction(
                "public static bool CreateObjectGraph",
                ref indent,
                new string[] { 
                    typeof(Env).Name + " env",
                    typeof(ProgramName).Name + " progName",
                    "string modelName",
                    string.Format("out {0}<{1}> task", typeof(Task).Name, typeof(ObjectGraphResult).Name) });
            WriteLine("Contract.Requires(env != null && progName != null && !string.IsNullOrEmpty(modelName));", indent);
            WriteLine(
                string.Format(
                    "return env.CreateObjectGraph(progName, modelName, MkNumeric, MkString, {0}, out task);",
                    ConstructorMapName),
                indent);
            CloseBlock(ref indent);

            OpenFunction(string.Format("public static {0} MkNumeric", realName), ref indent, new string[] { "int val" });
            WriteLine(string.Format("var n = new {0}();", realName), indent);
            WriteLine("n.Value = new Rational(val);", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            OpenFunction(string.Format("public static {0} MkNumeric", realName), ref indent, new string[] { "double val" });
            WriteLine(string.Format("var n = new {0}();", realName), indent);
            WriteLine("n.Value = new Rational(val);", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            OpenFunction(string.Format("public static {0} MkNumeric", realName), ref indent, new string[] { "Rational val" });
            WriteLine(string.Format("var n = new {0}();", realName), indent);
            WriteLine("n.Value = val;", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            OpenFunction(string.Format("public static {0} MkString", stringName), ref indent, new string[] { "string val = default(string)" });
            WriteLine(string.Format("var n = new {0}();", stringName), indent);
            WriteLine("n.Value = val;", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            OpenFunction(string.Format("public static {0} MkQuotation", QuotationClassName), ref indent, new string[] { "string val = default(string)" });
            WriteLine(string.Format("var n = new {0}();", QuotationClassName), indent);
            WriteLine("n.Value = val;", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            PrintMkUserCnst(table.Root, indent);
            PrintMkConstructors(table.Root, indent);

            //// *********************** GroundTerm ***********************
            OpenClass(GroundTermClassName, ref indent, new string[] { typeof(ICSharpTerm).Name }, true);
            if (options.IsThreadSafeCode)
            {
                WriteLine("protected SpinLock rwLock = new SpinLock();", indent);
            }

            WriteLine("public abstract int Arity { get; }", indent);
            WriteLine("public abstract object Symbol { get; }", indent);
            WriteLine(string.Format("public abstract {0} this[int index] {{ get; }}", typeof(ICSharpTerm).Name), indent);

            if (options.IsThreadSafeCode)
            {
                OpenFunction("protected T Get<T>", ref indent, new string[] { "Func<T> getter" });
                WriteLine("bool gotLock = false;", indent);
                OpenTry(ref indent);
                WriteLine("rwLock.Enter(ref gotLock);", indent);
                WriteLine("return getter();", indent);
                CloseBlock(ref indent, false);
                OpenFinally(ref indent);
                OpenITE("gotLock", false, ref indent);
                WriteLine("rwLock.Exit();", indent);
                CloseBlock(ref indent, false);
                CloseBlock(ref indent, false);
                CloseBlock(ref indent);

                OpenFunction("protected void Set", ref indent, new string[] { "Action setter" });
                WriteLine("bool gotLock = false;", indent);
                OpenTry(ref indent);
                WriteLine("rwLock.Enter(ref gotLock);", indent);
                WriteLine("setter();", indent);
                CloseBlock(ref indent, false);
                OpenFinally(ref indent);
                OpenITE("gotLock", false, ref indent);
                WriteLine("rwLock.Exit();", indent);
                CloseBlock(ref indent, false);
                CloseBlock(ref indent, false);
                CloseBlock(ref indent, false);
            }

            CloseBlock(ref indent);
            //// *********************** END ***********************            

            PrintConstructors(table.Root, indent);

            //// *********************** RealCnst ***********************
            OpenClass(realName, ref indent, GetInherits(theRealSymbol).Prepend<string>(GroundTermClassName));
            if (options.IsThreadSafeCode)
            {
                WriteLine("Rational val = default(Rational);", indent);
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return Get<Rational>(() => val); } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public Rational Value { get { return Get<Rational>(() => val); } set { Set(() => { val = value; }); } }", indent);
            }
            else
            {
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return Value; } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public Rational Value { get; set; }", indent);
            }

            CloseBlock(ref indent);
            //// *********************** END ***********************            

            //// *********************** StringCnst ***********************
            OpenClass(stringName, ref indent, GetInherits(theStringSymbol).Prepend<string>(GroundTermClassName));
            if (options.IsThreadSafeCode)
            {
                WriteLine("string val = default(string);", indent);
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return Get<string>(() => val); } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public string Value { get { return Get<string>(() => val); } set { Set(() => { val = value; }); } }", indent);
            }
            else
            {
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return Value; } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public string Value { get; set; }", indent);
            }

            CloseBlock(ref indent);
            //// *********************** END ***********************            

            //// *********************** Quotation ***********************
            OpenClass(QuotationClassName, ref indent, argInterfaces.Prepend<string>(GroundTermClassName));
            WriteLine("string val = string.Empty;", indent);
            if (options.IsThreadSafeCode)
            {
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return Get<string>(() => string.Format(\"`{0}`\", val)); } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public string Value { get { return Get<string>(() => val); } set { Set(() => { val = value; }); } }", indent);
            }
            else
            {
                WriteLine("public override int Arity { get { return 0; } }", indent);
                WriteLine("public override object Symbol { get { return string.Format(\"`{0}`\", val); } }", indent);
                WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
                WriteLine("public string Value { get; set; }", indent);
            }

            CloseBlock(ref indent);
            //// *********************** END ***********************       

            //// *********************** UserCnst ***********************
            OpenClass(UserCnstClassName, ref indent, GetInherits(theTrueSymbol).Prepend<string>(GroundTermClassName));
            WriteLine(string.Format(
                        "private object val = {0}.{1}.{2};",
                        EscapeIdentifier(options.Classname) + RootSuffix,
                        UserCnstKindName,
                        ASTQueries.ASTSchema.Instance.ConstNameFalse), 
                      indent);
            WriteLine("public override int Arity { get { return 0; } }", indent);

            if (options.IsThreadSafeCode)
            {
                WriteLine("public override object Symbol { get { return Get<object>(() => ToSymbol(val)); } }", indent);
            }
            else
            {
                WriteLine("public override object Symbol { get { return Value; } }", indent);
            }

            WriteLine(string.Format("public override {0} this[int index] {{ get {{ throw new InvalidOperationException(); }} }}", typeof(ICSharpTerm).Name), indent);
            OpenProperty("public object Value", ref indent);
            OpenGetter(ref indent);
            if (options.IsThreadSafeCode)
            {
                WriteLine("return Get<object>(() => val);", indent);
            }
            else
            {
                WriteLine("return val;", indent);
            }

            CloseBlock(ref indent);

            OpenSetter(ref indent);
            OpenITE("!ValidateType(value)", false, ref indent);
            WriteLine("throw new InvalidOperationException();", indent);
            CloseBlock(ref indent);
            if (options.IsThreadSafeCode)
            {
                WriteLine("Set(() => { val = value; });", indent);
            }
            else
            {
                WriteLine("val = value;", indent);
            }

            CloseBlock(ref indent, false);
            CloseBlock(ref indent);
            
            OpenFunction("private static bool ValidateType", ref indent, new string[] { "object o" });
            OpenITE("o == null", false, ref indent);
            WriteLine("return true;", indent);
            CloseBlock(ref indent, false);
            PrintUserConstTests(table.Root, indent);
            OpenITE(null, true, ref indent);
            WriteLine("return false;", indent);
            CloseBlock(ref indent, false);
            CloseBlock(ref indent);

            OpenFunction("private static string ToSymbol", ref indent, new string[] { "object o" });
            OpenITE("o == null", false, ref indent);
            WriteLine("return null;", indent);
            CloseBlock(ref indent, false);
            PrintUserConstToSymbol(table.Root, indent);
            OpenITE(null, true, ref indent);
            WriteLine("throw new InvalidOperationException();", indent);
            CloseBlock(ref indent, false);
            CloseBlock(ref indent, false);
            CloseBlock(ref indent);
            //// *********************** END ***********************      

            foreach (var n in table.Root.Children)
            {
                PrintDataModel(n, indent);
            }
        }

        private void PrintUserConstTests(Namespace n, int indent)
        {
            var enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                UserCnstKindName);

            OpenITE("o is " + enumName, true, ref indent);
            WriteLine("return true;", indent);
            CloseBlock(ref indent, false);

            enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                TypeCnstKindName);

            OpenITE("o is " + enumName, true, ref indent);
            WriteLine("return true;", indent);
            CloseBlock(ref indent, false);

            foreach (var m in n.Children)
            {
                PrintUserConstTests(m, indent);
            }
        }

        private void PrintDataModel(Namespace n, int indent)
        {
            OpenStaticClass(EscapeIdentifier(n.Name), ref indent);
            PrintUserConstantEnums(n, indent);
            PrintUserConstantNames(n, indent);

            WriteLine(string.Format("public static string Namespace {{ get {{ return \"{0}\"; }} }}", n.Name), indent);

            PrintMkConstructors(n, indent);
            PrintConstructors(n, indent);

            foreach (var m in n.Children)
            {
                PrintDataModel(m, indent);
            }

            CloseBlock(ref indent);
        }

        private void PrintUserConstToSymbol(Namespace n, int indent)
        {
            var enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                UserCnstKindName);

            var arrayName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                UserCnstNamesName);

            OpenITE("o is " + enumName, true, ref indent);
            WriteLine(string.Format("return {0}[(int)o];", arrayName), indent);
            CloseBlock(ref indent, false);

            enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                TypeCnstKindName);

            arrayName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                TypeCnstNamesName);

            OpenITE("o is " + enumName, true, ref indent);
            WriteLine(string.Format("return {0}[(int)o];", arrayName), indent);
            CloseBlock(ref indent, false);

            foreach (var m in n.Children)
            {
                PrintUserConstToSymbol(m, indent);
            }
        }

        private void PrintMkUserCnst(Namespace n, int indent)
        {
            var enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                UserCnstKindName);

            OpenFunction(string.Format("public static {0} MkUserCnst", UserCnstClassName), ref indent, new string[] { enumName + " val" });
            WriteLine(string.Format("var n = new {0}();", UserCnstClassName), indent);
            WriteLine("n.Value = val;", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            enumName = string.Format("{0}.{1}{2}",
                EscapeIdentifier(options.Classname) + RootSuffix,
                string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                TypeCnstKindName);

            OpenFunction(string.Format("public static {0} MkUserCnst", UserCnstClassName), ref indent, new string[] { enumName + " val" });
            WriteLine(string.Format("var n = new {0}();", UserCnstClassName), indent);
            WriteLine("n.Value = val;", indent);
            WriteLine("return n;", indent);
            CloseBlock(ref indent);

            foreach (var m in n.Children)
            {
                PrintMkUserCnst(m, indent);
            }
        }

        private void PrintConstructors(Namespace n, int indent)
        {
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew)
                {
                    continue;
                }

                PrintConstructor((UserSymbol)s, indent);
            }
        }

        private string MkDefault(UserSymbol us, int pos)
        {
            ComputeMinElement(us, new Set<UserSymbol>(Symbol.Compare));
            var minArgs = defaultValueMap[us];
            var val = minArgs[pos].Value;

            if (val is string)
            {
                return string.Format("MkString({0});", ASTQueries.ASTSchema.Instance.Encode((string)val));
            }
            else if (val is Rational)
            {
                return string.Format(
                    "MkNumeric(new Rational(BigInteger.Parse(\"{0}\"), BigInteger.Parse(\"{1}\")));",
                    ((Rational)val).Numerator,
                    ((Rational)val).Denominator);
            }
            else if (val is UserCnstSymb)
            {
                var uc = (UserCnstSymb)val;
                if (uc.IsTypeConstant)
                {
                    return string.Format(
                        "MkUserCnst({0}.{1}{2}.{3});",
                        EscapeIdentifier(options.Classname) + RootSuffix,
                        string.IsNullOrWhiteSpace(uc.Namespace.FullName) ? string.Empty : EscapeIdentifier(uc.Namespace.FullName) + ".",
                        TypeCnstKindName,
                        EscapeIdentifier(uc.Name));
                }
                else
                {
                    return string.Format(
                        "MkUserCnst({0}.{1}{2}.{3});",
                        EscapeIdentifier(options.Classname) + RootSuffix,
                        string.IsNullOrWhiteSpace(uc.Namespace.FullName) ? string.Empty : EscapeIdentifier(uc.Namespace.FullName) + ".",
                        UserCnstKindName,
                        EscapeIdentifier(uc.Name));
                }
            }
            else
            {
                return string.Format("new {0}();", GetClassName((UserSymbol)val));
            }
        }

        private void PrintConstructMap(Namespace n, int indent)
        {
            UserCnstSymb uc;
            string enumElement;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb && s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && s.IsDerivedConstant)
                {
                    continue;
                }
                else if (s.IsVariable || (s.Kind == SymbolKind.UserCnstSymb && ((UserCnstSymb)s).IsSymbolicConstant))
                {
                    continue;
                }

                if (s.Arity > 0)
                {
                    var entry = string.Format(
                        "{0}.Add(\"{1}\", args => {2}.{3}{4}(",
                        ConstructorMapName,
                        s.FullName,
                        EscapeIdentifier(options.Classname) + RootSuffix,
                        string.IsNullOrEmpty(s.Namespace.Name) ? string.Empty : (EscapeIdentifier(s.Namespace.Name) + "."),
                        EscapeIdentifier("Mk" + s.Name));

                    for (int i = 0; i < s.Arity; ++i)
                    {
                        if (i < s.Arity - 1)
                        {
                            entry += string.Format("({0})args[{1}], ", GetIArgTypeName(s, i), i);
                        }
                        else
                        {
                            entry += string.Format("({0})args[{1}]));", GetIArgTypeName(s, i), i);
                        }
                    }

                    WriteLine(entry, indent);
                }
                else
                {
                    uc = (UserCnstSymb)s;
                    enumElement = string.Format("{0}.{1}{2}.{3}",
                        EscapeIdentifier(options.Classname) + RootSuffix,
                        string.IsNullOrWhiteSpace(n.FullName) ? string.Empty : EscapeIdentifier(n.FullName) + ".",
                        uc.IsTypeConstant ? TypeCnstKindName : UserCnstKindName,
                        EscapeIdentifier(s.Name));

                    WriteLine(
                        string.Format(
                            "{0}.Add(\"{1}\", args => MkUserCnst({2}));",
                            ConstructorMapName,
                            s.FullName,
                            enumElement),
                        indent);
                }
            }

            foreach (var m in n.Children)
            {
                PrintConstructMap(m, indent);
            }
        }

        private void PrintMkConstructors(Namespace n, int indent)
        {
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew)
                {
                    continue;
                }

                var args = new string[s.Arity];
                IEnumerable<Field> fields = null;
                switch (s.Kind)
                {
                    case SymbolKind.ConSymb:
                        fields = ((AST<ConDecl>)s.Definitions.First<AST<Node>>()).Node.Fields;
                        break;
                    case SymbolKind.MapSymb:
                        fields = ((AST<MapDecl>)s.Definitions.First<AST<Node>>()).Node.Dom.Concat<Field>(
                                  ((AST<MapDecl>)s.Definitions.First<AST<Node>>()).Node.Cod);
                        break;
                    default:
                        throw new NotImplementedException();
                }

                int ndex = 0;
                foreach (var f in fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name))
                    {
                        args[ndex] = string.Format("{0} arg_{1} = null", GetIArgTypeName(s, ndex), ndex);
                    }
                    else
                    {
                        args[ndex] = string.Format("{0} {1} = null", GetIArgTypeName(s, ndex), EscapeIdentifier(f.Name));
                    }

                    ++ndex;
                }

                OpenFunction(
                    string.Format("public static {0} {1}", GetClassName(s), EscapeIdentifier("Mk" + s.Name)), 
                    ref indent,
                    args);

                WriteLine(string.Format("var _n_ = new {0}();", GetClassName(s)), indent);

                ndex = 0;
                foreach (var f in fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name))
                    {
                        OpenITE(string.Format("arg_{0} != null", ndex), false, ref indent);
                        WriteLine(string.Format("_n_._{0} = arg_{0};", ndex), indent);
                        CloseBlock(ref indent);
                    }
                    else
                    {
                        OpenITE(string.Format("{0} != null", EscapeIdentifier(f.Name)), false, ref indent);
                        WriteLine(string.Format("_n_.{0} = {0};", EscapeIdentifier(f.Name)), indent);
                        CloseBlock(ref indent);
                    }

                    ++ndex;
                }

                WriteLine("return _n_;", indent);
                CloseBlock(ref indent);
            }
        }
      
        private void PrintConstructor(UserSymbol s, int indent)
        {
            for (int i = 0; i < s.Arity; ++i)
            {
                OpenInterface(GetIArgTypeName(s, i, false), ref indent, new string[] { typeof(ICSharpTerm).Name });
                CloseBlock(ref indent);
            }

            OpenClass(s.Name, ref indent, GetInherits(s).Prepend<string>(GroundTermClassName));
            for (int i = 0; i < s.Arity; ++i)
            {
                WriteLine(string.Format("private {0} _{1}_val = {2}", GetIArgTypeName(s, i), i, MkDefault(s, i)), indent);
            }

            WriteLine();
            if (options.IsThreadSafeCode)
            {
                for (int i = 0; i < s.Arity; ++i)
                {
                    OpenProperty(string.Format("public {0} _{1}", GetIArgTypeName(s, i), i), ref indent);
                    OpenGetter(ref indent);
                    WriteLine(string.Format("Contract.Ensures(_{0}_val != null);", i), indent);
                    WriteLine(string.Format("return Get<{0}>(() => _{1}_val);", GetIArgTypeName(s, i), i), indent);
                    CloseBlock(ref indent);

                    OpenSetter(ref indent);
                    WriteLine(string.Format("Contract.Requires(value != null);", i), indent);
                    WriteLine(string.Format("Set(() => {{ _{0}_val = value; }});", i), indent);
                    CloseBlock(ref indent, false);
                    CloseBlock(ref indent);
                }
            }
            else
            {
                for (int i = 0; i < s.Arity; ++i)
                {
                    OpenProperty(string.Format("public {0} _{1}", GetIArgTypeName(s, i), i), ref indent);
                    OpenGetter(ref indent);
                    WriteLine(string.Format("Contract.Ensures(_{0}_val != null);", i), indent);
                    WriteLine(string.Format("return _{0}_val;", i), indent);
                    CloseBlock(ref indent);

                    OpenSetter(ref indent);
                    WriteLine(string.Format("Contract.Requires(value != null);", i), indent);
                    WriteLine(string.Format("_{0}_val = value;", i), indent);
                    CloseBlock(ref indent, false);
                    CloseBlock(ref indent);
                }
            }
          
            WriteLine();
            IEnumerable<Field> fields = null;
            switch (s.Kind)
            {
                case SymbolKind.ConSymb:
                    fields = ((AST<ConDecl>)s.Definitions.First<AST<Node>>()).Node.Fields;
                    break;
                case SymbolKind.MapSymb:
                    fields = ((AST<MapDecl>)s.Definitions.First<AST<Node>>()).Node.Dom.Concat<Field>(
                              ((AST<MapDecl>)s.Definitions.First<AST<Node>>()).Node.Cod);
                    break;
                default:
                    throw new NotImplementedException();
            }

            int ndex = 0;            
            if (options.IsThreadSafeCode)
            {
                foreach (var f in fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name))
                    {
                        ++ndex;
                        continue;
                    }

                    OpenProperty(string.Format("public {0} {1}", GetIArgTypeName(s, ndex), EscapeIdentifier(f.Name)), ref indent);
                    OpenGetter(ref indent);
                    WriteLine(string.Format("Contract.Ensures(_{0}_val != null);", ndex), indent);
                    WriteLine(string.Format("return Get<{0}>(() => _{1}_val);", GetIArgTypeName(s, ndex), ndex), indent);
                    CloseBlock(ref indent);

                    OpenSetter(ref indent);
                    WriteLine(string.Format("Contract.Requires(value != null);", ndex), indent);
                    WriteLine(string.Format("Set(() => {{ _{0}_val = value; }});", ndex), indent);
                    CloseBlock(ref indent, false);
                    CloseBlock(ref indent);

                    ++ndex;
                }
            }
            else
            {
                foreach (var f in fields)
                {
                    if (string.IsNullOrWhiteSpace(f.Name))
                    {
                        ++ndex;
                        continue;
                    }

                    OpenProperty(string.Format("public {0} {1}", GetIArgTypeName(s, ndex), EscapeIdentifier(f.Name)), ref indent);
                    OpenGetter(ref indent);
                    WriteLine(string.Format("Contract.Ensures(_{0}_val != null);", ndex), indent);
                    WriteLine(string.Format("return _{0}_val;", ndex), indent);
                    CloseBlock(ref indent);

                    OpenSetter(ref indent);
                    WriteLine(string.Format("Contract.Requires(value != null);", ndex), indent);
                    WriteLine(string.Format("_{0}_val = value;", ndex), indent);
                    CloseBlock(ref indent, false);
                    CloseBlock(ref indent);

                    ++ndex;
                }
            }

            WriteLine(string.Format("public override int Arity {{ get {{ return {0}; }} }}", s.Arity), indent);
            WriteLine(string.Format("public override object Symbol {{ get {{ return \"{0}\"; }} }}", s.FullName), indent);
                
            WriteLine(string.Format("public override {0} this[int index]", typeof(ICSharpTerm).Name), indent);
            WriteLine("{", indent);
            ++indent;
            OpenGetter(ref indent);
            if (options.IsThreadSafeCode)
            {
                WriteLine(string.Format("return Get<{0}>(", typeof(ICSharpTerm).Name), indent);
                ++indent;
                OpenLambda(ref indent);
                
            }

            OpenSwitch("index", ref indent);
            for (int i = 0; i < s.Arity; ++i)
            {
                WriteLine(string.Format("case {0}:", i), indent);
                WriteLine(string.Format("return _{0}_val;", i), indent + 1);
            }

            WriteLine("default:", indent);
            WriteLine("throw new InvalidOperationException();", indent + 1);
            CloseBlock(ref indent, false);

            if (options.IsThreadSafeCode)
            {
                --indent;
                WriteLine("}", indent);
                --indent;
                WriteLine(");", indent);
            }

            CloseBlock(ref indent, false);
            CloseBlock(ref indent, false);
            CloseBlock(ref indent);
        }

        private void BuildInheritsMaps(Namespace n)
        {
            UserSymbol us;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.ConSymb && s.Kind != SymbolKind.MapSymb)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && s.Kind == SymbolKind.ConSymb && !((ConSymb)s).IsNew)
                {
                    continue;
                }

                us = (UserSymbol)s;
                for (int i = 0; i < us.Arity; ++i)
                {
                    var argType = GetIArgTypeName(us, i);
                    foreach (var e in us.CanonicalForm[i].NonRangeMembers)
                    {
                        AddInherits(e, argType);
                    }

                    if (!us.CanonicalForm[i].RangeMembers.IsEmpty<KeyValuePair<BigInteger, BigInteger>>())
                    {
                        AddInherits(theRealSymbol, argType);
                    }
                }
            }

            foreach (var m in n.Children)
            {
                BuildInheritsMaps(m);
            }
        }

        private void AddInherits(Symbol s, string iname)
        {
            Symbol mapSymb = null;
            switch (s.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                    {
                        var bc = (BaseCnstSymb)s;
                        switch (bc.CnstKind)
                        {
                            case CnstKind.Numeric:
                                mapSymb = theRealSymbol;
                                break;
                            case CnstKind.String:
                                mapSymb = theStringSymbol;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    }
                case SymbolKind.BaseSortSymb:
                    {
                        var bs = (BaseSortSymb)s;
                        switch (bs.SortKind)
                        {
                            case BaseSortKind.Integer:
                            case BaseSortKind.Natural:
                            case BaseSortKind.NegInteger:
                            case BaseSortKind.PosInteger:
                            case BaseSortKind.Real:
                                mapSymb = theRealSymbol;
                                break;
                            case BaseSortKind.String:
                                mapSymb = theStringSymbol;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    }
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    mapSymb = s;
                    break;
                case SymbolKind.UserCnstSymb:
                    mapSymb = theTrueSymbol;
                    break;
                case SymbolKind.UserSortSymb:
                    mapSymb = ((UserSortSymb)s).DataSymbol;
                    break;
                default:
                    throw new NotImplementedException();
            }

            Set<string> inheritSet;
            if (!inheritsMap.TryFindValue(mapSymb, out inheritSet))
            {
                inheritSet = new Set<string>(string.CompareOrdinal);
                inheritsMap.Add(mapSymb, inheritSet);
            }

            argInterfaces.Add(iname);
            inheritSet.Add(iname);
        }

        private string GetIArgTypeName(UserSymbol s, int arg, bool qualified = true)
        {
            if (qualified)
            {
                return EscapeIdentifier(options.Classname) + 
                        RootSuffix + "." + 
                        (string.IsNullOrEmpty(s.Namespace.FullName) ? "" : (EscapeIdentifier(s.Namespace.FullName) + ".")) +
                        "IArgType" + "_" + EscapeIdentifier(s.Name) + 
                        "__" + arg.ToString();
            }
            else
            {
                return "IArgType" + "_" + EscapeIdentifier(s.Name) + "__" + arg.ToString();
            }
        }

        private string GetClassName(UserSymbol s, bool qualified = true)
        {
            if (qualified)
            {
                return EscapeIdentifier(options.Classname) + RootSuffix + "." + EscapeIdentifier(s.FullName);
            }
            else
            {
                return EscapeIdentifier(s.FullName);
            }
        }

        private void PrintUserConstantEnums(Namespace n, int indent)
        {
            OpenEnum(UserCnstKindName, ref indent);
            UserCnstSymb uc;

            bool isFirst = true;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (uc.IsTypeConstant || uc.IsSymbolicConstant || uc.IsVariable)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && !uc.IsNewConstant)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    Write(EscapeIdentifier(uc.Name), indent);
                }
                else
                {
                    WriteLine(",", 0);
                    Write(EscapeIdentifier(uc.Name), indent);
                }                
            }

            if (!isFirst)
            {
                WriteLine();
            }

            CloseBlock(ref indent);

            OpenEnum(TypeCnstKindName, ref indent);
            isFirst = true;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (!uc.IsTypeConstant)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    Write(EscapeIdentifier(uc.Name), indent);
                }
                else
                {
                    WriteLine(",", 0);
                    Write(EscapeIdentifier(uc.Name), indent);
                }
            }

            if (!isFirst)
            {
                WriteLine();
            }

            CloseBlock(ref indent);
        }

        private void PrintUserConstantNames(Namespace n, int indent)
        {
            WriteLine(string.Format("public static readonly string[] {0} =", UserCnstNamesName), indent);
            WriteLine("{", indent);
            ++indent;

            UserCnstSymb uc;
            bool isFirst = true;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (uc.IsTypeConstant || uc.IsSymbolicConstant || uc.IsVariable)
                {
                    continue;
                }
                else if (options.IsNewTypesOnly && !uc.IsNewConstant)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    Write(string.Format("\"{0}\"", uc.FullName), indent);
                }
                else
                {
                    WriteLine(",", 0);
                    Write(string.Format("\"{0}\"", uc.FullName), indent);
                }
            }

            if (!isFirst)
            {
                WriteLine();
            }

            --indent;
            WriteLine("};", indent);
            WriteLine();
            
            WriteLine(string.Format("public static readonly string[] {0} =", TypeCnstNamesName), indent);
            WriteLine("{", indent);
            ++indent;
            isFirst = true;
            foreach (var s in n.Symbols)
            {
                if (s.Kind != SymbolKind.UserCnstSymb)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (!uc.IsTypeConstant)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    Write(string.Format("\"{0}\"", uc.FullName), indent);
                }
                else
                {
                    WriteLine(",", 0);
                    Write(string.Format("\"{0}\"", uc.FullName), indent);
                }
            }

            if (!isFirst)
            {
                WriteLine();
            }

            --indent;
            WriteLine("};", indent);
            WriteLine();
        }

        private void PrintUsings(int indent)
        {
            WriteLine(string.Format("using {0};", typeof(String).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(LinkedList<int>).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(Contract).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(BigInteger).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(SpinLock).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(Task).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(AST<Node>).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(Node).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(ICSharpTerm).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(Rational).Namespace), indent);
            WriteLine(string.Format("using {0};", typeof(Term).Namespace), indent);
            WriteLine();
        }

        private void WriteLine()
        {
            writer.WriteLine();
        }

        private void WriteLine(string s, int indent)
        {
            if (indentStr.Length != indent * IndentAmount)
            {
                indentStr = indent == 0 ? string.Empty : new string(' ', indent * IndentAmount);
            }

            writer.Write(indentStr);
            writer.WriteLine(s);
        }

        private void Write(string s, int indent)
        {
            if (indent == 0)
            {
                writer.Write(s);
                return;
            }

            if (indentStr.Length != indent * IndentAmount)
            {
                indentStr = indent == 0 ? string.Empty : new string(' ', indent * IndentAmount);
            }

            writer.Write(indentStr);
            writer.Write(s);
        }

        private string EscapeIdentifier(string s)
        {
            char c;
            int primeCount = 0;
            string outId = string.Empty;
            for (var i = 0; i < s.Length; ++i)
            {
                c = s[i];
                switch (c)
                {
                    case '.':
                        if (primeCount == 0)
                        {
                            outId += ".";
                        }
                        else
                        {
                            outId = string.Format("{0}_PRIME{1}.", outId, primeCount);
                            primeCount = 0;
                        }

                        break;
                    case '#':
                        outId += "TYPECONST_";
                        break;
                    case '[':
                        outId += "_NDEX_";
                        break;
                    case ']':
                        break;
                    case '\'':
                        ++primeCount;
                        break;
                    default:
                        outId += c;
                        break;
                }
            }

            if (primeCount != 0)
            {
                outId = string.Format("{0}_PRIME{1}", outId, primeCount);
            }

            var splits = outId.Split(DotDelim);
            outId = string.Empty;
            for (int i = 0; i < splits.Length; ++i)
            {
                if (CSharpKeywords.Contains(splits[i]))
                {
                    outId += "@" + splits[i];
                }
                else
                {
                    outId += splits[i];
                }

                if (i < splits.Length - 1)
                {
                    outId += ".";
                }
            }

            return outId;
        }

        private IEnumerable<string> GetInherits(Symbol s)
        {
            Symbol mapSymb = null;
            switch (s.Kind)
            {
                case SymbolKind.BaseCnstSymb:
                    {
                        var bc = (BaseCnstSymb)s;
                        switch (bc.CnstKind)
                        {
                            case CnstKind.Numeric:
                                mapSymb = theRealSymbol;
                                break;
                            case CnstKind.String:
                                mapSymb = theStringSymbol;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    }
                case SymbolKind.BaseSortSymb:
                    {
                        var bs = (BaseSortSymb)s;
                        switch (bs.SortKind)
                        {
                            case BaseSortKind.Integer:
                            case BaseSortKind.Natural:
                            case BaseSortKind.NegInteger:
                            case BaseSortKind.PosInteger:
                            case BaseSortKind.Real:
                                mapSymb = theRealSymbol;
                                break;
                            case BaseSortKind.String:
                                mapSymb = theStringSymbol;
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    }
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    mapSymb = s;
                    break;
                case SymbolKind.UserCnstSymb:
                    mapSymb = theTrueSymbol;
                    break;
                case SymbolKind.UserSortSymb:
                    mapSymb = ((UserSortSymb)s).DataSymbol;
                    break;
                default:
                    throw new NotImplementedException();
            }

            Set<string> inheritSet;
            if (!inheritsMap.TryFindValue(mapSymb, out inheritSet))
            {
                return null;
            }

            return inheritSet;
        }

        private int ComputeMinElement(UserSymbol s, Set<UserSymbol> blocked)
        {
            Contract.Requires(s != null && (s.Kind == SymbolKind.ConSymb || s.Kind == SymbolKind.MapSymb));
            Contract.Requires(blocked != null && !blocked.Contains(s));

            MinimalElement[] minArgs;
            int cost = 1;
            if (defaultValueMap.TryFindValue(s, out minArgs))
            {
                for (int i = 0; i < minArgs.Length; ++i)
                {
                    cost += minArgs[i].Cost;
                }

                return cost;
            }


            blocked.Add(s);
            MinimalElement min;
            minArgs = new MinimalElement[s.Arity];
            for (int i = 0; i < s.Arity; ++i)
            {
                min = new MinimalElement(s, i);
                min.Cost = -1;
                minArgs[i] = min;
                using (var it = s.CanonicalForm[i].RangeMembers.GetEnumerator())
                {
                    if (it.MoveNext())
                    {
                        min.Value = new Rational(it.Current.Key, BigInteger.One);
                        min.Cost = 1;
                        continue;
                    }
                }

                foreach (var e in s.CanonicalForm[i].NonRangeMembers)
                {
                    if (e.Kind == SymbolKind.BaseCnstSymb || 
                        e.Kind == SymbolKind.UserCnstSymb)
                    {
                        min.Value = e;
                        min.Cost = 1;
                        break;
                    }
                    else if (e.Kind == SymbolKind.BaseSortSymb)
                    {
                        var bs = (BaseSortSymb)e;
                        switch (bs.SortKind)
                        {
                            case BaseSortKind.Integer:
                            case BaseSortKind.Real:
                            case BaseSortKind.Natural:
                                min.Value = Rational.Zero;
                                min.Cost = 1;
                                break;
                            case BaseSortKind.NegInteger:
                                min.Value = new Rational(-1);
                                min.Cost = 1;
                                break;
                            case BaseSortKind.PosInteger:
                                min.Value = Rational.One;
                                min.Cost = 1;
                                break;
                            case BaseSortKind.String:
                                min.Value = string.Empty;
                                min.Cost = 1;
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    else if (e.Kind == SymbolKind.UserSortSymb)
                    {
                        var us = ((UserSortSymb)e).DataSymbol;
                        if (blocked.Contains(us))
                        {
                            continue;
                        }

                        var uscost = ComputeMinElement(us, blocked);
                        if (min.Cost == -1 || uscost < min.Cost)
                        {
                            min.Cost = uscost;
                            min.Value = us;
                        }
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }                    
                }

                if (min.Cost == -1)
                {
                    return -1;
                }

                cost += min.Cost;
            }

            defaultValueMap.Add(s, minArgs);
            blocked.Remove(s);
            return cost;
        }

        private class MinimalElement
        {
            public int Cost
            {
                get;
                set;
            }

            public object Value
            {
                get;
                set;
            }

            public UserSymbol Constructor
            {
                get;
                private set;
            }

            public int Pos
            {
                get;
                private set;
            }

            public MinimalElement(UserSymbol constructor, int pos)
            {
                Pos = pos;
                Constructor = constructor;
            }
        }
    }
}