namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Common.Terms;

    public sealed class RuleLinter
    {
        public static SymbolTable SymTable
        {
            get;
            set;
        }

        private static NodePred[] IdQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.Id) &
            NodePredFactory.Instance.MkPredicate(ChildContextKind.Args)
        };

        private static NodePred[] FindQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.Find)
        };

        private static NodePred[] FuncTermQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.FuncTerm)
        };

        private static NodePred[] RelConstrFuncTermQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.RelConstr)
        };

        public static bool ValidateBodyQualifiedIds(Body b, out List<string> v)
        {
            var path = new LinkedList<ChildInfo>();
            path.AddLast(new ChildInfo(b, ChildContextKind.AnyChildContext, -1, -1));

            HashSet<string> listOfVarIds = new HashSet<string>();
            HashSet<string> listOfBindingVars = new HashSet<string>();
            HashSet<string> listOfFindMatchVars = new HashSet<string>();

            b.FindAll(
            path,
            FindQueryRule, 
            (ch, n) =>                
            {
                if(n.NodeKind is NodeKind.Find)
                {
                    var ft = (Find) n;
                    if(ft.Binding != null)
                    {
                        var bind = (Id) ft.Binding;
                        listOfBindingVars.Add(bind.Name);
                    }

                    if(ft.Match != null)
                    {
                        if(ft.Match is Id)
                        {
                            var id = (Id) ft.Match;
                            if(id.IsQualified)
                            {
                                var vr = id.Fragments[0];
                                listOfFindMatchVars.Add(vr);
                            }
                        }
                    }
                }
            });

            b.FindAll(
            path,
            IdQueryRule, 
            (ch, n) =>                
            {
                var id = (Id) n;
                if(id.IsQualified)
                {
                    var vr = id.Fragments[0];
                    if(!listOfFindMatchVars.Contains(vr))
                    {
                        listOfVarIds.Add(vr);
                    }
                }
            });

            b.FindAll(
            path,
            FuncTermQueryRule, 
            (ch, n) =>                
            {
                if(n.NodeKind is NodeKind.FuncTerm)
                {
                    var ft = (FuncTerm) n;
                    foreach(var c in ft.Children)
                    {
                        if (c is Id)
                        {
                            var id = (Id) c;
                            UserSymbol usym = null;
                            var res = SymTable.Root.TryGetSymbol(id.Name, out usym);
                            if(res)
                            {
                                if(!usym.IsAutoGen)
                                {
                                    listOfBindingVars.Add(id.Name);
                                }
                            }
                        }
                    }
                }
            });

            HashSet<string> listOfBindingVars = new HashSet<string>();
            b.FindAll(
            path,
            RelConstrFuncTermQueryRule, 
            (ch, n) =>                
            {
                if(n.NodeKind is NodeKind.RelConstr)
                {
                    var rn = (RelConstr) n;
                    var op = (RelKind) rn.Op;
                    if(op is RelKind.Eq)
                    {
                        var arg1 = rn.Arg1;
                        if(arg1 is Id)
                        {
                            var id = (Id) arg1;
                            listOfBindingVars.Add(id.Name);
                        }
                    }
                }
            });

            v = new List<string>();
            foreach(var id in listOfVarIds)
            {
                if(!listOfBindingVars.Contains(id))
                {
                    v.Add(id);
                }
            }

            if(v.Count > 0)
            {
                return false;
            }
            return true;
        }
    }
}