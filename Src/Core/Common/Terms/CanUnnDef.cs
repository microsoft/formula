namespace Microsoft.Formula.Common.Terms
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;

    internal class CanUnnDef
    {
        private SymbolTable table;
        private AST<UnnDecl> unnDecl;

        private Set<Symbol> elements = new Set<Symbol>(Symbol.Compare);
        private Map<Rational, Symbol> rngStarts = new Map<Rational, Symbol>(Rational.Compare);
        private Map<Rational, Symbol> rngEnds = new Map<Rational, Symbol>(Rational.Compare);

        internal CanUnnDef(SymbolTable table, AST<UnnDecl> unnDecl)
        {
            this.table = table;
            this.unnDecl = unnDecl;
        }

        internal bool BuildDefinition(List<Flag> flags, CancellationToken cancel)
        {
            var compQuery = new NodePred[]
            {
                NodePredFactory.Instance.Star,
                NodePredFactory.Instance.MkPredicate(NodeKind.Enum) |
                NodePredFactory.Instance.MkPredicate(NodeKind.Id)
            };

            bool result = true;
            Factory.Instance.ToAST(unnDecl.Node.Body).FindAll(
                compQuery,
                (path, node) =>
                {
                    if (node.NodeKind == NodeKind.Id &&
                        ((LinkedList<ChildInfo>)path).Last.Previous.Value.Node.NodeKind != NodeKind.Enum)
                    {
                        result = AddTypeName((Id)node, flags) & result;
                    }
                    else if (node.NodeKind == NodeKind.Enum)
                    {
                        result = AddEnum((API.Nodes.Enum)node, flags) & result;
                    }
                },
                cancel);

            NormalizeRanges();

            Console.Write("{0}: ", unnDecl.Node.Name);
            using (var itStart = rngStarts.Keys.GetEnumerator())
            {
                using (var itEnd = rngEnds.Keys.GetEnumerator())
                {
                    while (itStart.MoveNext() && itEnd.MoveNext())
                    {
                        Console.Write("{0}..{1}, ", itStart.Current, itEnd.Current);
                    }
                }
            }
            Console.WriteLine();

            return result;
        }

        private void NormalizeRanges()
        {
            var merges = new List<Tuple<Rational, Rational>>();
            Rational? start = null, end = null;
            Rational pstart = Rational.Zero, pend = Rational.Zero;
            using (var itStart = rngStarts.Keys.GetEnumerator())
            {
                using (var itEnd = rngEnds.Keys.GetEnumerator())
                {
                    while (itStart.MoveNext() && itEnd.MoveNext())
                    {
                        if (start == null)
                        {
                            start = itStart.Current;
                            end = itEnd.Current;
                        }
                        else if (Rational.One + (Rational)end == itStart.Current)
                        {
                            end = itEnd.Current;
                        }
                        else  
                        {
                            if (pstart != (Rational)start || pend != (Rational)end)
                            {
                                merges.Add(new Tuple<Rational, Rational>((Rational)start, (Rational)end));
                            }

                            start = itStart.Current;
                            end = itEnd.Current;
                        }

                        pstart = itStart.Current;
                        pend = itEnd.Current;
                    }
                }
            }

            if (start != null && (pstart != (Rational)start || pend != (Rational)end))
            {
                AddRange((Rational)start, (Rational)end);
            }

            foreach (var rng in merges)
            {
                AddRange(rng.Item1, rng.Item2);
            }
        }

        private bool AddTypeName(Id typeId, List<Flag> flags)
        {
            var symbol = Resolve(typeId, flags, true);
            if (symbol == null)
            {
                return false;
            }

            elements.Add(symbol);
            return true;
        }

        private bool AddEnum(API.Nodes.Enum enm, List<Flag> flags)
        {
            bool result = true;
            foreach (var e in enm.Elements)
            {
                switch (e.NodeKind)
                {
                    case NodeKind.Cnst:
                        var cnst = (Cnst)e;
                        switch (cnst.CnstKind)
                        {
                            case CnstKind.Numeric:
                                AddRange((Rational)cnst.Raw, (Rational)cnst.Raw);
                                break;
                            case CnstKind.String:
                                elements.Add(table.GetCnstSymbol((string)cnst.Raw));
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        break;
                    case NodeKind.Range:
                        var rng = (Range)e;
                        AddRange(rng.Lower, rng.Upper);
                        break;
                    case NodeKind.Id:
                        var symbol = Resolve((Id)e, flags, false);
                        if (symbol == null)
                        {
                            result = false;
                        }
                        else
                        {
                            elements.Add(symbol);
                        }

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return result;
        }

        private void AddRange(Rational lower, Rational upper)
        {
            Rational start, end;
            Tuple<Rational, Rational> lIntr = null;
            Tuple<Rational, Rational> uIntr = null;
            Set<Rational> contained = new Set<Rational>(Rational.Compare); 

            using (var itStart = rngStarts.Keys.GetEnumerator())
            {
                using (var itEnd = rngEnds.Keys.GetEnumerator())
                {
                    while (itStart.MoveNext() && itEnd.MoveNext())
                    {
                        start = itStart.Current;
                        end = itEnd.Current;

                        if (lIntr == null && start <= lower && lower <= end)
                        {
                            lIntr = new Tuple<Rational, Rational>(start, end);
                        }

                        if (uIntr == null && start <= upper && upper <= end)
                        {
                            uIntr = new Tuple<Rational, Rational>(start, end);
                            if (lIntr.Item1 == uIntr.Item1 && lIntr.Item2 == uIntr.Item2)
                            {
                                return;
                            }
                        }

                        if (lower <= start && start <= upper)
                        {
                            contained.Add(start);
                        }

                        if (lower <= end && end <= upper)
                        {
                            contained.Add(end);
                        }
                    }
                }
            }

            foreach (var r in contained)
            {
                rngStarts.Remove(r);
                rngEnds.Remove(r);
            }

            if (lIntr == null)
            {
                rngStarts.Add(lower, table.GetCnstSymbol(lower));
            }
            else 
            {
                rngStarts[lIntr.Item1] = table.GetCnstSymbol(lIntr.Item1);
            }

            if (uIntr == null)
            {
                rngEnds.Add(upper, table.GetCnstSymbol(upper));
            }
            else
            {
                rngEnds[uIntr.Item2] = table.GetCnstSymbol(uIntr.Item2);
            }
        }

        private UserSymbol Resolve(Id id, List<Flag> flags, bool isTypeId)
        {
            UserSymbol other;
            var symbol = table.Resolve(id.Name, out other);
            if (symbol == null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    id,
                    Constants.UndefinedSymbol.ToString(isTypeId ? "type id" : "constant", id.Name),
                    Constants.UndefinedSymbol.Code);
                flags.Add(flag);
            }
            else if (other != null)
            {
                var flag = new Flag(
                    SeverityKind.Error,
                    id,
                    Constants.AmbiguousSymbol.ToString(
                        isTypeId ? "type id" : "constant",
                        id.Name,
                        string.Format("({0}, {1}): {2}",
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                symbol.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                symbol.Name),
                        string.Format("({0}, {1}): {2}",
                                other.Definitions.First<AST<Node>>().Node.Span.StartLine,
                                other.Definitions.First<AST<Node>>().Node.Span.StartCol,
                                other.Name)),
                    Constants.AmbiguousSymbol.Code);
                flags.Add(flag);
            }
            else if (isTypeId && 
                     symbol.Kind != SymbolKind.ConSymb &&
                     symbol.Kind != SymbolKind.MapSymb &&
                     symbol.Kind != SymbolKind.SortSymb &&
                     symbol.Kind != SymbolKind.UnnSymb)
            {
                var flag = new Flag(
                            SeverityKind.Error,
                            id,
                            Constants.BadId.ToString(symbol.Name, "type id"),
                            Constants.BadId.Code);
                flags.Add(flag);
            }
            else if (!isTypeId && (symbol.Kind != SymbolKind.UserCnstSymb || 
                                   ((UserCnstSymb)symbol).UserCnstKind != UserCnstSymbKind.New))
            {
                var flag = new Flag(
                            SeverityKind.Error,
                            id,
                            Constants.BadId.ToString(symbol.Name, "constant"),
                            Constants.BadId.Code);
                flags.Add(flag);
            }
            else
            {
                return symbol;
            }

            return null;
        }
    }
}
