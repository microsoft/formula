namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using API;
    using API.Nodes;
    using API.ASTQueries;
    using Compiler;
    using Extras;
    using Terms;
    
    internal class Bindable
    {
        public Term Binding 
        {
            get; 
            protected set; 
        }

        internal Bindable() 
        { 
        }

        internal Bindable(Term binding)
        {
            Binding = binding;
        }
    }
}
