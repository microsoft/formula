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

    /// <summary>
    /// Extensions methods for nodes that have been compiled and tagged with metadata
    /// </summary>
    internal static class CompiledNodeMethods
    {
        internal static Configuration GetModuleConfiguration(this Node node)
        {
            Contract.Requires(node.IsModule);

            switch (node.NodeKind)
            {
                case NodeKind.Domain:
                    return (Configuration)((Domain)node).Config.CompilerData;
                case NodeKind.Model:
                    return (Configuration)((Model)node).Config.CompilerData;
                case NodeKind.Transform:
                    return (Configuration)((Transform)node).Config.CompilerData;
                case NodeKind.TSystem:
                    return (Configuration)((TSystem)node).Config.CompilerData;
                case NodeKind.Machine:
                    return (Configuration)((Machine)node).Config.CompilerData;
                default:
                    throw new NotImplementedException();
            }
        }

        internal static bool TryGetConfiguration(this Node node, out Configuration conf)
        {
            Config confNode;
            conf = null;
            switch (node.NodeKind)
            {
                case NodeKind.ModelFact:
                    confNode = ((ModelFact)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Rule:
                    confNode = ((Rule)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.ConDecl:
                    confNode = ((ConDecl)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.MapDecl:
                    confNode = ((MapDecl)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.UnnDecl:
                    confNode = ((UnnDecl)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Update:
                    confNode = ((Update)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Step:
                    confNode = ((Step)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Domain:
                    confNode = ((Domain)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Model:
                    confNode = ((Model)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Transform:
                    confNode = ((Transform)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.TSystem:
                    confNode = ((TSystem)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Machine:
                    confNode = ((Machine)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                case NodeKind.Program:
                    confNode = ((Program)node).Config;
                    if (confNode != null)
                    {
                        conf = (Configuration)confNode.CompilerData;
                    }

                    break;
                default:
                    conf = null;
                    break;
            }

            return conf != null;
        }
    }
}
