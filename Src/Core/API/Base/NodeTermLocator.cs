namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Common;
    using Common.Terms;
    using Compiler;
    using Nodes;

    /// <summary>
    /// Locates a ground term with a node. If the node matches the constructor and arity of the term
    /// then the nodes children locate the immediate subterms. Otherwise, the subterms are located
    /// by the same node.
    /// </summary>
    internal class NodeTermLocator : Locator
    {
        private Node locatorNode;
        private Term locatorTerm;
        private ProgramName locatorProgram;

        /// <summary>
        /// Spin lock for args
        /// </summary>
        private SpinLock argsLock = new SpinLock();

        /// <summary>
        /// Becomes non-null after a child of this locator has been accessed.
        /// </summary>
        private NodeTermLocator[] args;

        public override int Arity
        {
            get { return locatorTerm.Symbol.Arity; }
        }

        public override ProgramName Program
        {
            get { return locatorProgram; }
        }

        public override Span Span
        {
            get { return locatorNode.Span; }
        }

        /// <summary>
        /// Gets the locators of this locators subterms
        /// </summary>
        public override Locator this[int index]
        {
            get
            {
                Contract.Assert(index >= 0 && index < Arity);
                bool gotLock = false;
                try
                {
                    argsLock.Enter(ref gotLock);
                    if (args != null)
                    {
                        return args[index];
                    }

                    args = new NodeTermLocator[locatorTerm.Symbol.Arity];
                    FuncTerm ftnode;
                    if (locatorNode.NodeKind == NodeKind.FuncTerm && 
                        (ftnode = (FuncTerm)locatorNode).Args.Count == locatorTerm.Symbol.Arity)
                    {
                        int i = 0;
                        foreach (var a in ftnode.Args)
                        {
                            args[i] = new NodeTermLocator(a, Program, locatorTerm.Args[i]);
                            ++i;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < locatorTerm.Args.Length; ++i)
                        {
                            args[i] = new NodeTermLocator(locatorNode, Program, locatorTerm.Args[i]);
                        }
                    }

                    return args[index];
                }
                finally
                {
                    if (gotLock)
                    {
                        argsLock.Exit();
                    }
                }
            }
        }

        public NodeTermLocator(Node node, ProgramName program, Term t)
        {
            Contract.Requires(node != null && program != null && t != null);
            Contract.Requires(t.Groundness == Groundness.Ground);

            locatorTerm = t;
            locatorProgram = program;
            locatorNode = ChooseRepresentativeNode(node, t);
        }

        /// <summary>
        /// Chooses a child of node to represent t.
        /// </summary>
        public static Node ChooseRepresentativeNode(Node node, Term t)
        {
            switch (node.NodeKind)
            {
                case NodeKind.Rule:
                    {
                        //// If this is a rule node, then pick a head that is similar to t.
                        //// Code below uses a very simple definition of similarity, but this could be
                        //// improved;
                        var rule = (Rule)node;
                        if (t.Args.Length == 0)
                        {
                            foreach (var h in rule.Heads)
                            {
                                if (h.NodeKind == NodeKind.Id)
                                {
                                    return h;
                                }
                            }
                        }
                        else
                        {
                            Id con;
                            FuncTerm ftnode;
                            var dataSymb = (UserSymbol)t.Symbol;
                            foreach (var h in rule.Heads)
                            {
                                if (h.NodeKind != NodeKind.FuncTerm)
                                {
                                    continue;
                                }

                                ftnode = (FuncTerm)h;
                                if ((con = ftnode.Function as Id) == null || ftnode.Args.Count != dataSymb.Arity)
                                {
                                    continue;
                                }

                                if (con.Name == dataSymb.Name || con.Name.EndsWith("." + dataSymb.Name))
                                {
                                    return h;
                                }
                            }
                        }

                        return rule.Heads.First();
                    }
                default:
                    return node;

            }
        }
    }
}
