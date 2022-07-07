namespace Microsoft.Formula.API.ASTQueries
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Common;

    /// <summary>
    /// A simplified representation of the AST schema for checking
    /// feasibility of AST queries.
    /// </summary>
    public sealed class ASTSchema
    {
        private static readonly NodeKind[] IsQuoteItem = new NodeKind[]
        { 
                NodeKind.QuoteRun,
                NodeKind.Id,
                NodeKind.Cnst,
                NodeKind.Quote,
                NodeKind.FuncTerm,
                NodeKind.Compr,
        };

        private static readonly NodeKind[] IsFuncTerm = new NodeKind[]
        { 
                NodeKind.Id,
                NodeKind.Cnst,
                NodeKind.Quote,
                NodeKind.FuncTerm,
                NodeKind.Compr,
        };

        private static readonly NodeKind[] IsModAppArg = new NodeKind[]
        { 
                NodeKind.Id,
                NodeKind.Cnst,
                NodeKind.Quote,
                NodeKind.FuncTerm,
                NodeKind.ModRef,
        };

        private static readonly NodeKind[] IsContractSpec = new NodeKind[] { NodeKind.Body, NodeKind.CardPair };

        private static readonly NodeKind[] IsParamType = new NodeKind[] { NodeKind.ModRef, NodeKind.Union, NodeKind.Id, NodeKind.Enum };

        private static readonly NodeKind[] IsTypeTerm = new NodeKind[] { NodeKind.Union, NodeKind.Id, NodeKind.Enum };

        private static readonly NodeKind[] IsUnionComponent = new NodeKind[] { NodeKind.Id, NodeKind.Enum };

        private static readonly NodeKind[] IsEnumElement = new NodeKind[] { NodeKind.Id, NodeKind.Cnst, NodeKind.Range };

        private static readonly NodeKind[] IsAtom = new NodeKind[] { NodeKind.Id, NodeKind.Cnst };

        private static readonly NodeKind[] IsDomOrTrans = new NodeKind[] { NodeKind.Domain, NodeKind.Transform };

        private static readonly NodeKind[] IsModule = new NodeKind[] { NodeKind.Domain, NodeKind.Transform, NodeKind.TSystem, NodeKind.Model, NodeKind.Machine };

        private static readonly NodeKind[] IsTypeDecl = new NodeKind[] { NodeKind.ConDecl, NodeKind.MapDecl, NodeKind.UnnDecl };

        private static readonly NodeKind[] IsConstraint = new NodeKind[] { NodeKind.Find, NodeKind.RelConstr };

        private static ASTSchema theInstance = new ASTSchema();

        public static ASTSchema Instance { get { return theInstance; } }

        private ChildData[][] adjList = new ChildData[(int)NodeKind.AnyNodeKind][];

        /// <summary>
        /// A set of standard type names that are predefined, but not keywords.
        /// </summary>

        public string ConstNameTrue
        {
            get { return "TRUE"; }
        }

        public string ConstNameFalse
        {
            get { return "FALSE"; }
        }

        public string DontCareName
        {
            get { return "_"; }
        }
        
        public string TypeNameConstant
        {
            get { return "Constant"; }
        }
        
        public string TypeNameData
        {
            get { return "Data"; }
        }

        public string TypeNameAny
        {
            get { return "Any"; }
        }

        public string TypeNameBoolean
        {
            get { return "Boolean"; }
        }

        /// <summary>
        /// Rules about which node kinds can be replaced with other node kinds. 
        /// It is assumed that two nodes of the same kind in the same context can always
        /// be replaced.
        /// </summary>
        private GenericSet<ReplaceData> replaceables = new GenericSet<ReplaceData>(Common.ReplaceDataComparer.GetReplaceDataComparer());
        //private Set<ReplaceData> replaceables = new Set<ReplaceData>(ReplaceData.Compare);


        private Map<string, OpKind> namedFuncs = new Map<string, OpKind>(string.Compare);

        private Tuple<string, OpStyleKind>[] opKindStrings;

        //private Set<string> reservedWords = new Set<string>(string.CompareOrdinal);
        private GenericSet<string> reservedWords = new GenericSet<string>(Common.StringComparer.GetStringComparer());

        private string[] relKindStrings;

        private string[] mapKindStrings;

        private string[] compKindStrings;

        private string[] contrKindStrings;

        internal ASTSchema()
        {
            contrKindStrings = new string[typeof(ContractKind).GetEnumValues().Length];
            compKindStrings = new string[typeof(ComposeKind).GetEnumValues().Length];
            mapKindStrings = new string[typeof(MapKind).GetEnumValues().Length];
            relKindStrings = new string[typeof(RelKind).GetEnumValues().Length];
            opKindStrings = new Tuple<string, OpStyleKind>[typeof(OpKind).GetEnumValues().Length];

            adjList[(int)NodeKind.Folder] = new ChildData[]
            {
                new ChildData(NodeKind.Program, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Folder, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Program] = new ChildData[]
            {
                new ChildData(NodeKind.Domain, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Transform, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.TSystem, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Model, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Machine, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Program, ChildContextKind.AnyChildContext, IsModule, IsModule);

            adjList[(int)NodeKind.Domain] = new ChildData[]
            {
                new ChildData(NodeKind.Rule, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.UnnDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ConDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.MapDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ModRef, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ContractItem, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Domain, ChildContextKind.AnyChildContext, IsTypeDecl, IsTypeDecl);

            adjList[(int)NodeKind.Transform] = new ChildData[]
            {
                new ChildData(NodeKind.Param, ChildContextKind.Inputs),
                new ChildData(NodeKind.Param, ChildContextKind.Outputs),

                new ChildData(NodeKind.Rule, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.UnnDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ConDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.MapDecl, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ContractItem, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Transform, ChildContextKind.AnyChildContext, IsTypeDecl, IsTypeDecl);

            adjList[(int)NodeKind.TSystem] = new ChildData[]
            {
                new ChildData(NodeKind.Param, ChildContextKind.Inputs),
                new ChildData(NodeKind.Param, ChildContextKind.Outputs),

                new ChildData(NodeKind.Step, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Model] = new ChildData[]
            {
                new ChildData(NodeKind.ModRef, ChildContextKind.Domain),
                new ChildData(NodeKind.ModRef, ChildContextKind.Includes),

                new ChildData(NodeKind.ContractItem, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ModelFact, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Machine] = new ChildData[]
            {
                new ChildData(NodeKind.Update, ChildContextKind.Initials),
                new ChildData(NodeKind.Update, ChildContextKind.Nexts),

                new ChildData(NodeKind.ModRef, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Step, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Param, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Property, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Config] = new ChildData[]
            {
                new ChildData(NodeKind.Setting, ChildContextKind.AnyChildContext)            
            };

            adjList[(int)NodeKind.Setting] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Cnst, ChildContextKind.AnyChildContext)            
            };

            adjList[(int)NodeKind.ConDecl] = new ChildData[]
            {
                new ChildData(NodeKind.Field, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.MapDecl] = new ChildData[]
            {
                new ChildData(NodeKind.Field, ChildContextKind.Dom),            
                new ChildData(NodeKind.Field, ChildContextKind.Cod),            
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.UnnDecl] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Enum, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Union, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.UnnDecl, ChildContextKind.AnyChildContext, IsTypeTerm, IsTypeTerm);

            adjList[(int)NodeKind.Union] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Enum, ChildContextKind.AnyChildContext),            
            };

            AddReplaceables(NodeKind.Union, ChildContextKind.AnyChildContext, IsUnionComponent, IsUnionComponent);

            adjList[(int)NodeKind.Field] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Enum, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Union, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Field, ChildContextKind.AnyChildContext, IsTypeTerm, IsTypeTerm);

            adjList[(int)NodeKind.Enum] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Cnst, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Range, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Enum, ChildContextKind.AnyChildContext, IsEnumElement, IsEnumElement);

            adjList[(int)NodeKind.Param] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Enum, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Union, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.ModRef, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.Param, ChildContextKind.AnyChildContext, IsTypeTerm, IsTypeTerm);

            adjList[(int)NodeKind.Rule] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Cnst, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Quote, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Compr, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Body, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)          
            };

            AddReplaceables(NodeKind.Rule, ChildContextKind.AnyChildContext, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.Compr] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Cnst, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Quote, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Compr, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Body, ChildContextKind.AnyChildContext)          
            };

            AddReplaceables(NodeKind.Compr, ChildContextKind.AnyChildContext, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.Quote] = new ChildData[]
            {
                new ChildData(NodeKind.QuoteRun, ChildContextKind.Args),            
                new ChildData(NodeKind.Id, ChildContextKind.Args),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Args),            
                new ChildData(NodeKind.Quote, ChildContextKind.Args),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Args),
                new ChildData(NodeKind.Compr, ChildContextKind.Args)           
            };

            AddReplaceables(NodeKind.Quote, ChildContextKind.Args, IsQuoteItem, IsQuoteItem);

            adjList[(int)NodeKind.Body] = new ChildData[]
            {
                new ChildData(NodeKind.Find, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.RelConstr, ChildContextKind.AnyChildContext)                        
            };

            AddReplaceables(NodeKind.Body, ChildContextKind.AnyChildContext, IsConstraint, IsConstraint);

            adjList[(int)NodeKind.Find] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.Binding),            
                new ChildData(NodeKind.Id, ChildContextKind.Match),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Match),            
                new ChildData(NodeKind.Quote, ChildContextKind.Match),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Match),
                new ChildData(NodeKind.Compr, ChildContextKind.Match) 
            };

            AddReplaceables(NodeKind.Find, ChildContextKind.Match, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.ModelFact] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.Binding),            
                new ChildData(NodeKind.Id, ChildContextKind.Match),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Match),            
                new ChildData(NodeKind.Quote, ChildContextKind.Match),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Match),
                new ChildData(NodeKind.Compr, ChildContextKind.Match),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.ModelFact, ChildContextKind.Match, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.FuncTerm] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.Operator),            

                new ChildData(NodeKind.Id, ChildContextKind.Args),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Args),            
                new ChildData(NodeKind.Quote, ChildContextKind.Args),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Args),         
                new ChildData(NodeKind.Compr, ChildContextKind.Args) 
            };

            AddReplaceables(NodeKind.FuncTerm, ChildContextKind.Args, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.RelConstr] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.Args),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Args),            
                new ChildData(NodeKind.Quote, ChildContextKind.Args),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Args),   
                new ChildData(NodeKind.Compr, ChildContextKind.Args)   
            };

            AddReplaceables(NodeKind.RelConstr, ChildContextKind.Args, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.Step] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.ModApply, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Update] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.ModApply, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.ModApply] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.Args),            
                new ChildData(NodeKind.Cnst, ChildContextKind.Args),            
                new ChildData(NodeKind.Quote, ChildContextKind.Args),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.Args),                       
                new ChildData(NodeKind.ModRef, ChildContextKind.Args),   

                new ChildData(NodeKind.ModRef, ChildContextKind.Operator)
            };

            AddReplaceables(NodeKind.ModApply, ChildContextKind.Args, IsModAppArg, IsModAppArg);

            adjList[(int)NodeKind.Property] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Cnst, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.Quote, ChildContextKind.AnyChildContext),            
                new ChildData(NodeKind.FuncTerm, ChildContextKind.AnyChildContext),           
                new ChildData(NodeKind.Compr, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)   
            };

            AddReplaceables(NodeKind.Property, ChildContextKind.AnyChildContext, IsFuncTerm, IsFuncTerm);

            adjList[(int)NodeKind.ContractItem] = new ChildData[]
            {
                new ChildData(NodeKind.Body, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.CardPair, ChildContextKind.AnyChildContext),
                new ChildData(NodeKind.Config, ChildContextKind.AnyChildContext)
            };

            AddReplaceables(NodeKind.ContractItem, ChildContextKind.AnyChildContext, IsContractSpec, IsContractSpec);

            adjList[(int)NodeKind.CardPair] = new ChildData[]
            {
                new ChildData(NodeKind.Id, ChildContextKind.AnyChildContext)
            };

            adjList[(int)NodeKind.Range] = new ChildData[0];
            adjList[(int)NodeKind.Id] = new ChildData[0];
            adjList[(int)NodeKind.Cnst] = new ChildData[0];
            adjList[(int)NodeKind.ModRef] = new ChildData[0];
            adjList[(int)NodeKind.QuoteRun] = new ChildData[0];

            RegisterNames();
        }

        /// <summary>
        /// Returns true if an application of the inner function should be parenthesized
        /// when used as an argument to the outer function.
        /// </summary>
        public bool NeedsParen(OpKind outer, OpKind inner)
        {
            var schema = ASTQueries.ASTSchema.Instance;
            if (schema.GetStyle(outer) == ASTQueries.OpStyleKind.Apply ||
                schema.GetStyle(inner) == ASTQueries.OpStyleKind.Apply)
            {
                return false;
            }

            switch (outer)
            {
                case OpKind.Neg:
                    return true;
                case OpKind.Mul:
                    return inner == OpKind.Add ||
                           inner == OpKind.Sub ||
                           inner == OpKind.Mod ||
                           inner == OpKind.Div;
                case OpKind.Div:
                    return inner == OpKind.Add ||
                           inner == OpKind.Sub ||
                           inner == OpKind.Mod;
                case OpKind.Mod:
                    return inner == OpKind.Add ||
                           inner == OpKind.Sub;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Tries to convert the string representation of a built-in operator
        /// to a member of the OpKind enum.
        /// </summary>
        public bool TryGetOpKind(string name, out OpKind kind)
        {
            Contract.Requires(name != null);
            return namedFuncs.TryFindValue(name, out kind);
        }

        /// <summary>
        /// Tries to convert a base sort kind to its language-level type name.
        /// </summary>
        public bool TryGetSortName(Common.Terms.BaseSortKind kind, out string name)
        {
            switch (kind)
            {
                case Common.Terms.BaseSortKind.Real:
                    name = "Real";
                    return true;
                case Common.Terms.BaseSortKind.Integer:
                    name = "Integer";
                    return true;
                case Common.Terms.BaseSortKind.Natural:
                    name = "Natural";
                    return true;
                case Common.Terms.BaseSortKind.PosInteger:
                    name = "PosInteger";
                    return true;
                case Common.Terms.BaseSortKind.NegInteger:
                    name = "NegInteger";
                    return true;
                case Common.Terms.BaseSortKind.String:
                    name = "String";
                    return true;
                default:
                    name = string.Empty;
                    return false;
            }
        }

        /// <summary>
        /// Encodes a string by escaping certain control characters. If useMultiline is
        /// true then encodes the string using a multiline string: '" "'
        /// </summary>
        public string Encode(string s, bool useMultiline = false)
        {
            if (string.IsNullOrEmpty(s))
            {
                return useMultiline ? "\'\"\"\'" : "\"\"";
            }
            else if (useMultiline)
            {
                return string.Format("\'\"{0}\"\'", s.Replace("\'\"", "\'\'\"\"").Replace("\"\'", "\"\"\'\'"));
            }
            else
            {
                var sb = new StringBuilder();
                char c;
                sb.Append('\"');
                for (int i = 0; i < s.Length; ++i)
                {
                    c = s[i];
                    switch (c)
                    {
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        case '\\':
                            sb.Append("\\\\");
                            break;
                        case '\"':
                            sb.Append("\\\"");
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }

                sb.Append('\"');
                return sb.ToString();
            }
        }

        /// <summary>
        /// Returns the string representation of relational operators.
        /// </summary>
        public string ToString(RelKind kind)
        {
            return relKindStrings[(int)kind];
        }

        /// <summary>
        /// Returns the string representation of reserved operators
        /// </summary>
        public string ToString(ReservedOpKind kind)
        {
            switch (kind)
            {
                case ReservedOpKind.TypeUnn:
                    return "_+_";
                case ReservedOpKind.Select:
                    return ".";
                case ReservedOpKind.Range:
                    return "..";
                case ReservedOpKind.Relabel:
                    return "-->";
                case ReservedOpKind.Find:
                    return "?";
                case ReservedOpKind.Conj:
                    return "/\\";
                case ReservedOpKind.ConjR:
                    return "||";
                case ReservedOpKind.Disj:
                    return "\\/";
                case ReservedOpKind.Compr:
                    return "{}";
                case ReservedOpKind.PRule:
                    return ":--";
                case ReservedOpKind.CRule:
                    return "|-";
                case ReservedOpKind.Rule:
                    return ":-";
                case ReservedOpKind.Proj:
                    return "$";
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns the string representation of a built-in operator along with its style.
        /// </summary>
        public string ToString(OpKind kind, out OpStyleKind style)
        {
            var info = opKindStrings[(int)kind];
            style = info.Item2;
            return info.Item1;
        }

        /// <summary>
        /// Returns the style of this built-in operator
        /// </summary>
        public OpStyleKind GetStyle(OpKind kind)
        {
            return opKindStrings[(int)kind].Item2;
        }

        public bool IsReservedWord(string s)
        {
            return s == null || namedFuncs.ContainsKey(s) || reservedWords.Contains(s);
        }

        public bool IsId(
                        string s,
                        bool allowQualification,
                        bool allowTypeCnst,
                        bool allowSymbCnst,
                        bool allowDontCare)
        {
            bool isQualified, isTypeCnst, isSymbCnst, isDontCare;
            return IsId(
                s, 
                allowQualification, 
                allowTypeCnst, 
                allowSymbCnst, 
                allowDontCare, 
                out isQualified, 
                out isTypeCnst, 
                out isSymbCnst, 
                out isDontCare);
        }

        [Pure]
        public bool IsId(
                        string s,
                        bool allowQualification,
                        bool allowTypeCnst,
                        bool allowSymbCnst,
                        bool allowDontCare,
                        out bool isQualified,
                        out bool isTypeCnst,
                        out bool isSymbCnst,
                        out bool isDontCare)
        {
            isTypeCnst = isSymbCnst = isQualified = isDontCare = false;
            if (s == null || s.Length == 0 || IsReservedWord(s))
            {
                return false;
            }

            bool isFirst = true;
            bool isPrimed = false;
            bool isArg = false;

            char c;
            char l = s[0];
            for (int i = 0; i < s.Length; ++i)
            {
                c = l;
                l = i + 1 == s.Length ? '\0' : s[i + 1];
                if (isFirst)
                {
                    isFirst = false;
                    if (c == '_')
                    {
                        if (l == '.')
                        {
                            return false; //// _. is never a valid id.
                        }
                        else if (i + 1 == s.Length)
                        {
                            isDontCare = true;
                            return allowDontCare; //// _ is valid only if allowDontCare.
                        }
                    }
                    else if (c == '#')
                    {
                        if (allowTypeCnst)
                        {
                            if (l != '_' && !char.IsLetter(l))
                            {
                                return false; //// A typename must follow a #.
                            }

                            isTypeCnst = true;
                        }
                        else
                        {
                            return false; //// Type constants are not allowed.
                        }
                    }
                    else if (c == '%')
                    {
                        if (allowSymbCnst && !isSymbCnst)
                        {
                            if (l != '_' && !char.IsLetter(l))
                            {
                                return false; //// A typename must follow a %.
                            }

                            isSymbCnst = true;
                        }
                        else
                        {
                            return false; //// Type constants are not allowed.
                        }
                    }
                    else if (!char.IsLetter(c))
                    {
                        return false; //// Not a legal starting char.
                    }
                }
                else if (isArg)
                {
                    if (c == ']')
                    {
                        return i + 1 == s.Length; //// If #T[...], then must be end of identifier.
                    }
                    else if (!char.IsDigit(c))
                    {
                        return false; //// If #T[...c.. then c must be a digit.
                    }
                }
                else if (c == '[')
                {
                    if (!isTypeCnst || !char.IsDigit(l))
                    {
                        return false; //// Type cnst must be allowed and 
                    }

                    isArg = true;
                }
                else if (c == '.')
                {
                    if (isTypeCnst || !allowQualification)
                    {
                        return false; //// There cannot be further fragments after a type constant, or qual is not allowed
                    }
                    else
                    {
                        isQualified = true;
                        isFirst = true;
                        isPrimed = false;
                    }
                }
                else if (c == '\'')
                {
                    isPrimed = true;
                }
                else if (c == '_' || char.IsLetterOrDigit(c))
                {
                    if (isPrimed)
                    {
                        return false; //// A prime cannot be followed by a non-prime.
                    }
                }
                else
                {
                    return false; //// No other characters are valid.
                }
            }

            return true;
        }

        /// <summary>
        /// Version of IsId that records if any fragment satisfies the fragPredicate
        /// </summary>
        [Pure]
        public bool IsId(
                        string s,
                        bool allowQualification,
                        bool allowTypeCnst,
                        bool allowSymbCnst,
                        bool allowDontCare,
                        Predicate<string> fragPredicate,
                        out bool isQualified,
                        out bool isTypeCnst,
                        out bool isSymbCnst,
                        out bool isDontCare,
                        out bool satisfiesPred)
        {
            satisfiesPred = isTypeCnst = isSymbCnst = isQualified = isDontCare = false;
            if (s == null || s.Length == 0 || IsReservedWord(s))
            {
                return false;
            }

            bool isFirst = true;
            bool isPrimed = false;
            bool isArg = false;
            int fragStart = 0;

            char c;
            char l = s[0];
            for (int i = 0; i < s.Length; ++i)
            {
                c = l;
                l = i + 1 == s.Length ? '\0' : s[i + 1];
                if (isFirst)
                {
                    isFirst = false;
                    fragStart = i;
                    if (c == '_')
                    {
                        if (l == '.')
                        {
                            return false; //// _. is never a valid id.
                        }
                        else if (i + 1 == s.Length)
                        {
                            isDontCare = true;
                            return allowDontCare; //// _ is valid only if allowDontCare.
                        }
                    }
                    else if (c == '#')
                    {
                        if (allowTypeCnst)
                        {
                            if (l != '_' && !char.IsLetter(l))
                            {
                                return false; //// A typename must follow a #.
                            }

                            isTypeCnst = true;
                        }
                        else
                        {
                            return false; //// Type constants are not allowed.
                        }
                    }
                    else if (c == '%')
                    {
                        if (allowSymbCnst && !isSymbCnst)
                        {
                            if (l != '_' && !char.IsLetter(l))
                            {
                                return false; //// A typename must follow a %.
                            }

                            isSymbCnst = true;
                        }
                        else
                        {
                            return false; //// Type constants are not allowed.
                        }
                    }
                    else if (!char.IsLetter(c))
                    {
                        return false; //// Not a legal starting char.
                    }
                }
                else if (isArg)
                {
                    if (c == ']')
                    {
                        return i + 1 == s.Length; //// If #T[...], then must be end of identifier.
                    }
                    else if (!char.IsDigit(c))
                    {
                        return false; //// If #T[...c.. then c must be a digit.
                    }
                }
                else if (c == '[')
                {
                    if (!isTypeCnst || !char.IsDigit(l))
                    {
                        return false; //// Type cnst must be allowed and 
                    }

                    isArg = true;
                }
                else if (c == '.')
                {
                    if (isTypeCnst || !allowQualification)
                    {
                        return false; //// There cannot be further fragments after a type constant, or qual is not allowed
                    }
                    else
                    {
                        if (!satisfiesPred && fragPredicate(s.Substring(fragStart, i - fragStart)))
                        {
                            satisfiesPred = true;
                        }

                        isQualified = true;
                        isFirst = true;
                        isPrimed = false;
                    }
                }
                else if (c == '\'')
                {
                    isPrimed = true;
                }
                else if (c == '_' || char.IsLetterOrDigit(c))
                {
                    if (isPrimed)
                    {
                        return false; //// A prime cannot be followed by a non-prime.
                    }
                }
                else
                {
                    return false; //// No other characters are valid.
                }
            }

            if (!satisfiesPred && fragPredicate(s.Substring(fragStart, s.Length - fragStart)))
            {
                satisfiesPred = true;
            }

            return true;
        }

        /// <summary>
        /// Strips the accessors off an Id, and returns only the name of the Id.
        /// The isQualified flag is only used in cases where the Id is neither a
        /// type constant nor a symbolic constant.
        /// </summary>
        internal string StripAccessors(Id id, bool isQualified, out int firstAccIndex)
        {
            if (id.Name.Contains('%'))
            {
                var name = string.Empty;
                for (int i = 0; i < id.Fragments.Length; ++i)
                {
                    name += name == string.Empty ? id.Fragments[i] : "." + id.Fragments[i];

                    if (id.Fragments[i][0] == '%')
                    {
                        firstAccIndex = i + 1;
                        return name;
                    }
                }

                firstAccIndex = id.Fragments.Length;
                return name;
            }
            else if (id.Name.Contains("#") || isQualified)
            {
                firstAccIndex = id.Fragments.Length;
                return id.Name;
            }
            else
            {
                firstAccIndex = 1;
                return id.Fragments[0];
            }
        }

        internal bool IsQueryFeasible(
                                NodeKind kind,
                                ChildContextKind context,
                                NodePred[] query,
                                int pos)
        {
            Contract.Requires(query != null && pos >= 0 && pos < query.Length);
            if (query.Length == 0)
            {
                return false;
            }

            return IsFeasible(kind, context, query, pos);
        }

        internal string ToString(MapKind kind)
        {
            return mapKindStrings[(int)kind];
        }

        internal string ToString(ComposeKind kind)
        {
            return compKindStrings[(int)kind];
        }

        internal string ToString(ContractKind kind)
        {
            return contrKindStrings[(int)kind];
        }

        /// <summary>
        /// Returns true if the child in context can be replaced with a node of kind replaceKind.
        /// </summary>
        internal bool CanReplace(Node parent, Node child, ChildContextKind context, NodeKind replaceKind)
        {
            Contract.Requires(parent != null && child != null);
            if (child.NodeKind == replaceKind)
            {
                return true;
            }

            return replaceables.Contains(new ReplaceData(parent.NodeKind, context, child.NodeKind, replaceKind));
        }

        private bool IsStarFeasible(
                                NodeKind kind,
                                ChildContextKind context,
                                NodePred[] query,
                                int pos,
                                SearchState state)
        {
            if (state == null)
            {
                state = new SearchState();
            }

            if (state[kind, context])
            {
                return false;
            }

            state[kind, context] = true;
            if (IsFeasible(kind, context, query, pos))
            {
                return true;
            }

            var children = adjList[(int)kind];
            for (int i = 0; i < children.Length; ++i)
            {
                if (IsStarFeasible(children[i].childKind, children[i].childContext, query, pos, state))
                {
                    return true;
                }
            }

            return false;
        }
       
        private bool IsFeasible(
                                NodeKind kind,
                                ChildContextKind context,
                                NodePred[] query,
                                int pos)
        {
            var q = query[pos];
            if (q.PredicateKind == NodePredicateKind.Star)
            {
                while (pos < query.Length && query[pos].PredicateKind == NodePredicateKind.Star)
                {
                    ++pos;
                }

                if (pos >= query.Length)
                {
                    return true;
                }
                else
                {
                    return IsStarFeasible(kind, context, query, pos, null);
                }
            }
            else if (!IsFeasible(kind, context, q))
            {
                return false;
            }

            ++pos;
            if (pos >= query.Length)
            {
                return true;
            }

            q = query[pos];
            var children = adjList[(int)kind];
            for (int i = 0; i < children.Length; ++i)
            {
                if (IsFeasible(children[i].childKind, children[i].childContext, query, pos))
                {
                    return true;
                }                
            }

            return false;
        }

        private bool IsFeasible(NodeKind kind, ChildContextKind context, NodePred pred)
        {
            if (pred.PredicateKind == NodePredicateKind.False)
            {
                return false;
            }
            else if (pred.PredicateKind == NodePredicateKind.Atom)
            {
                var atom = (NodePredAtom)pred;
                if (atom.TargetKind == NodeKind.AnyNodeKind && atom.ChildContext == ChildContextKind.AnyChildContext)
                {
                    return true;
                }
                else if (atom.TargetKind == NodeKind.AnyNodeKind)
                {
                    return atom.ChildContext == context;
                }
                else if (atom.ChildContext == ChildContextKind.AnyChildContext)
                {
                    return atom.TargetKind == kind;
                }
                else
                {
                    return atom.ChildContext == context && atom.TargetKind == kind;
                }                
            }
            else if (pred.PredicateKind == NodePredicateKind.Or)
            {
                var or = (NodePredOr)pred;
                if (or.Arg1.PredicateKind == NodePredicateKind.Or)
                {
                    return IsFeasible(kind, context, or.Arg2) ||
                           IsFeasible(kind, context, or.Arg1);
                }
                else
                {
                    return IsFeasible(kind, context, or.Arg1) ||
                           IsFeasible(kind, context, or.Arg2);
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private void AddReplaceables(NodeKind parentKind, ChildContextKind context, NodeKind[] childKinds, NodeKind[] replaceKinds)
        {
            for (int i = 0; i < childKinds.Length; ++i)
            {
                for (int j = 0; j < replaceKinds.Length; ++j)
                {
                    replaceables.Add(new ReplaceData(parentKind, context, childKinds[i], replaceKinds[j]));
                }
            }
        }

        private void RegisterNames()
        {
            Register(OpKind.Add, "+", OpStyleKind.Infix);
            Register(OpKind.And, "and", OpStyleKind.Apply);
            Register(OpKind.AndAll, "andAll", OpStyleKind.Apply);
            Register(OpKind.Count, "count", OpStyleKind.Apply);
            Register(OpKind.Div, "/", OpStyleKind.Infix);
            Register(OpKind.GCD, "gcd", OpStyleKind.Apply);
            Register(OpKind.GCDAll, "gcdAll", OpStyleKind.Apply);
            Register(OpKind.Impl, "impl", OpStyleKind.Apply);
            Register(OpKind.IsSubstring, "isSubstring", OpStyleKind.Apply);
            Register(OpKind.LCM, "lcm", OpStyleKind.Apply);
            Register(OpKind.LCMAll, "lcmAll", OpStyleKind.Apply);
            Register(OpKind.Max, "max", OpStyleKind.Apply);
            Register(OpKind.MaxAll, "maxAll", OpStyleKind.Apply);
            Register(OpKind.Min, "min", OpStyleKind.Apply);
            Register(OpKind.MinAll, "minAll", OpStyleKind.Apply);
            Register(OpKind.Mod, "%", OpStyleKind.Infix);
            Register(OpKind.Mul, "*", OpStyleKind.Infix);
            Register(OpKind.Neg, "-", OpStyleKind.Prefix);
            Register(OpKind.Not, "not", OpStyleKind.Apply);
            Register(OpKind.Or, "or", OpStyleKind.Apply);
            Register(OpKind.Prod, "prod", OpStyleKind.Apply);
            Register(OpKind.Qtnt, "qtnt", OpStyleKind.Apply);
            Register(OpKind.Sub, "-", OpStyleKind.Infix);

            Register(OpKind.LstLength, "lstLength", OpStyleKind.Apply);
            Register(OpKind.LstReverse, "lstReverse", OpStyleKind.Apply);
            Register(OpKind.LstFind, "lstFind", OpStyleKind.Apply);
            Register(OpKind.LstFindAll, "lstFindAll", OpStyleKind.Apply);
            Register(OpKind.LstFindAllNot, "lstFindAllNot", OpStyleKind.Apply);
            Register(OpKind.LstGetAt, "lstGetAt", OpStyleKind.Apply);

            Register(OpKind.RflIsMember, "rflIsMember", OpStyleKind.Apply);
            Register(OpKind.RflIsSubtype, "rflIsSubtype", OpStyleKind.Apply);
            Register(OpKind.RflGetArgType, "rflGetArgType", OpStyleKind.Apply);
            Register(OpKind.RflGetArity, "rflGetArity", OpStyleKind.Apply);

            Register(OpKind.StrAfter, "strAfter", OpStyleKind.Apply);
            Register(OpKind.StrBefore, "strBefore", OpStyleKind.Apply);
            Register(OpKind.StrFind, "strFind", OpStyleKind.Apply);
            Register(OpKind.StrGetAt, "strGetAt", OpStyleKind.Apply);
            Register(OpKind.StrJoin, "strJoin", OpStyleKind.Apply);
            Register(OpKind.StrReplace, "strReplace", OpStyleKind.Apply);
            Register(OpKind.StrLength, "strLength", OpStyleKind.Apply);
            Register(OpKind.StrLower, "strLower", OpStyleKind.Apply);
            Register(OpKind.StrReverse, "strReverse", OpStyleKind.Apply);
            Register(OpKind.StrUpper, "strUpper", OpStyleKind.Apply);
            Register(OpKind.Sum, "sum", OpStyleKind.Apply);
            Register(OpKind.Sign, "sign", OpStyleKind.Apply);

            Register(OpKind.ToList, "toList", OpStyleKind.Apply);
            Register(OpKind.ToOrdinal, "toOrdinal", OpStyleKind.Apply);
            Register(OpKind.ToNatural, "toNatural", OpStyleKind.Apply);
            Register(OpKind.ToString, "toString", OpStyleKind.Apply);
            Register(OpKind.ToSymbol, "toSymbol", OpStyleKind.Apply);

            Register(RelKind.Eq, "=");
            Register(RelKind.Ge, ">=");
            Register(RelKind.Gt, ">");
            Register(RelKind.Le, "<=");
            Register(RelKind.Lt, "<");
            Register(RelKind.Neq, "!=");
            Register(RelKind.No, "no");
            Register(RelKind.Typ, ":");

            Register(MapKind.Bij, "bij");
            Register(MapKind.Fun, "fun");
            Register(MapKind.Inj, "inj");
            Register(MapKind.Sur, "sur");

            Register(ComposeKind.Extends, "extends");
            Register(ComposeKind.Includes, "includes");
            Register(ComposeKind.None, "");

            Register(ContractKind.EnsuresProp, "ensures");
            Register(ContractKind.RequiresAtLeast, "requires atleast");
            Register(ContractKind.RequiresAtMost, "requires atmost");
            Register(ContractKind.RequiresProp, "requires");
            Register(ContractKind.RequiresSome, "requires some");
            Register(ContractKind.ConformsProp, "conforms");

            RegisterReservedWords();
        }

        private void RegisterReservedWords()
        {
            reservedWords.Add("domain");
            reservedWords.Add("model");
            reservedWords.Add("transform");
            reservedWords.Add("system");
            reservedWords.Add("includes");
            reservedWords.Add("extends");
            reservedWords.Add("of");
            reservedWords.Add("returns");
            reservedWords.Add("at");
            reservedWords.Add("machine");
            reservedWords.Add("is");
            reservedWords.Add("no");
            reservedWords.Add("sub");
            reservedWords.Add("new");
            reservedWords.Add("fun");
            reservedWords.Add("inj");
            reservedWords.Add("bij");
            reservedWords.Add("sur");
            reservedWords.Add("any");
            reservedWords.Add("ensures");
            reservedWords.Add("requires");
            reservedWords.Add("some");
            reservedWords.Add("atleast");
            reservedWords.Add("atmost");
            reservedWords.Add("partial");
            reservedWords.Add("initially");
            reservedWords.Add("next");
            reservedWords.Add("property");
            reservedWords.Add("boot");
        }

        private void Register(OpKind kind, string name, OpStyleKind style)
        {
            opKindStrings[(int)kind] = new Tuple<string, OpStyleKind>(name, style);
            if (style == OpStyleKind.Apply)
            {
                namedFuncs.Add(name, kind);
            }
        }

        private void Register(RelKind kind, string name)
        {
            relKindStrings[(int)kind] = name;
        }

        private void Register(MapKind kind, string name)
        {
            mapKindStrings[(int)kind] = name;
        }

        private void Register(ComposeKind kind, string name)
        {
            compKindStrings[(int)kind] = name;
        }

        private void Register(ContractKind kind, string name)
        {
            contrKindStrings[(int)kind] = name;
        }

        public class SearchState
        {
            //private Set<Pair> seen = new Set<Pair>(Pair.Compare);
            private GenericSet<Pair> seen = new GenericSet<Pair>(Common.PairComparer.GetPairComparer());

            public bool this[NodeKind k, ChildContextKind c]
            {
                get 
                {
                    return seen.Contains(new Pair(k, c));                
                }

                set 
                {
                    if (value)
                    {
                        seen.Add(new Pair(k, c));
                    }
                    else 
                    {
                        seen.Remove(new Pair(k, c));
                    }                
                }            
            }

            public struct Pair
            {
                public NodeKind nodeKind;
                public ChildContextKind context;

                public Pair(NodeKind nodeKind, ChildContextKind context)
                {
                    this.nodeKind = nodeKind;
                    this.context = context;
                }

                public static int Compare(Pair p1, Pair p2)
                {
                    if (p1.nodeKind != p2.nodeKind)
                    {
                        return (int)p1.nodeKind - (int)p2.nodeKind;
                    }

                    return (int)p1.context - (int)p2.context;
                }
            }
        }

        public struct ReplaceData
        {
            public NodeKind parentKind;
            public ChildContextKind context;
            public NodeKind childKind;
            public NodeKind replaceKind;

            public ReplaceData(
                NodeKind parentKind, 
                ChildContextKind context, 
                NodeKind childKind,
                NodeKind replaceKind)
            {
                this.parentKind = parentKind;
                this.context = context;
                this.childKind = childKind;
                this.replaceKind = replaceKind;
            }

            public static int Compare(ReplaceData d1, ReplaceData d2)
            {
                if (d1.parentKind != d2.parentKind)
                {
                    return (int)d1.parentKind - (int)d2.parentKind;
                }

                if (d1.context != d2.context)
                {
                    return (int)d1.context - (int)d2.context;
                }

                if (d1.childKind != d2.childKind)
                {
                    return (int)d1.childKind - (int)d2.childKind;
                }

                return (int)d1.replaceKind - (int)d2.replaceKind;
            }
        }

        private struct ChildData
        {
            public NodeKind childKind;
            public ChildContextKind childContext;

            public ChildData(NodeKind childKind, ChildContextKind childContext)
            {
                this.childKind = childKind;
                this.childContext = childContext;
            }
        }
    }
}
