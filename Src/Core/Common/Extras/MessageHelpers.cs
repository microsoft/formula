namespace Microsoft.Formula.Common.Extras
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;

    using API;
    using API.Nodes;
    using Compiler;
        
    internal static class MessageHelpers
    {
        internal static string MkDupErrMsg(string item, object offDef1, object offDef2, EnvParams envParams)
        {
            return Constants.DuplicateDefs.ToString(
                item, 
                GetCodeLocationString(offDef1, envParams),
                GetCodeLocationString(offDef2, envParams));
        }

        internal static string GetCodeLocationString(this object obj, EnvParams envParams)
        {
            if (obj is Location)
            {
                return ((Location)obj).GetFileLocationString(envParams);
            }

            Span span;
            if (obj is Node)
            {
                span = ((Node)obj).Span;
            }
            else if (obj is AST<Node>)
            {
                span = ((AST<Node>)obj).Node.Span;
            }
            else
            {
                return ("(?,?)");
            }

            return string.Format("({0}, {1})", span.StartLine, span.StartCol);
        }

        internal static string Debug_GetSmallTermString(this Terms.Term t)
        {
            var sw = new System.IO.StringWriter();
            Terms.TermIndex.Debug_PrintSmallTerm(t, sw);
            return sw.ToString();
        }

        internal static string Debug_GetSmallTermString(this IEnumerable<Terms.Term> terms)
        {
            if (terms == null)
            {
                return string.Empty;
            }
            
            var sw = new System.IO.StringWriter();
            foreach (var t in terms)
            {
                Terms.TermIndex.Debug_PrintSmallTerm(t, sw);
                sw.Write(",   ");
            }

            return sw.ToString();
        }
    }
}
