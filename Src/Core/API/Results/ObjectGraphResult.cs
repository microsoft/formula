namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using Nodes;
    using Generators;

    public sealed class ObjectGraphResult
    {
        private List<Flag> flags = new List<Flag>();
        private List<ICSharpTerm> objects = new List<ICSharpTerm>();
        private Dictionary<string, ICSharpTerm> aliases = new Dictionary<string, ICSharpTerm>();

        public bool Succeeded
        {
            get;
            private set;
        }

        public List<ICSharpTerm> Objects
        {
            get { return objects; }
        }

        public Dictionary<string, ICSharpTerm> Aliases
        {
            get { return aliases; }
        }

        public IEnumerable<Flag> Flags
        {
            get { return flags; }
        }

        internal ObjectGraphResult()
        {
            Succeeded = true;
        }

        internal void AddFlag(Flag flag)
        {
            flags.Add(flag);
            Succeeded = Succeeded && flag.Severity != SeverityKind.Error;
        }

        internal void AddFlags(IEnumerable<Flag> flags)
        {
            if (flags == null)
            {
                return;
            }

            foreach (var f in flags)
            {
                AddFlag(f);
            }
        }

        internal void Failed()
        {
            Succeeded = false;
        }
    }
}
