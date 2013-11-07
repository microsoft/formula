namespace Microsoft.Formula.Compiler
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Concurrent;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using API;
    using API.ASTQueries;
    using API.Plugins;
    using API.Nodes;
    using Common;
    using Common.Extras;

    internal class Renderer
    {
        private AST<Node> sourceModule;

        private bool rendered = false;

        private RenderResult result;

        private CancellationToken cancel;

        public Renderer(AST<Node> sourceModule, RenderResult result, CancellationToken cancel = default(CancellationToken))
        {
            Contract.Requires(sourceModule != null && sourceModule.Node.IsModule);

            this.result = result;
            this.cancel = cancel;
            this.sourceModule = sourceModule;
        }

        public bool Render()
        {
            if (rendered)
            {
                return result.Succeeded;
            }

            rendered = true;
            var configStack = new Stack<Configuration>();
            AST<Node> simplNode;
            if (!Compiler.TryGetReducedForm(sourceModule, out simplNode))
            {
                simplNode = sourceModule;
            }

            var resNode = simplNode.Compute<Node>(
                (node) =>
                {
                    return RenderUnfold(node, configStack);
                },
                (node, folds) =>
                {
                    return RenderFold(node, folds, configStack);
                },
                cancel);

            if (cancel.IsCancellationRequested)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    sourceModule.Node,
                    Constants.QuotationError.ToString("Cancelled rendering"),
                    Constants.QuotationError.Code);
                result.AddFlag(flag);
            }

            if (result.Succeeded)
            {
                result.Module = Factory.Instance.ToAST(resNode);
            }

            return result.Succeeded;
        }

        private Node RenderFold(
            Node n,
            IEnumerable<Node> folds,
            Stack<Configuration> configStack)
        {
            if (n.NodeKind == NodeKind.Config)
            {
                return n;
            }

            Configuration conf;
            if (n.TryGetConfiguration(out conf))
            {
                Contract.Assert(configStack.Count > 0 && configStack.Peek() == conf);
                configStack.Pop();
            }
          
            if (folds.IsEmpty<Node>())
            {
                return n;
            }
            else if (n.NodeKind == NodeKind.FuncTerm)
            {
                var rendered = folds.First<Node>() as Cnst;
                Contract.Assert(rendered != null && rendered.CnstKind == CnstKind.String);

                ImmutableCollection<Flag> flags;
                var pres = Factory.Instance.ParseDataTerm(rendered.GetStringValue(), out flags);
                Contract.Assert(pres == null || pres.Node.IsFuncOrAtom);

                result.AddFlags(flags);
                
                if (pres == null)
                {
                    result.Failed();
                    return n;
                }
                else
                {
                    return pres.Node;
                }
            }

            Node resultNode = n;
            //// TODO: This could be a performance bottle-neck if many children
            //// have children with quotations. In this case, a new ShallowClone
            //// function must implemented, which simultaneously replaces many children.
            bool mF, mC;
            int pos = 0;
            using (var itF = folds.GetEnumerator())
            {
                using (var itC = n.Children.GetEnumerator())
                {
                    while ((mF = itF.MoveNext()) & (mC = itC.MoveNext()))
                    {
                        if (itF.Current != itC.Current)
                        {
                            resultNode = resultNode.ShallowClone(itF.Current, pos);
                        }

                        ++pos;
                    }

                    Contract.Assert(!mF && !mC);
                }
            }

            return resultNode;
        }

        private IEnumerable<Node> RenderUnfold(
            Node n,
            Stack<Configuration> configStack)
        {
            if (n.NodeKind == NodeKind.Config)
            {
                yield break;
            }

            Cnst value;
            Configuration conf;
            if (n.TryGetConfiguration(out conf))
            {
                configStack.Push(conf);
            }

            if (n.NodeKind != NodeKind.FuncTerm)
            {
                foreach (var c in n.Children)
                {
                    yield return c;
                }

                yield break;
            }

            Contract.Assert(configStack.Count > 0);
            conf = configStack.Peek();
            if (!conf.TryGetSetting(Configuration.Parse_ActiveRenderSetting, out value))
            {
                yield break;
            }
            
            IQuoteParser parser;
            if (!conf.TryGetParserInstance(value.GetStringValue(), out parser))
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.QuotationError.ToString(string.Format("Cannot find a parser named {0}", value.GetStringValue())),
                    Constants.QuotationError.Code);
                result.AddFlag(flag);
                yield break;
            }

            Cnst renderNode = null;
            try
            {
                List<Flag> flags;
                using (var sw = new System.IO.StringWriter())
                {
                    if (!parser.Render(
                                conf,
                                sw,
                                Factory.Instance.ToAST(n),
                                out flags))
                    {
                        var flag = new Flag(
                            SeverityKind.Error,
                            n,
                            Constants.QuotationError.ToString(string.Empty),
                            Constants.QuotationError.Code);
                        result.AddFlag(flag);
                    }
                    else
                    {
                        renderNode = Factory.Instance.MkCnst(sw.GetStringBuilder().ToString()).Node;
                    }
                }

                result.AddFlags(flags);
            }
            catch (Exception e)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.PluginException.ToString(Configuration.ParsersCollectionName, value.GetStringValue(), e.Message),
                    Constants.PluginException.Code);
                result.AddFlag(flag);
            }

            if (renderNode != null)
            {
                yield return renderNode;
            }
        }
    }
}
