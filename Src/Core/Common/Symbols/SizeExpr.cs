namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using Extras;

    internal enum SizeExprKind { Infinity, Prod, Sum, Count }

    internal class SizeExpr
    {
        private static readonly SizeExpr infinity = new SizeExpr();

        public static SizeExpr Infinity
        {
            get { return infinity; }
        }

        public SizeExprKind Kind
        {
            get;
            private set;
        }

        public object Raw
        {
            get;
            private set;
        }

        public SizeExpr(BigInteger nNewConstants, LinkedList<SizeExpr> exprs)
        {
            Contract.Requires(nNewConstants.Sign >= 0);
            Contract.Requires(exprs != null);
            Kind = SizeExprKind.Sum;
            Raw = new Tuple<BigInteger, LinkedList<SizeExpr>>(nNewConstants, exprs); 
        }

        public SizeExpr(string name)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Kind = SizeExprKind.Count;
            Raw = name;
        }

        public SizeExpr(SizeExpr[] args)
        {
            Contract.Requires(args != null && args.Length > 0);
            Kind = SizeExprKind.Prod;
            Raw = args;
        }

        private SizeExpr()
        {
            Kind = SizeExprKind.Infinity;
            Raw = null;
        }

        public AST<Node> ToAST(string auxVarName, Span span)
        {
            //// Note: Recursive implementation. Depth of sizeExpr not expected to be large.
            switch (Kind)
            {
                case SizeExprKind.Infinity:
                    //// Maybe called, but an error should have been reported.
                    return Factory.Instance.MkCnst(0, span);
                case SizeExprKind.Count:
                    {
                        var compr = Factory.Instance.MkComprehension(span);
                        compr = Factory.Instance.AddHead(compr, Factory.Instance.MkId(auxVarName, span));

                        var body = Factory.Instance.MkBody(span);
                        body = Factory.Instance.AddConjunct(
                            body,
                            Factory.Instance.MkFind(
                                Factory.Instance.MkId(auxVarName, span),
                                Factory.Instance.MkId((string)Raw, span)));

                        var count = Factory.Instance.MkFuncTerm(OpKind.Count, span);
                        return Factory.Instance.AddArg(count, Factory.Instance.AddBody(compr, body));
                    }
                case SizeExprKind.Sum:
                    {
                        var tup = (Tuple<BigInteger, LinkedList<SizeExpr>>)Raw;
                        var expr = (AST<Node>)Factory.Instance.MkCnst(new Rational(tup.Item1, BigInteger.One), span);
                        foreach (var e in tup.Item2)
                        {
                            expr = Factory.Instance.MkFuncTerm(
                                OpKind.Add, 
                                span, 
                                new AST<Node>[] { expr, e.ToAST(auxVarName, span) });
                        }

                        return expr;
                    }
                case SizeExprKind.Prod:
                    {
                        var args = (SizeExpr[])Raw;
                        var expr = (AST<Node>)Factory.Instance.MkCnst(Rational.One, span);
                        for (int i = 0; i < args.Length; ++i)
                        {
                            expr = Factory.Instance.MkFuncTerm(
                                OpKind.Mul,
                                span,
                                new AST<Node>[] { expr, args[i].ToAST(auxVarName, span) });
                        }

                        return expr;
                    }
                default:
                    throw new NotImplementedException();
            }
        }
       
        public SizeExpr Clone(string renaming = null)
        {
            //// Note: Recursive implementation. Depth of sizeExpr not expected to be large.
            if (string.IsNullOrEmpty(renaming))
            {
                return this;
            }

            switch (Kind)
            {
                case SizeExprKind.Infinity:
                    return this;
                case SizeExprKind.Count:
                    return new SizeExpr(string.Format("{0}.{1}", renaming, (string)Raw));
                case SizeExprKind.Sum:
                    {
                        var tup = (Tuple<BigInteger, LinkedList<SizeExpr>>)Raw;
                        var clonedSum = new LinkedList<SizeExpr>();
                        foreach (var e in tup.Item2)
                        {
                            clonedSum.AddLast(e.Clone(renaming));
                        }

                        return new SizeExpr(tup.Item1, clonedSum);
                    }
                case SizeExprKind.Prod:
                    {
                        var args = (SizeExpr[])Raw;
                        var clonedArgs = new SizeExpr[args.Length];
                        for (int i = 0; i < args.Length; ++i)
                        {
                            clonedArgs[i] = args[i].Clone(renaming);
                        }

                        return new SizeExpr(clonedArgs);
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public string Debug_ToString()
        {
            switch (Kind)
            {
                case SizeExprKind.Infinity:
                    return "<INFTY>";
                case SizeExprKind.Count:
                    return string.Format("|{0}|", (string)Raw);
                case SizeExprKind.Sum:
                    {
                        var tup = (Tuple<BigInteger, LinkedList<SizeExpr>>)Raw;
                        var str = string.Format("({0}", tup.Item1);
                        foreach (var e in tup.Item2)
                        {
                            str += string.Format(" + {0}", e.Debug_ToString());
                        }

                        return str + ")";
                    }
                case SizeExprKind.Prod:
                    {
                        var args = (SizeExpr[])Raw;
                        var str = args[0].Debug_ToString();
                        for (int i = 1; i < args.Length; ++i)
                        {
                            str += string.Format(" * {0}", args[i].Debug_ToString());
                        }

                        return str;
                    }
                default:
                    throw new NotImplementedException();                   
            }
        }
    }
}
