namespace Microsoft.Formula.Solver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using API;
    using API.Nodes;
    using API.Plugins;
    using Common;
    using Common.Extras;
    using Common.Terms;
    using Compiler;

    public class OATStrategy : ISearchStrategy
    {
        private static readonly OATStrategy theFactoryInstance = new OATStrategy();

        public const string LimitSettingName = "Limit";
        public const string SolsPerIncSettingName = "SolsPerInc";
        public const string DOFsSettingName = "DOFs";

        private static readonly char[] ListDelim = new char[] { ',' };

        private int limitSetting;
        private int solsPerIncSetting;
        private Set<UserSymbol> dofsSetting = new Set<UserSymbol>(Symbol.Compare);
        private ISolver solver;

        /// <summary>
        /// Return a shared instance for internally creating OAT strategies
        /// </summary>
        internal static OATStrategy TheFactoryInstance
        {
            get { return theFactoryInstance; }
        }

        public AST<Node> Module
        {
            get;
            private set;
        }

        public string CollectionName
        {
            get;
            private set;
        }

        public string InstanceName
        {
            get;
            private set;
        }

        public SymbolTable Table
        {
            get;
            private set;
        }

        public IEnumerable<Tuple<string, CnstKind>> SuggestedSettings
        {
            get
            {
                yield return new Tuple<string, CnstKind>(LimitSettingName, CnstKind.Numeric);
                yield return new Tuple<string, CnstKind>(SolsPerIncSettingName, CnstKind.Numeric);
                yield return new Tuple<string, CnstKind>(DOFsSettingName, CnstKind.String);
            }
        }

        public ISearchStrategy CreateInstance(
            AST<Node> module, 
            string collectionName, 
            string instanceName)
        {
            var inst = new OATStrategy();
            inst.Module = module;
            inst.CollectionName = collectionName;
            inst.InstanceName = instanceName;
            return inst;
        }

        public ISearchStrategy Begin(ISolver solver, out List<Flag> flags)
        {
            var inst = new OATStrategy();
            inst.solver = solver;
            inst.Module = Module;
            inst.CollectionName = CollectionName;
            inst.InstanceName = InstanceName;
            inst.Table = solver.SymbolTable;

            flags = new List<Flag>();
            Cnst cnstVal;
            string dofsStringSetting = null;
            if (!inst.TryGetNaturalSetting(LimitSettingName, solver.Configuration, flags, 8, ref inst.limitSetting, out cnstVal) |
                !inst.TryGetNaturalSetting(SolsPerIncSettingName, solver.Configuration, flags, 1, ref inst.solsPerIncSetting, out cnstVal) |
                !inst.TryGetStringSetting(DOFsSettingName, solver.Configuration, flags, null, ref dofsStringSetting, out cnstVal))
            {
                return null;
            }

            bool success = true;
            if (dofsStringSetting != null)
            {
                var types = dofsStringSetting.Split(ListDelim);
                foreach (var t in types)
                {
                    success = inst.AddDOFs(cnstVal, t, flags) && success;
                }
            }
            else
            {
                //// Every new-kind constructor can be a degree of freedom
                AddDOFs(solver.SymbolTable.Root);
            }

            return success ? inst : null;
        }

        public IEnumerable<KeyValuePair<UserSymbol, int>> GetNextCmd()
        {
            throw new NotImplementedException();
        }

        private void AddDOFs(Namespace ns)
        {
            foreach (var s in ns.Symbols)
            {
                if (s.Kind == SymbolKind.MapSymb || s.Kind == SymbolKind.ConSymb && ((ConSymb)s).IsNew)
                {
                    dofsSetting.Add(s);
                }
            }
        }

        private bool AddDOFs(Node n, string typename, List<Flag> flags)
        {
            UserSortSymb us;
            UserSymbol symb, other;
            symb = Table.Resolve(typename.Trim(), out other);
            if (symb == null)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.BadId.ToString(typename, "type"),
                    Constants.BadId.Code));
                return false;
            }
            else if (other != null)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    n,
                    Constants.AmbiguousSymbol.ToString("id", typename, symb.FullName, other.FullName),
                    Constants.AmbiguousSymbol.Code));
                return false;
            }

            if (symb.Kind == SymbolKind.MapSymb)
            {
                dofsSetting.Add(symb);
            }
            else if (symb.Kind != SymbolKind.UnnSymb)
            {
                if (symb.Kind == SymbolKind.ConSymb && ((ConSymb)symb).IsNew)
                {
                    dofsSetting.Add(symb);
                }
                else
                {
                    flags.Add(new Flag(
                        SeverityKind.Warning,
                        n,
                        Constants.PluginWarning.ToString(
                                CollectionName,
                                InstanceName,
                                string.Format("The type / value {0} is not a legal degree-of-freedom", symb.FullName)),
                        Constants.PluginWarning.Code));
                }
            }
            else
            {
                foreach (var s in symb.CanonicalForm[0].NonRangeMembers)
                {
                    if (s.Kind == SymbolKind.UserSortSymb)
                    {
                        us = (UserSortSymb)s;
                        if (us.DataSymbol.Kind == SymbolKind.MapSymb ||
                            us.DataSymbol.Kind == SymbolKind.ConSymb && ((ConSymb)us.DataSymbol).IsNew)
                        {
                            dofsSetting.Add(us.DataSymbol);
                        }
                        else
                        {
                            flags.Add(new Flag(
                                SeverityKind.Warning,
                                n,
                                Constants.PluginWarning.ToString(
                                        CollectionName,
                                        InstanceName,
                                        string.Format("The type / value {0} is not a legal degree-of-freedom", us.DataSymbol.FullName)),
                                Constants.PluginWarning.Code));
                        }
                    }
                    else
                    {
                        flags.Add(new Flag(
                            SeverityKind.Warning,
                            n,
                            Constants.PluginWarning.ToString(
                                    CollectionName,
                                    InstanceName,
                                    string.Format("The type / value {0} is not a legal degree-of-freedom", s.PrintableName)),
                            Constants.PluginWarning.Code));
                    }
                }

                if (!symb.CanonicalForm[0].RangeMembers.IsEmpty())
                {
                    string intName;
                    API.ASTQueries.ASTSchema.Instance.TryGetSortName(BaseSortKind.Integer, out intName);

                    flags.Add(new Flag(
                        SeverityKind.Warning,
                        n,
                        Constants.PluginWarning.ToString(
                                CollectionName,
                                InstanceName,
                                string.Format("The type / value {0} is not a legal degree-of-freedom", intName)),
                        Constants.PluginWarning.Code));
                }
            }

            return true;
        }

        private bool TryGetStringSetting(string setting, Configuration config, List<Flag> flags, string defValue, ref string value, out Cnst cnstVal)
        {
            cnstVal = null;            
            if (Module == null || CollectionName == null || InstanceName == null)
            {
                value = defValue;
                return true;
            }

            if (!config.TryGetSetting(CollectionName, InstanceName, setting, out cnstVal))
            {
                value = defValue;
                return true;
            }
            else if (cnstVal.CnstKind != CnstKind.String)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    cnstVal,
                    Constants.BadSetting.ToString(setting, cnstVal.Raw, "Expected a string value"),
                    Constants.BadSetting.Code));
                return false;
            }

            value = string.IsNullOrWhiteSpace(cnstVal.GetStringValue()) ? defValue : cnstVal.GetStringValue();
            return true;
        }

        private bool TryGetNaturalSetting(string setting, Configuration config, List<Flag> flags, int defValue, ref int value, out Cnst cnstVal)
        {
            cnstVal = null;
            if (Module == null || CollectionName == null || InstanceName == null)
            {
                value = defValue;
                return true;
            }

            if (!config.TryGetSetting(CollectionName, InstanceName, setting, out cnstVal))
            {
                value = defValue;
                return true;
            }
            else if (cnstVal.CnstKind != CnstKind.Numeric)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    cnstVal,
                    Constants.BadSetting.ToString(setting, cnstVal.Raw, "Expected a small non-negative integer"),
                    Constants.BadSetting.Code));
                return false;
            }

            var r = cnstVal.GetNumericValue();
            if (r.Sign < 0 || !r.IsInteger || r.Numerator >= int.MaxValue)
            {
                flags.Add(new Flag(
                    SeverityKind.Error,
                    cnstVal,
                    Constants.BadSetting.ToString(setting, cnstVal.Raw, "Expected a small non-negative integer"),
                    Constants.BadSetting.Code));
                return false;
            }

            defValue = (int)r.Numerator;
            return true;
        }
    }
}
