namespace Microsoft.Formula.Compiler
{
    using System.Collections.Generic;

    using API;
    using API.Nodes;
    using API.ASTQueries;

    public sealed class RuleLinter
    {
        private static NodePred[] IdQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.Id) &
            NodePredFactory.Instance.MkPredicate(ChildContextKind.Args)
        };

        private static NodePred[] DeclQueryRule = new NodePred[]
        {
            NodePredFactory.Instance.Star,
            NodePredFactory.Instance.MkPredicate(NodeKind.Find)
        };

        public static bool ValidateBodyQualifiedIds(Body b, out List<string> v)
        {
            var path = new LinkedList<ChildInfo>();
            path.AddLast(new ChildInfo(b, ChildContextKind.AnyChildContext, -1, -1));

            HashSet<string> listOfVarIds = new HashSet<string>();
            b.FindAll(
            path,
            IdQueryRule, 
            (ch, n) =>                
            {
                var id = (Id) n;
                if(id.IsQualified)
                {
                    var vr = id.Fragments[0];
                    listOfVarIds.Add(vr);
                }
            });

            HashSet<string> listOfBindingVars = new HashSet<string>();
            b.FindAll(
            path,
            DeclQueryRule, 
            (ch, n) =>                
            {
                var find = (Find)n;
                if(find.IsConstraint && 
                   find.Binding != null &&
                   find.Match.IsTypeTerm)
                {
                    string name = null;
                    find.Binding.TryGetStringAttribute(AttributeKind.Name, out name);
                    listOfBindingVars.Add(name);
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