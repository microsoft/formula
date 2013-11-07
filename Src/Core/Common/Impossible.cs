namespace Microsoft.Formula.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public class Impossible : Exception
    {
        internal Impossible()
            : base()
        { }

        internal Impossible(string message)
            : base(message)
        { }
    }
}
