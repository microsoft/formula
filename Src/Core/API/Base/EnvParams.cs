namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using ParameterData = System.Tuple<System.Type, string, object, System.Action<EnvParams, EnvParamKind, object>>;

    public sealed class EnvParams
    {
        /// <summary>
        /// Maps a parameter to its type, description, default value, and setter.
        /// </summary>
        private static readonly Dictionary<EnvParamKind, ParameterData> parameters = 
            new Dictionary<EnvParamKind, ParameterData>();

        static EnvParams()
        {
            parameters.Add(
                EnvParamKind.Msgs_SuppressPaths,
                new ParameterData(
                    typeof(bool),
                    "If true, then messages will not include path names, only file names (default: true).",
                    true,
                    SetBoolParameter));

            parameters.Add(
                EnvParamKind.Printer_ReferencePrintKind,
                new ParameterData(
                    typeof(ReferencePrintKind),
                    "Determines how to print references in an AST (default: ReferencePrintKind.Verbatim).",
                    ReferencePrintKind.Verbatim,
                    SetReferencePrintKindParameter));
        }

        /// <summary>
        /// Local settings 
        /// </summary>
        private Dictionary<EnvParamKind, object> settings;

        /// <summary>
        /// Functions can register a base uri for the construction of relative uris.
        /// </summary>
        internal Uri BaseUri
        {
            get;
            private set;
        }

        public EnvParams(params Tuple<EnvParamKind, object>[] parameters)
        {
            settings = new Dictionary<EnvParamKind, object>();
            foreach (var kv in parameters)
            {
                EnvParams.parameters[kv.Item1].Item4(this, kv.Item1, kv.Item2);
            }
        }

        internal EnvParams(EnvParams source, Uri baseUri)
        {
            BaseUri = baseUri;
            settings = source == null ? new Dictionary<EnvParamKind, object>() : source.settings;
        }

        private EnvParams()
        {
            settings = new Dictionary<EnvParamKind, object>();
        }

        [Pure]
        public static Type GetParameterType(EnvParamKind prm)
        {
            return parameters[prm].Item1;
        }

        public static string GetParameterDescr(EnvParamKind prm)
        {
            return parameters[prm].Item2;
        }

        public static ReferencePrintKind GetReferencePrintKindParameter(EnvParams prms, EnvParamKind prm)
        {
            Contract.Requires(GetParameterType(prm).Equals(typeof(ReferencePrintKind)));
            object val;
            if (prms != null && prms.settings.TryGetValue(prm, out val))
            {
                return (ReferencePrintKind)val;
            }

            return (ReferencePrintKind)parameters[prm].Item3;
        }

        public static bool GetBoolParameter(EnvParams prms, EnvParamKind prm)
        {
            Contract.Requires(GetParameterType(prm).Equals(typeof(bool)));
            object val;
            if (prms != null && prms.settings.TryGetValue(prm, out val))
            {
                return (bool)val;
            }

            return (bool)parameters[prm].Item3;
        }

        /// <summary>
        /// Returns a new set of parameters with same parameters as prms, but with kind = value.
        /// If prms is null, then all parameters start with their default values.
        /// </summary>
        public static EnvParams SetParameter(EnvParams prms, EnvParamKind kind, object value)
        {
            var clone = new EnvParams();
            if (prms != null)
            {
                foreach (var kv in prms.settings)
                {
                    clone.settings[kv.Key] = kv.Value;
                }
            }

            EnvParams.parameters[kind].Item4(clone, kind, value);
            return clone;
        }

        private static void SetBoolParameter(EnvParams prms, EnvParamKind prm, object value)
        {
            if (!(value is bool))
            {
                throw new BadEnvParamException(
                    string.Format(
                            "Bad environment parameter; {0} must have type bool",
                            prm));
            }

            prms.settings[prm] = value;
        }

        private static void SetReferencePrintKindParameter(EnvParams prms, EnvParamKind prm, object value)
        {
            if (!(value is ReferencePrintKind))
            {
                throw new BadEnvParamException(
                    string.Format(
                            "Bad environment parameter; {0} must have type {1}",
                            prm,
                            typeof(ReferencePrintKind).Name));
            }

            prms.settings[prm] = value;
        }

        public class BadEnvParamException : Exception
        {
            public BadEnvParamException(string message)
                : base(message)
            { }
        }
    }
}
