namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using Nodes;

    internal interface IInternalClonable
    {
        AST<Node> DeepClone(bool keepCompilerData, CancellationToken cancel = default(CancellationToken));
    }
}
