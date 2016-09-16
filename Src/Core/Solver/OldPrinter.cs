namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Numerics;

    using API;
    using API.Nodes;
    using Compiler;
    using Common;
    using Common.Rules;
    using Common.Terms;
    using Common.Extras;

    internal static class OldPrinter
    {
        private static readonly Rational MaxInt = new Rational(new BigInteger(int.MaxValue), BigInteger.One);
        private static readonly Rational MinInt = new Rational(new BigInteger(int.MinValue), BigInteger.One);

        public static bool Print(           
            FactSet set, 
            Map<UserSymbol, int> intros,
            TextWriter writer, 
            List<Flag> flags, 
            out string oldModelName)
        {
            var result = true;
            oldModelName = MangleName(set.Model.Node.Name);
            foreach (var kv in intros)
            {
                writer.WriteLine(
                    "{0}[Introduce({1}, {2})]",
                    MkTab(1),
                    MangleName(kv.Key.FullName),
                    kv.Value);
            }

            writer.WriteLine("partial model {0} of Requires", MangleName(set.Model.Node.Name));
            writer.WriteLine("{");
            foreach (var f in set.Facts)
            {
                writer.Write(MkTab(1));
                result = PrintFactTerm(f, set.Model.Node, writer, flags) && result;
                writer.WriteLine();
            }

            writer.WriteLine("}");
            writer.WriteLine("domain Requires extends {0}", MangleName(set.Model.Node.Domain.Name));
            writer.WriteLine("{");
            writer.WriteLine("{0}QUERY ::= (String).", MkTab(1));
            writer.WriteLine("{0}conforms := QUERY(\"{1}.requires\").", MkTab(1), MangleName(set.Model.Node.Name));
            writer.WriteLine("{0}requires := QUERY(\"{1}.requires\").", MkTab(1), MangleName(set.Model.Node.Name));
            writer.WriteLine("{0}ensures := QUERY(\"{1}.ensures\").", MkTab(1), MangleName(set.Model.Node.Name));
            result = PrintRules(set.Rules, writer, flags) && result;
            writer.WriteLine("}");

            result = PrintModelDepends(
                        ((Location)set.Model.Node.Domain.CompilerData).AST.Node, 
                        writer, 
                        flags, 
                        new Set<string>(string.Compare));
            return result;
        }

        private static bool PrintModelDepends(
            Node module,
            TextWriter writer,
            List<Flag> flags,
            Set<string> gendModules)
        {
            Contract.Requires(module != null && module.IsModule);

            string name;
            if (!module.TryGetStringAttribute(AttributeKind.Name, out name) || gendModules.Contains(name))
            {
                return true;
            }

            gendModules.Add(name);
            var mod = (ModuleData)module.CompilerData;
            var result = Print(mod.Reduced.Node, mod.SymbolTable, mod.FinalOutput, writer, flags);
            var dom = (Domain)mod.Reduced.Node;
            foreach (var c in dom.Compositions)
            {
                result = PrintModelDepends(((Location)c.CompilerData).AST.Node, writer, flags, gendModules) && result;
            }

            return result;
        }

        private static bool Print(
            Node module, 
            SymbolTable table,
            object compiled, 
            TextWriter writer, 
            List<Flag> flags)
        {
            Contract.Requires(module != null && table != null && compiled != null && writer != null && flags != null);
            Contract.Requires(module.IsModule);

            switch (module.NodeKind)
            {
                case NodeKind.Model:
                    return PrintModel((Model)module, writer, flags);
                case NodeKind.Domain:
                    return PrintDomain((Domain)module, (RuleTable)compiled, writer, flags);
                case NodeKind.Transform:
                    return PrintTransform((Transform)module, (RuleTable)compiled, writer, flags);
                default:
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            module,
                            Constants.NotImplemented.ToString("Cannot solve/verify " + module.NodeKind.ToString()),
                            Constants.NotImplemented.Code));
                    return false;
            }
        }

        private static bool PrintDomain(Domain module, RuleTable rules, TextWriter writer, List<Flag> flags)
        {
            writer.WriteLine("domain {0}", MangleName(module.Name));
            if (module.Compositions.Count > 0)
            {
                writer.WriteLine(module.ComposeKind == ComposeKind.Extends ? "extends" : "includes");
                int i = 0;
                foreach (var cmp in module.Compositions)
                {
                    if (!string.IsNullOrEmpty(cmp.Rename))
                    {
                        writer.Write("{0}::{1}", MangleName(cmp.Rename), MangleName(cmp.Name));
                    }
                    else
                    {
                        writer.Write(MangleName(cmp.Name));
                    }

                    if (i < module.Compositions.Count - 1)
                    {
                        writer.WriteLine(",");
                    }
                    else
                    {
                        writer.WriteLine();
                    }

                    ++i;
                }
            }

            writer.WriteLine("{");
            var result = PrintTypeSystem(rules.Index.SymbolTable, writer, flags);
            result = PrintRules(rules, writer, flags) && result;
            writer.WriteLine("{0}conforms := QUERY(\"{1}.conforms\").", MkTab(1), MangleName(module.Name));

            foreach (var mr in module.Compositions)
            {
                if (!string.IsNullOrEmpty(mr.Rename))
                {
                    writer.WriteLine("{0}QUERY(x) :- {1}.QUERY(x).", MkTab(1), MangleName(mr.Rename));
                }
            }

            writer.WriteLine("}");
            return result;
        }

        private static bool PrintTransform(Transform module, RuleTable rules, TextWriter writer, List<Flag> flags)
        {
            var result = true;
            writer.WriteLine("transform {0}", module.Name);
            var isFirst = true;
            foreach (var p in module.Inputs)
            {
                if (!p.IsValueParam)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    writer.Write("<{0}: {1}", MangleName(p.Name), MangleName(((Id)p.Type).Name));
                }
                else
                {
                    writer.Write(", {0}: {1}", MangleName(p.Name), MangleName(((Id)p.Type).Name));
                }
            }

            if (!isFirst)
            {
                writer.WriteLine(">");
            }

            writer.WriteLine("from");
            isFirst = true;
            foreach (var p in module.Inputs)
            {
                if (p.IsValueParam)
                {
                    continue;
                }

                if (isFirst)
                {
                    isFirst = false;
                    writer.WriteLine("{0}::{1}", MangleName(((ModRef)p.Type).Rename), MangleName(((ModRef)p.Type).Name));
                }
                else
                {
                    writer.WriteLine(",{0}::{1}", MangleName(((ModRef)p.Type).Rename), MangleName(((ModRef)p.Type).Name));
                }
            }

            if (isFirst)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        module,
                        Constants.NotImplemented.ToString("Cannot translate a transform with zero model inputs."),
                        Constants.NotImplemented.Code));
                result = false;
            }

            writer.WriteLine("to");
            isFirst = true;
            foreach (var p in module.Outputs)
            {
                if (isFirst)
                {
                    isFirst = false;
                    writer.WriteLine("{0}::{1}", MangleName(((ModRef)p.Type).Rename), MangleName(((ModRef)p.Type).Name));
                }
                else
                {
                    writer.WriteLine(",{0}::{1}", MangleName(((ModRef)p.Type).Rename), MangleName(((ModRef)p.Type).Name));
                }
            }

            writer.WriteLine("{");
            result = PrintTypeSystem(rules.Index.SymbolTable, writer, flags);
            result = PrintRules(rules, writer, flags) && result;

            foreach (var p in module.Inputs)
            {
                if (!p.IsValueParam)
                {
                    writer.WriteLine("{0}QUERY(x) :- {1}.QUERY(x).", MkTab(1), MangleName(((ModRef)p.Type).Rename));
                }
            }

            foreach (var p in module.Outputs)
            {
                if (!p.IsValueParam)
                {
                    writer.WriteLine("{0}QUERY(x) :- {1}.QUERY(x).", MkTab(1), MangleName(((ModRef)p.Type).Rename));
                }
            }

            writer.WriteLine("{0}requires ::= QUERY(\"{1}.requires\").", MkTab(1), MangleName(module.Name));
            writer.WriteLine("{0}ensures ::= QUERY(\"{1}.ensures\").", MkTab(1), MangleName(module.Name));
            writer.WriteLine("}");
            return result;
        }

        private static bool CanPrintConstraint(Term t, bool isSelection = false)
        {
            if (t.Symbol == t.Owner.TypeRelSymbol && !t.Args[0].Symbol.IsVariable)
            {
                return false;
            }
            else if (isSelection && t.Symbol != t.Owner.SelectorSymbol && !t.Symbol.IsVariable)
            {
                return false;
            }
            else if (t.Symbol == t.Owner.SelectorSymbol)
            {
                return CanPrintConstraint(t.Args[0], true);
            }
            else
            {
                foreach (var a in t.Args)
                {
                    if (!CanPrintConstraint(a, isSelection))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool PrintModel(Model module, TextWriter writer, List<Flag> flags)
        {
            return true;
        }

        private static bool PrintTypeSystem(SymbolTable table, TextWriter writer, List<Flag> flags)
        {          
            PrintConstants(table, writer);
            return PrintTypeDefinitions(table, writer, flags);
        }

        private static bool PrintRules(RuleTable rules, TextWriter writer, List<Flag> flags)
        {
            var result = true;
            var auxDeclares = new Map<Symbol, string>(Symbol.Compare);
            var otherBlame = rules.ModuleData.Source.AST.Node;
            foreach (var r in rules.Rules)
            {
                if (r.IsClone)
                {
                    continue;
                }

                result = PrintRule(rules, r, otherBlame, writer, auxDeclares, flags) && result;
            }

            Term[] types;
            string outType;
            string[] outTypes;
            bool typeResult;
            foreach (var kv in auxDeclares)
            {
                typeResult = rules.TryGetCompilerConTypes((UserSymbol)kv.Key, out types);
                Contract.Assert(typeResult);
                outTypes = new string[kv.Key.Arity];
                for (int i = 0; i < types.Length; ++i)
                {
                    if (types[i] == null)
                    {
                        outTypes[i] = "Any";
                    }
                    else
                    {
                        result = PrintTypeUnion(
                                        writer,
                                        string.Format("{0}_{1}", kv.Value, i),
                                        true,
                                        rules.Index.SymbolTable,
                                        new AppFreeCanUnn(types[i]),
                                        rules.ModuleData.Source.AST.Node,
                                        flags,
                                        out outType) && result;
                        outTypes[i] = outType;
                    }
                }
              
                writer.Write("{0}{1} ::= (", MkTab(1), kv.Value);
                for (int i = 0; i < kv.Key.Arity; ++i)
                {
                    if (i < kv.Key.Arity - 1)
                    {
                        writer.Write("{0}, ", outTypes[i]);
                    }
                    else
                    {
                        writer.Write(outTypes[i]);
                    }
                }

                writer.WriteLine(").");
            }
            
            return result;
        }

        private static bool PrintRule(RuleTable rules, CoreRule r, Node otherBlame, TextWriter writer, Map<Symbol, string> auxDeclares, List<Flag> flags)
        {
            var result = true;
            bool isVar;
            writer.Write(MkTab(1));
            result = PrintHeadTerm(rules, r.Head, r.Node == null ? otherBlame : r.Node, writer, auxDeclares, flags, out isVar) && result;
            if (isVar)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        r.Node == null ? otherBlame : r.Node,
                        Constants.NotImplemented.ToString("Cannot translate a rule whose head is a variable under an identity function."),
                        Constants.NotImplemented.Code));
                result = false;
            }

            if (r.Find1.IsNull && r.Find2.IsNull && r.Constraints.IsEmpty())
            {
                writer.WriteLine(".");
                return result;
            }

            writer.WriteLine();
            writer.WriteLine("{0}:-", MkTab(2));
            writer.Write(MkTab(3));
            bool isFirst = true;
            var nextComprId = 0;
            var cnstrId = 0;
            var defCache = new List<string>();
            if (!r.Find1.IsNull)
            {
                isFirst = false;
                result = PrintFind(r, cnstrId, r.Find1, r.Node == null ? otherBlame : r.Node, writer, ref nextComprId, auxDeclares, defCache, flags) && result;
                ++cnstrId;
            }

            if (!r.Find2.IsNull)
            {
                Contract.Assert(!isFirst);
                writer.Write(", ");
                result = PrintFind(r, cnstrId, r.Find2, r.Node == null ? otherBlame : r.Node, writer, ref nextComprId, auxDeclares, defCache, flags) && result;
                ++cnstrId;
            }

            foreach (var c in r.Constraints)
            {
                if (!CanPrintConstraint(c))
                {
                    //// Some compiler mangled type test. Cannot print this.
                    ++cnstrId;
                    continue;
                }
                else if (isFirst)
                {
                    isFirst = false;
                    result = PrintBodyTerm(r, cnstrId, c, r.Node == null ? otherBlame : r.Node, writer, false, ref nextComprId, auxDeclares, defCache, flags) && result;
                }
                else
                {
                    writer.Write(", ");
                    result = PrintBodyTerm(r, cnstrId, c, r.Node == null ? otherBlame : r.Node, writer, false, ref nextComprId, auxDeclares, defCache, flags) && result;
                }

                ++cnstrId;
            }

            writer.WriteLine();
            writer.WriteLine("{0}.", MkTab(2));

            foreach (var def in defCache)
            {
                writer.Write(def);
            }

            return result;
        }

        private static bool PrintHeadTerm(
            RuleTable rules, 
            Term head, 
            Node blame, 
            TextWriter writer, 
            Map<Symbol, string> auxDeclMap, 
            List<Flag> flags, 
            out bool isVar)
        {
            string str;
            bool result = true;
            switch (head.Symbol.Kind)
            {
                case SymbolKind.BaseOpSymb:
                    {
                        if (head.Symbol == head.Owner.SelectorSymbol)
                        {
                            //// A selector is treated like a var
                            result = PrintHeadTerm(rules, head.Args[0], blame, writer, auxDeclMap, flags, out isVar);
                            writer.Write(".{0}", MangleName((string)((BaseCnstSymb)head.Args[1].Symbol).Raw));
                            isVar = true;
                        }
                        else if (((BaseOpSymb)head.Symbol).IsRelabel)
                        {
                            //// Skip the relabel, old compiler must re-infer this.
                            //// A relabel is treated like a var
                            result = PrintHeadTerm(rules, head.Args[2], blame, writer, auxDeclMap, flags, out isVar);
                            isVar = true;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    }

                    break;
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    {
                        writer.Write("{0}(", DeclareAndMangleCon((UserSymbol)head.Symbol, head.Owner.SymbolTable, auxDeclMap));
                        for (var i = 0; i < head.Args.Length; ++i)
                        {
                            result = PrintHeadTerm(rules, head.Args[i], blame, writer, auxDeclMap, flags, out isVar) && result;
                            if (i < head.Args.Length - 1)
                            {
                                writer.Write(", ");
                            }
                        }
                      
                        isVar = false;
                        writer.Write(")");
                    }

                    break;
                case SymbolKind.BaseCnstSymb:
                    result = MkBaseCnstStr((BaseCnstSymb)head.Symbol, blame, flags, out str);
                    writer.Write(str);
                    isVar = false;
                    break;

                case SymbolKind.UserCnstSymb:
                    result = MkUserCnstStr((UserCnstSymb)head.Symbol, head.Owner.SymbolTable, blame, false, flags, out str);
                    writer.Write(str);
                    isVar = head.Symbol.IsVariable;
                    break;

                default:
                    throw new NotImplementedException();
            }              

            return result;
        }

        private static bool PrintFind(
            CoreRule r,
            int cnstrIndex,
            FindData findData,
            Node blame,
            TextWriter writer,
            ref int nextComprId,
            Map<Symbol, string> auxDeclMap,
            List<string> defCache,
            List<Flag> flags)
        {
            Contract.Requires(!findData.IsNull);
            if (findData.Binding.Symbol.IsReservedOperation)
            {
                return PrintBodyTerm(r, cnstrIndex, findData.Pattern, blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags);
            }
            else if (findData.Pattern.Symbol.IsVariable)
            {
                var result = PrintBodyTerm(r, cnstrIndex, findData.Binding, blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags);
                writer.Write(" is ");
                if (findData.Type.Symbol.IsReservedOperation)
                {
                    string defName;
                    result = PrintTypeUnionToCache(
                        defCache,
                        string.Format("RULE_{0}_CNSTR_{1}", r.RuleId, cnstrIndex),
                        false,
                        r.Index.SymbolTable,
                        new AppFreeCanUnn(findData.Type),
                        blame,
                        flags,
                        out defName) && result;
                    writer.Write(defName);
                    return result;
                }
                else
                {
                    return PrintBodyTerm(r, cnstrIndex, findData.Type, blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                }
            }
            else
            {
                var result = PrintBodyTerm(r, cnstrIndex, findData.Binding, blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags);
                writer.Write(" is ");
                return PrintBodyTerm(r, cnstrIndex, findData.Pattern, blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
            }
        }

        private static bool PrintFactTerm(
            Term term,
            Node blame,
            TextWriter writer,
            List<Flag> flags)
        {
            string str;
            bool result = true;
            switch (term.Symbol.Kind)
            {
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    {
                        writer.Write("{0}(", MangleName(((UserSymbol)term.Symbol).FullName));
                        for (var i = 0; i < term.Args.Length; ++i)
                        {
                            if (i < term.Args.Length - 1)
                            {
                                result = PrintFactTerm(term.Args[i], blame, writer, flags) && result;
                                writer.Write(", ");
                            }
                            else
                            {
                                result = PrintFactTerm(term.Args[i], blame, writer, flags) && result;
                            }
                        }

                        writer.Write(")");
                    }

                    break;
                case SymbolKind.BaseCnstSymb:
                    result = MkBaseCnstStr((BaseCnstSymb)term.Symbol, blame, flags, out str);
                    writer.Write(str);
                    break;

                case SymbolKind.UserCnstSymb:
                    result = MkUserCnstStr((UserCnstSymb)term.Symbol, term.Owner.SymbolTable, blame, false, flags, out str);
                    writer.Write(str);
                    break;

                case SymbolKind.UserSortSymb:
                    result = MkUserSortStr((UserSortSymb)term.Symbol, blame, flags, out str);
                    writer.Write(str);
                    break;

                default:
                    throw new NotImplementedException();
            }

            return result;
        }

        private static bool PrintBodyTerm(
            CoreRule r,
            int constrIndex,
            Term term, 
            Node blame, 
            TextWriter writer, 
            bool isCompr,
            ref int nextComprId,
            Map<Symbol, string> auxDeclMap, 
            List<string> defCache,
            List<Flag> flags)
        {
            string str;
            bool result = true;
            switch (term.Symbol.Kind)
            {
                case SymbolKind.BaseOpSymb:
                    {
                        var bo = (BaseOpSymb)term.Symbol;
                        if (bo.OpKind is RelKind)
                        {
                            var relKind = (RelKind)bo.OpKind;
                            switch (relKind)
                            {
                                case RelKind.Eq:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" = ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Neq:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" != ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Lt:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" < ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Le:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" <= ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Gt:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" > ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Ge:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" >= ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                case RelKind.Typ:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(" in ");
                                    //if (term.Args[1].Symbol.IsReservedOperation)
                                    //{
                                    string defName;
                                    result = PrintTypeUnionToCache(
                                        defCache,
                                        string.Format("RULE_{0}_CNSTR_{1}", r.RuleId, constrIndex),
                                        false,
                                        r.Index.SymbolTable,
                                        new AppFreeCanUnn(term.Args[1]),
                                        blame,
                                        flags,
                                        out defName) && result;
                                    writer.Write(defName);
                                    return result;
                                    //}
                                    //else
                                    //{
                                    //    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    //}
                                    //
                                    //break;
                                case RelKind.No:
                                    writer.Write("no ");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    break;

                                default:
                                    flags.Add(
                                        new Flag(
                                            SeverityKind.Error,
                                            blame,
                                            Constants.NotImplemented.ToString(string.Format("Cannot translate the relation {0}.", API.ASTQueries.ASTSchema.Instance.ToString(relKind))),
                                            Constants.NotImplemented.Code));
                                    result = false;
                                    break;
                            }
                        }
                        else if (bo.OpKind is OpKind)
                        {
                            var opKind = (OpKind)bo.OpKind;
                            switch (opKind)
                            {
                                case OpKind.Neg:
                                    writer.Write("-");
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    break;

                                case OpKind.Add:
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    writer.Write(" + ");

                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write(")");
                                    }

                                    break;

                                case OpKind.Sub:
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    writer.Write(" - ");

                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write(")");
                                    }

                                    break;

                                case OpKind.Mul:
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    writer.Write(" * ");
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write(")");
                                    }

                                    break;

                                case OpKind.Div:
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    writer.Write(" / ");
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write(")");
                                    }
                                    break;

                                case OpKind.Mod:
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[0]))
                                    {
                                        writer.Write(")");
                                    }

                                    writer.Write(" % ");
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write("(");
                                    }
                                    result = PrintBodyTerm(r, constrIndex, term.Args[1], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    if (CanPutParens(term.Args[1]))
                                    {
                                        writer.Write(")");
                                    }

                                    break;

                                case OpKind.Count:
                                    writer.Write("count(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(")");
                                    break;

                                case OpKind.MaxAll:
                                    writer.Write("max(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                case OpKind.MinAll:
                                    writer.Write("min(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                case OpKind.Sum:
                                    writer.Write("sum(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                case OpKind.Prod:
                                    writer.Write("product(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                case OpKind.GCDAll:
                                    writer.Write("gcd(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                case OpKind.LCMAll:
                                    writer.Write("lcm(");
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, true, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(",{0})", term.Args[0].Symbol.Arity - 1);
                                    break;

                                default:
                                    API.ASTQueries.OpStyleKind style;
                                    flags.Add(
                                        new Flag(
                                            SeverityKind.Error,
                                            blame,
                                            Constants.NotImplemented.ToString(string.Format("Cannot translate the operator {0}.", API.ASTQueries.ASTSchema.Instance.ToString(opKind, out style))),
                                            Constants.NotImplemented.Code));
                                    result = false;
                                    break;
                            }
                        }
                        else
                        {
                            var resOpKind = (ReservedOpKind)bo.OpKind;
                            switch (resOpKind)
                            {
                                case ReservedOpKind.Select:
                                    result = PrintBodyTerm(r, constrIndex, term.Args[0], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                    writer.Write(".{0}", MangleName((string)((BaseCnstSymb)term.Args[1].Symbol).Raw));
                                    break;

                                default:
                                    flags.Add(
                                        new Flag(
                                            SeverityKind.Error,
                                            blame,
                                            Constants.NotImplemented.ToString(string.Format("Cannot translate the operator {0}.", API.ASTQueries.ASTSchema.Instance.ToString(resOpKind))),
                                            Constants.NotImplemented.Code));
                                    result = false;
                                    break;
                            }
                        }
                    }

                    break;
                case SymbolKind.ConSymb:
                case SymbolKind.MapSymb:
                    {
                        writer.Write("{0}(", DeclareAndMangleCon((UserSymbol)term.Symbol, term.Owner.SymbolTable, auxDeclMap));
                        for (var i = 0; i < term.Args.Length; ++i)
                        {
                            if (i < term.Args.Length - 1)
                            {
                                result = PrintBodyTerm(r, constrIndex, term.Args[i], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                                writer.Write(", ");
                            }
                            else if (isCompr)
                            {
                                writer.Write(MangleName(string.Format("~COMPRVAR~{0}", nextComprId)));
                                ++nextComprId;
                            }
                            else
                            {
                                result = PrintBodyTerm(r, constrIndex, term.Args[i], blame, writer, false, ref nextComprId, auxDeclMap, defCache, flags) && result;
                            }
                        }

                        writer.Write(")");
                    }

                    break;
                case SymbolKind.BaseCnstSymb:
                    result = MkBaseCnstStr((BaseCnstSymb)term.Symbol, blame, flags, out str);
                    writer.Write(str);
                    break;

                case SymbolKind.UserCnstSymb:
                    result = MkUserCnstStr((UserCnstSymb)term.Symbol, term.Owner.SymbolTable, blame, false, flags, out str);
                    writer.Write(str);
                    break;

                case SymbolKind.UserSortSymb:
                    result = MkUserSortStr((UserSortSymb)term.Symbol, blame, flags, out str);
                    writer.Write(str);
                    break;

                default:
                    throw new NotImplementedException();
            }

            return result;
        }

        private static bool PrintTypeDefinitions(SymbolTable table, TextWriter writer, List<Flag> flags)
        {
            Node n;
            Node defNode;
            bool result = true;
            foreach (var s in table.Root.Symbols)
            {
                if (!s.IsDataConstructor && s.Kind != SymbolKind.UnnSymb)
                {
                    continue;
                }

                defNode = null;
                foreach (var def in s.Definitions)
                {
                    n = def.GetPathParent(0);
                    if (n != null && n.IsModule && ((ModuleData)n.CompilerData).Reduced == table.ModuleData.Reduced)
                    {
                        defNode = def.Node;
                        break;
                    }
                }

                if (defNode != null)
                {
                    switch (s.Kind)
                    {
                        case SymbolKind.ConSymb:
                            result = PrintConDef(writer, (ConDecl)defNode, (ConSymb)s, table, flags) && result;
                            break;
                        case SymbolKind.MapSymb:
                            result = PrintMapDef(writer, (MapDecl)defNode, (MapSymb)s, table, flags) && result;
                            break;
                        case SymbolKind.UnnSymb:
                            result = PrintUnnDef(writer, (UnnDecl)defNode, (UnnSymb)s, table, flags) && result;
                            break;
                        default:
                            flags.Add(
                                new Flag(SeverityKind.Error,
                                         defNode,
                                         Constants.NotImplemented.ToString(string.Format("Cannot translate {0}", s.Kind)),
                                         Constants.NotImplemented.Code));
                            result = false;
                            break;
                    }
                }
            }

            return result;
        }

        private static bool PrintAttributes(TextWriter writer, Node node, UserSymbol symb, List<Flag> flags)
        {
            int i;
            var result = true;
            //// ******************** First, print closed attributes ********************
            var closed = new List<string>();
            var fields = symb.Kind == SymbolKind.ConSymb
                                ? ((ConDecl)node).Fields
                                : ((MapDecl)node).Dom.Concat(((MapDecl)node).Cod);
            if (symb.Kind == SymbolKind.MapSymb || ((ConSymb)symb).IsNew)
            {
                i = 0;
                foreach (var f in fields)
                {
                    if (f.IsAny || !HasUserSort(symb, i))
                    {
                        ++i;
                        continue;
                    }
                    else if (string.IsNullOrEmpty(f.Name))
                    {
                        flags.Add(
                            new Flag(
                                SeverityKind.Error,
                                f,
                                Constants.NotImplemented.ToString("Cannot translate an unnamed relational field"),
                                Constants.NotImplemented.Code));
                        result = false;
                    }
                    else
                    {
                        closed.Add(f.Name);
                    }

                    ++i;
                }
            }

            if (closed.Count > 0)
            {
                writer.Write("{0}[Closed(", MkTab(1));
                bool isFirst = true;
                foreach (var c in closed)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                        writer.Write(MangleName(c));
                    }
                    else
                    {
                        writer.Write(", {0}", MangleName(c));
                    }
                }

                writer.WriteLine(")]");
            }

            return result;

            //// Disabled translation of other functional constraints due to bugs
            /*
            if (symb.Kind == SymbolKind.ConSymb)
            {
                return result;
            }

            var mapSymb = (MapSymb)symb;
            var mapDecl = (MapDecl)node;
            //// ******************** Second, print totality constraints ********************
            if (!mapSymb.IsPartial)            
            {
                writer.Write("{0}[Total(", MkTab(1));
                i = 0;
                foreach (var f in mapDecl.Dom)
                {
                    if (string.IsNullOrEmpty(f.Name))
                    {
                        flags.Add(
                            new Flag(
                                SeverityKind.Error,
                                f,
                                Constants.NotImplemented.ToString("Cannot translate an unnamed relational field"),
                                Constants.NotImplemented.Code));
                        result = false;
                    }
                    else if (i < mapSymb.DomArity - 1)
                    {
                        writer.Write("{0}, ", MangleName(f.Name));
                    }
                    else
                    {
                        writer.Write(MangleName(f.Name));
                    }

                    ++i;
                }

                writer.WriteLine(")]");
            }

            //// ******************** Third, print functional annotation ********************
            string funAnnot = null;
            switch (mapSymb.MapKind)
            {
                case MapKind.Bij:
                    funAnnot = "Bijection";
                    break;
                case MapKind.Fun:
                    funAnnot = "Unique";
                    break;
                case MapKind.Inj:
                    funAnnot = "Injection";
                    break;
                case MapKind.Sur:
                    funAnnot = "Surjection";
                    break;
                default:
                    throw new NotImplementedException();
            }

            writer.Write("{0}[{1}(", MkTab(1), funAnnot);
            i = 0;
            foreach (var f in mapDecl.Dom)
            {
                if (string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an unnamed relational field"),
                            Constants.NotImplemented.Code));
                    result = false;
                }
                else if (i < mapSymb.DomArity - 1)
                {
                    writer.Write("{0}, ", MangleName(f.Name));
                }
                else
                {
                    writer.Write("{0} -> ", MangleName(f.Name));
                }

                ++i;
            }

            i = 0;
            foreach (var f in mapDecl.Cod)
            {
                if (string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an unnamed relational field"),
                            Constants.NotImplemented.Code));
                    result = false;
                }
                else if (i < mapSymb.CodArity - 1)
                {
                    writer.Write("{0}, ", MangleName(f.Name));
                }
                else
                {
                    writer.Write("{0}", MangleName(f.Name));
                }

                ++i;
            }

            writer.WriteLine(")]");
          
            return result;
            */
        }

        private static bool PrintConDef(TextWriter writer, ConDecl decl, ConSymb symb, SymbolTable table, List<Flag> flags)
        {
            var result = true;
            string str;
            bool hasFieldNames = false;
            var outTypes = new string[symb.Arity];
            var i = 0;
            foreach (var f in decl.Fields)
            {
                if (i == 0)
                {
                    hasFieldNames = !string.IsNullOrEmpty(f.Name);
                }
                else if (hasFieldNames && string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an unnamed field when other fields have names"),
                            Constants.NotImplemented.Code));
                    result = false;
                }
                else if (!hasFieldNames && !string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an named field when other fields are unnamed"),
                            Constants.NotImplemented.Code));
                    result = false;
                }

                if (PrintTypeUnion(writer, string.Format("{0}_{1}", symb.FullName, i), true, table, symb.CanonicalForm[i], decl, flags, out str))
                {
                    outTypes[i] = str;
                }
                else
                {
                    result = false;
                }

                ++i;
            }

            if (!result)
            {
                return false;
            }

            result = PrintAttributes(writer, decl, symb, flags) && result;
            writer.Write("{0}{1}{2} ::= (", MkTab(1), symb.IsNew ? "primitive " : "", MangleName(symb.Name));
            i = 0;
            foreach (var f in decl.Fields)
            {
                if (hasFieldNames)
                {
                    writer.Write("{0}: {1}{2}", MangleName(f.Name), outTypes[i], i < symb.Arity - 1 ? ", " : "");
                }
                else
                {
                    writer.Write("{0}{1}", outTypes[i], i < symb.Arity - 1 ? ", " : "");
                }

                ++i;
            }

            writer.WriteLine(").");
            return result;
        }

        private static bool PrintMapDef(TextWriter writer, MapDecl decl, MapSymb symb, SymbolTable table, List<Flag> flags)
        {
            var result = true;
            string str;
            bool hasFieldNames = false;
            var outTypes = new string[symb.Arity];
            var i = 0;
            var fields = decl.Dom.Concat(decl.Cod);
            foreach (var f in fields)
            {
                if (i == 0)
                {
                    hasFieldNames = !string.IsNullOrEmpty(f.Name);
                }
                else if (hasFieldNames && string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an unnamed field when other fields have names"),
                            Constants.NotImplemented.Code));
                    result = false;
                }
                else if (!hasFieldNames && !string.IsNullOrEmpty(f.Name))
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            f,
                            Constants.NotImplemented.ToString("Cannot translate an named field when other fields are unnamed"),
                            Constants.NotImplemented.Code));
                    result = false;
                }

                if (PrintTypeUnion(writer, string.Format("{0}_{1}", symb.FullName, i), true, table, symb.CanonicalForm[i], decl, flags, out str))
                {
                    outTypes[i] = str;
                }
                else
                {
                    result = false;
                }

                ++i;
            }

            if (!result)
            {
                return false;
            }

            result = PrintAttributes(writer, decl, symb, flags) && result;
            writer.Write("{0}primitive {1} ::= (", MkTab(1), MangleName(symb.Name));
            i = 0;
            foreach (var f in fields)
            {
                if (hasFieldNames)
                {
                    writer.Write("{0}: {1}{2}", MangleName(f.Name), outTypes[i], i < symb.Arity - 1 ? ", " : "");
                }
                else
                {
                    writer.Write("{0}{1}", outTypes[i], i < symb.Arity - 1 ? ", " : "");
                }

                ++i;
            }

            writer.WriteLine(").");
            return result;
        }

        private static bool PrintUnnDef(TextWriter writer, UnnDecl decl, UnnSymb symb, SymbolTable table, List<Flag> flags)
        {
            string outName;
            return PrintTypeUnion(
                        writer,
                        symb.FullName,
                        false,
                        table,
                        symb.CanonicalForm[0],
                        decl,
                        flags,
                        out outName);
        }

        private static bool PrintTypeUnion(
                                TextWriter writer, 
                                string name, 
                                bool isAnonType,
                                SymbolTable table,
                                AppFreeCanUnn form,
                                Node blame,
                                List<Flag> flags, 
                                out string outName)
        {
            name = MangleName(name);
            var unnCnsts = new List<string>();
            var unnTypes = new List<string>();

            string str;
            bool result = true;
            foreach (var intr in form.RangeMembers)            
            {
                if (intr.Value - intr.Key > 255)
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            blame,
                            Constants.NotImplemented.ToString("Cannot translate a range with that many members"),
                            Constants.NotImplemented.Code));
                    result = false;
                    continue;
                }

                var bigI = intr.Key;
                while (bigI <= intr.Value)
                {
                    if (MkBaseCnstStr(bigI, blame, flags, out str))
                    {
                        unnCnsts.Add(str);
                    }
                    else
                    {
                        result = false;
                    }

                    ++bigI;
                }
            }

            foreach (var s in form.NonRangeMembers)
            {
                switch (s.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                        if (MkBaseCnstStr((BaseCnstSymb)s, blame, flags, out str))
                        {
                            unnCnsts.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    case SymbolKind.BaseSortSymb:
                        if (MkBaseSortStr((BaseSortSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    case SymbolKind.UnnSymb:
                        if (MkUnnStr((UnnSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;

                    case SymbolKind.UserSortSymb:
                        if (MkUserSortStr((UserSortSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;                    
                    case SymbolKind.UserCnstSymb:
                        if (MkUserCnstStr((UserCnstSymb)s, table, blame, true, flags, out str))
                        {
                            if (s.IsDerivedConstant)
                            {
                                unnTypes.Add(str);
                            }
                            else
                            {
                                unnCnsts.Add(str);
                            }
                        }
                        else
                        {
                            result = false;
                        }

                        break;                 
                    default:
                        throw new NotImplementedException();
                }
            }

            if (!result)
            {
                outName = string.Empty;
                return false;
            }

            int i;
            if (isAnonType)
            {
                if (unnCnsts.Count > 0)
                {
                    writer.Write("{0}{1}_UNN_CNSTS ::= {{ ", MkTab(1), name);
                    i = 0;
                    foreach (var c in unnCnsts)
                    {
                        writer.Write("{0}{1}", c, i < unnCnsts.Count - 1 ? ", " : "");
                        ++i;
                    }

                    writer.WriteLine(" }.");
                    unnTypes.Add(string.Format("{0}_UNN_CNSTS", name));
                }

                Contract.Assert(unnTypes.Count > 0);
                if (unnTypes.Count == 1)
                {
                    outName = unnTypes[0];
                }
                else
                {
                    writer.Write("{0}{1}_UNN_TYPES ::= ", MkTab(1), name);
                    i = 0;
                    foreach (var t in unnTypes)
                    {
                        writer.Write("{0}{1}", t, i < unnTypes.Count - 1 ? " + " : "");
                        ++i;
                    }

                    writer.WriteLine(".");
                    outName = string.Format("{0}_UNN_TYPES", name);
                }
            }
            else
            {
                outName = null;
                if (unnCnsts.Count > 0)
                {
                    writer.Write(
                        "{0}{1}{2}::= {{ ", 
                        MkTab(1), 
                        name, 
                        unnTypes.Count == 0 ? name : string.Format("{0}_UNN_CNSTS", name));
                    i = 0;
                    foreach (var c in unnCnsts)
                    {
                        writer.Write("{0}{1}", c, i < unnCnsts.Count - 1 ? ", " : "");
                        ++i;
                    }

                    writer.WriteLine(" }.");

                    if (unnTypes.Count == 0)
                    {
                        outName = name;
                    }
                    else
                    {
                        unnTypes.Add(string.Format("{0}_UNN_CNSTS", name));
                    }
                }

                if (unnTypes.Count > 0)
                {
                    if (unnTypes.Count == 1)
                    {
                        unnTypes.Add(unnTypes[0]);
                    }

                    writer.Write("{0}{1} ::= ", MkTab(1), name);
                    i = 0;
                    foreach (var t in unnTypes)
                    {
                        writer.Write("{0}{1}", t, i < unnTypes.Count - 1 ? " + " : "");
                        ++i;
                    }

                    writer.WriteLine(".");
                    outName = name;
                }
            }

            Contract.Assert(!string.IsNullOrEmpty(outName));
            return true;
        }

        private static bool PrintTypeUnionToCache(
                                 List<string> defCache,
                                 string name,
                                 bool isAnonType,
                                 SymbolTable table,
                                 AppFreeCanUnn form,
                                 Node blame,
                                 List<Flag> flags,
                                 out string outName)
        {
            name = MangleName(name);
            var unnCnsts = new List<string>();
            var unnTypes = new List<string>();

            string str;
            bool result = true;
            foreach (var intr in form.RangeMembers)
            {
                if (intr.Value - intr.Key > 255)
                {
                    flags.Add(
                        new Flag(
                            SeverityKind.Error,
                            blame,
                            Constants.NotImplemented.ToString("Cannot translate a range with that many members"),
                            Constants.NotImplemented.Code));
                    result = false;
                    continue;
                }

                var bigI = intr.Key;
                while (bigI <= intr.Value)
                {
                    if (MkBaseCnstStr(bigI, blame, flags, out str))
                    {
                        unnCnsts.Add(str);
                    }
                    else
                    {
                        result = false;
                    }

                    ++bigI;
                }
            }

            foreach (var s in form.NonRangeMembers)
            {
                switch (s.Kind)
                {
                    case SymbolKind.BaseCnstSymb:
                        if (MkBaseCnstStr((BaseCnstSymb)s, blame, flags, out str))
                        {
                            unnCnsts.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    case SymbolKind.BaseSortSymb:
                        if (MkBaseSortStr((BaseSortSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    case SymbolKind.UnnSymb:
                        if (MkUnnStr((UnnSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;

                    case SymbolKind.UserSortSymb:
                        if (MkUserSortStr((UserSortSymb)s, blame, flags, out str))
                        {
                            unnTypes.Add(str);
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    case SymbolKind.UserCnstSymb:
                        if (MkUserCnstStr((UserCnstSymb)s, table, blame, true, flags, out str))
                        {
                            if (s.IsDerivedConstant)
                            {
                                unnTypes.Add(str);
                            }
                            else
                            {
                                unnCnsts.Add(str);
                            }
                        }
                        else
                        {
                            result = false;
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            if (!result)
            {
                outName = string.Empty;
                return false;
            }

            int i;
            string def = string.Empty;
            if (isAnonType)
            {
                if (unnCnsts.Count > 0)
                {
                    def += string.Format("{0}{1}_UNN_CNSTS ::= {{ ", MkTab(1), name);
                    i = 0;
                    foreach (var c in unnCnsts)
                    {
                        def += string.Format("{0}{1}", c, i < unnCnsts.Count - 1 ? ", " : "");
                        ++i;
                    }

                    def += string.Format(" }}.\n");
                    defCache.Add(def);
                    def = string.Empty;
                    unnTypes.Add(string.Format("{0}_UNN_CNSTS", name));
                }

                Contract.Assert(unnTypes.Count > 0);
                if (unnTypes.Count == 1)
                {
                    outName = unnTypes[0];
                }
                else
                {
                    def += string.Format("{0}{1}_UNN_TYPES ::= ", MkTab(1), name);
                    i = 0;
                    foreach (var t in unnTypes)
                    {
                        def += string.Format("{0}{1}", t, i < unnTypes.Count - 1 ? " + " : "");
                        ++i;
                    }

                    def += string.Format(".\n");
                    defCache.Add(def);
                    def = string.Empty;
                    outName = string.Format("{0}_UNN_TYPES", name);
                }
            }
            else
            {
                outName = null;
                if (unnCnsts.Count > 0)
                {
                    /*
                    //// Mangling unclear
                    def += string.Format(
                        "{0}{1}{2}::= {{ ",
                        MkTab(1),
                        name,
                        unnTypes.Count == 0 ? name : string.Format("{0}_UNN_CNSTS", name));
                    */

                    i = 0;
                    def += string.Format("{0}{1}::= {{ ", MkTab(1), name);
                    foreach (var c in unnCnsts)
                    {
                        def += string.Format("{0}{1}", c, i < unnCnsts.Count - 1 ? ", " : "");
                        ++i;
                    }

                    def += string.Format(" }}.\n");
                    defCache.Add(def);
                    def = string.Empty;

                    if (unnTypes.Count == 0)
                    {
                        outName = name;
                    }
                    else
                    {
                        unnTypes.Add(string.Format("{0}_UNN_CNSTS", name));
                    }
                }

                if (unnTypes.Count > 0)
                {
                    if (unnTypes.Count == 1)
                    {
                        unnTypes.Add(unnTypes[0]);
                    }

                    def += string.Format("{0}{1} ::= ", MkTab(1), name);
                    i = 0;
                    foreach (var t in unnTypes)
                    {
                        def += string.Format("{0}{1}", t, i < unnTypes.Count - 1 ? " + " : "");
                        ++i;
                    }

                    def += string.Format(".\n");
                    defCache.Add(def);
                    def = string.Empty;
                    outName = name;
                }
            }

            Contract.Assert(!string.IsNullOrEmpty(outName));
            return true;
        }

        private static bool MkUserCnstStr(UserCnstSymb symb, SymbolTable table, Node n, bool isTypeExpr, List<Flag> flags, out string cnstStr)
        {
            if (symb.IsDerivedConstant && isTypeExpr)
            {
                /*
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString("Cannot translate a type expression with derived constants"),
                        Constants.NotImplemented.Code));
                cnstStr = string.Empty;
                return false;
                */

                //// Widen the constant to QUERY type
                cnstStr = "QUERY";
                return true;
            }
            else if (symb.IsTypeConstant)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString("Cannot translate a type constant"),
                        Constants.NotImplemented.Code));
                cnstStr = string.Empty;
                return false;
            }

            if (symb.IsDerivedConstant)
            {
                if (symb.Name.Contains(SymbolTable.ManglePrefix) && !symb.FullName.Contains('.'))
                {
                    cnstStr = string.Format("QUERY(\"{0}.{1}\")", MangleName(table.ModuleSpace.Name), MangleName(symb.FullName));
                }
                else
                {
                    cnstStr = string.Format("QUERY(\"{0}\")", MangleName(symb.FullName));
                }
            }
            else if (symb.IsVariable)
            {
                if (symb.Name.Contains(SymbolTable.ManglePrefix))
                {
                    cnstStr = string.Format("{0}_{1}", MangleName(table.ModuleSpace.Name), MangleName(symb.FullName));
                }
                else
                {
                    cnstStr = MangleName(symb.FullName);
                }
            }
            else if (symb.IsSymbolicConstant)
            {
                cnstStr = MangleName(symb.FullName.Replace("%", "").Replace(".", "__"));
            }
            else
            {
                cnstStr = MangleName(symb.FullName);
            }

            return true;
        }

        private static bool MkUserSortStr(UserSortSymb symb, Node n, List<Flag> flags, out string typeStr)
        {
            typeStr = MangleName(symb.DataSymbol.FullName);
            return true;
        }

        private static bool MkUnnStr(UnnSymb symb, Node n, List<Flag> flags, out string typeStr)
        {
            typeStr = MangleName(symb.PrintableName);
            return true;
        }

        private static bool MkBaseSortStr(BaseSortSymb symb, Node n, List<Flag> flags, out string typeStr)
        {
            typeStr = MangleName(symb.PrintableName);
            return true;
        }

        private static bool MkBaseCnstStr(BigInteger i, Node n, List<Flag> flags, out string baseStr)
        {
            if (i < MinInt.Numerator)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString(
                           string.Format("Numeric {0} is too small to translate", i.ToString())),
                        Constants.NotImplemented.Code));
                baseStr = string.Empty;
                return false;
            }
            else if (i > MaxInt.Numerator)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString(
                           string.Format("Numeric {0} is too big to translate", i.ToString())),
                        Constants.NotImplemented.Code));
                baseStr = string.Empty;
                return false;
            }

            baseStr = i.ToString();
            return true;
        }

        private static bool MkBaseCnstStr(Rational r, Node n, List<Flag> flags, out string baseStr)
        {
            if (r < MinInt)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString(
                           string.Format("Numeric {0} is too small to translate", r.ToString())),
                        Constants.NotImplemented.Code));
                baseStr = string.Empty;
                return false;
            }
            else if (r > MaxInt)
            {
                flags.Add(
                    new Flag(
                        SeverityKind.Error,
                        n,
                        Constants.NotImplemented.ToString(
                           string.Format("Numeric {0} is too big to translate", r.ToString())),
                        Constants.NotImplemented.Code));
                baseStr = string.Empty;
                return false;
            }

            baseStr = r.ToString(10);
            return true;
        }

        private static bool MkBaseCnstStr(String s, Node n, List<Flag> flags, out string baseStr)
        {
            baseStr = API.ASTQueries.ASTSchema.Instance.Encode(s);
            return true;
        }

        private static bool MkBaseCnstStr(BaseCnstSymb bc, Node n, List<Flag> flags, out string baseStr)
        {
            switch (bc.CnstKind)
            {
                case CnstKind.Numeric:
                    return MkBaseCnstStr((Rational)bc.Raw, n, flags, out baseStr);
                case CnstKind.String:
                    return MkBaseCnstStr((string)bc.Raw, n, flags, out baseStr);
                default:
                    throw new NotImplementedException();
            }
        }

        private static void PrintConstants(SymbolTable table, TextWriter writer)
        {
            writer.WriteLine("{0}QUERY ::= (String).", MkTab(1));
            writer.WriteLine("{0}{1}_CONSTANTS ::=", MkTab(1), MangleName(table.ModuleSpace.Name));
            writer.WriteLine("{0}{{", MkTab(1));
            bool first = true;
            UserCnstSymb uc;
            foreach (var s in table.Root.Symbols)
            {
                if (!s.IsNonVarConstant)
                {
                    continue;
                }

                uc = (UserCnstSymb)s;
                if (uc.IsSymbolicConstant || uc.IsTypeConstant)
                {
                    continue;
                }

                if (first)
                {
                    first = false;
                    writer.WriteLine("{0} {1}", MkTab(2), MangleName(s.Name));
                }
                else
                {
                    writer.WriteLine("{0},{1}", MkTab(2), MangleName(s.Name));
                }
            }

            writer.WriteLine("{0}}}.", MkTab(1));
        }

        private static string MangleName(string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            if (name[0] == '~')
            {
                return "COMPLRGEN_" + name.Substring(1).Replace("~", "_COMPLRGEN_").Replace("'", "_PRIME");
            }
            else if (name[0] == '_')
            {
                return "a" + name.Replace("~", "_COMPLRGEN_").Replace("'", "_PRIME");
            }
            else
            {
                return name.Replace("~", "_COMPLRGEN_").Replace("'", "_PRIME");
            }
        }

        /// <summary>
        /// Checks if us is a compiler introduced symbol. If so, adds a declaration for it
        /// if not already declared.
        /// </summary>
        private static string DeclareAndMangleCon(UserSymbol us, SymbolTable table, Map<Symbol, string> auxDeclMap)
        {
            Contract.Requires(us != null && table != null && auxDeclMap != null);
            Contract.Requires(us.IsDataConstructor);

            string name;
            if (auxDeclMap.TryFindValue(us, out name))
            {
                return name;
            }
            else if (!us.FullName.Contains(SymbolTable.ManglePrefix))
            {
                return MangleName(us.FullName);
            }

            name = string.Format("{0}_{1}", MangleName(table.ModuleSpace.Name), MangleName(us.FullName));
            auxDeclMap.Add(us, name);
            return name;
        }

        private static string MkTab(int nTabs)
        {
            return nTabs <= 0 ? string.Empty : new string(' ', 3 * nTabs);
        }

        private static bool HasUserSort(UserSymbol symb, int arg)
        {
            var can = symb.CanonicalForm[arg];
            foreach (var s in can.NonRangeMembers)
            {
                if (s.Kind == SymbolKind.UserSortSymb)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanPutParens(Term t)
        {
            if (t.Symbol.Kind != SymbolKind.BaseOpSymb)
            {
                return false;
            }

            var bo = (BaseOpSymb)t.Symbol;
            if (!(bo.OpKind is OpKind))
            {
                return false;
            }

            var opKind = (OpKind)bo.OpKind;
            switch (opKind)
            {
                case OpKind.Add:
                case OpKind.Sub:
                case OpKind.Mul:
                case OpKind.Div:
                case OpKind.Mod:
                case OpKind.Neg:
                    return true;
                default:
                    return false;
            }
        }
    }
}