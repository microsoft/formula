namespace Microsoft.Formula.API.Generators
{
    using System;
    using System.Diagnostics.Contracts;

    public sealed class GeneratorOptions
    {
        public enum Language { CSharp };

        public Language OutputLanguage
        {
            get;
            private set;
        }

        public bool IsThreadSafeCode
        {
            get;
            private set;
        }

        public bool IsNewTypesOnly
        {
            get;
            private set;
        }

        public string Namespace
        {
            get;
            private set;
        }

        public string Classname
        {
            get;
            private set;
        }

        public GeneratorOptions(Language outputLanguage, 
                                bool genThreadSafeCode, 
                                bool genNewTypesOnly,
                                string className,
                                string useNamespace)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(className));

            OutputLanguage = outputLanguage;
            IsThreadSafeCode = genThreadSafeCode;
            IsNewTypesOnly = genNewTypesOnly;
            Classname = className;
            Namespace = string.IsNullOrWhiteSpace(useNamespace) ? null : useNamespace.Trim();
        }
    }
}
