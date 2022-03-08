namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    public enum ContractKind 
    {
        ConformsProp,
        EnsuresProp, 
        RequiresProp, 
        RequiresSome, 
        RequiresAtLeast, 
        RequiresAtMost 
    };

    public enum ComposeKind { None, Includes, Extends };

    public enum RelKind { No, Eq, Neq, Le, Lt, Ge, Gt, Typ };

    public enum SeverityKind { Info = 0, Warning = 1, Error = 2 };

    public enum MapKind { Fun, Inj, Bij, Sur };

    public enum InstallKind { Compiled, Failed, Cached, Uninstalled };

    public enum CnstKind { Numeric, String };

    public enum NodeKind
    {
        Cnst,
        Id,
        Range,
        QuoteRun,
        CardPair,
        Quote,
        FuncTerm,
        Find,
        ModelFact,
        Compr,
        RelConstr,
        Body,
        Rule,
        ContractItem,
        Setting,
        Config,
        Field,
        Enum,
        Union,
        ConDecl,
        MapDecl,
        UnnDecl,
        Step,
        ModRef,
        ModApply,
        Param,
        Domain,
        Transform,
        TSystem,
        Model,
        Machine,
        Property,
        Update,
        Program,
        Folder,

        AnyNodeKind
    };

    public enum OpKind
    {
        Add,
        And,
        AndAll,
        Count,
        Div,
        GCD,
        GCDAll,
        Impl,
        IsSubstring,
        LCM,
        LCMAll,
        LstLength,
        LstReverse,
        Max,
        MaxAll,
        Min,
        MinAll,
        Mod,
        Mul,
        Neg,
        Not,
        Or,
        OrAll,
        Prod,
        Qtnt,
        RflIsMember,
        RflIsSubtype,
        RflGetArgType,
        RflGetArity,
        Sign,        
        StrAfter,
        StrBefore,
        StrFind,
        StrGetAt,
        StrJoin,
        StrReplace,
        StrLength,
        StrLower,
        StrReverse,
        StrUpper,
        SymAnd,
        SymCount,
        Sub,
        Sum,
        ToNatural,
        ToOrdinal,
        ToList,
        ToString,
        ToSymbol
    };

    /// <summary>
    /// These are reserved operation that are introduced by the compiler.
    /// The user cannot access these operations directly, though they may
    /// be introduced by the compiler and appear in compiler-generated data.
    /// </summary>
    public enum ReservedOpKind
    {
        Range,      //// Range(x, y) : A function constructing a type representing the set of all integers in the interval [x, y]
        TypeUnn,    //// TypeUnn(x, y): A function returning a type representing the the set of all values in the union of types x and y
        Relabel,    //// Relabel(p, p', x): A function which relabels the prefixes of constructor applications
        Select,     //// Select(x, y): A function returning the argument named y from the data term x.
        Find,       //// Find(t, p, tp): Represents a find operation binding t, pattern p, and type tp.
        Conj,       //// Conj(x, y): Represents the conjunction of two body constraints.
        ConjR,      //// ConjR(x, y): Represents the conjunction of two disjoint partial rules / projections.
        Disj,       //// Disj(x, y): Represents the disjunction of two partial rules / projections.
        Proj,       //// Proj(rule, vars): Represents the projection of a partial rule / projection.
        PRule,      //// PRule(f1, f2, body): Represents a partial rule with finds f1, f2. Body is a term of list of conjuncts
        CRule,      //// CRule(h, compr, rule): Represents a rule computing part of a comprehension from the body rule.
        Rule,       //// Rule(h, rule): Represents a complete rule as a term. rule is a partial rule / projection.
        Compr       //// Compr(heads, reads, disj): Represents a comprehension with heads, reads and a disjunction of rules.
    };

    public enum AttributeKind
    {
        Cardinality,
        CnstKind,
        Raw,
        Name,
        IsNew,
        IsSub,
        ContractKind,
        ComposeKind,
        IsAny,
        Op,
        MapKind,
        IsPartial,
        Rename,
        Location,
        Text,
        Lower,
        Upper
    };

    public enum ChildContextKind
    {
        Operator,
        Args,
        Initials,
        Nexts,
        Dom,
        Cod,
        Includes,
        Domain,
        Binding,
        Match,
        Inputs,
        Outputs,

        AnyChildContext
    };

    public enum NodePredicateKind { Atom, False, Star, Or };

    public enum BuilderResultKind { Success, Fail_Closed, Fail_BadArgs };

    public enum EnvParamKind
    {
        /// <summary>
        /// If true, then messages will not include path names, only file names (default: true).
        /// </summary>
        Msgs_SuppressPaths,

        /// <summary>
        /// Determines how to print references in an AST (default: ReferencePrintKind.Verbatim)
        /// </summary>
        Printer_ReferencePrintKind
    };

    public enum ReferencePrintKind
    {
        /// <summary>
        /// Print the location string exactly as it appeared in the AST.
        /// </summary>
        Verbatim,

        /// <summary>
        /// If the module reference has been resolved, and the AST is being printed to a file
        /// then print the module reference relative to the output file. Otherwise behave like Absolute.
        /// </summary>
        Relative,

        /// <summary>
        /// If the module reference has been resolved then print the full Uri of the reference. Otherwise behave like Verbatim.
        /// </summary>
        Absolute
    }

    public static class Constants
    {
        public static readonly MessageString OpCancelled = new MessageString("{0}. The operation was cancelled", 0);

        public static readonly MessageString OpFailed = new MessageString("The {0} operation failed", 1);
        
        public static readonly MessageString BadFile = new MessageString("File access error - {0}", 2);

        public static readonly MessageString BadSyntax = new MessageString("Syntax error - {0}", 3);
                
        public static readonly MessageString BadId = new MessageString("{0} is not a legal {1} identifier", 4);

        public static readonly MessageString BadSetting = new MessageString("Cannot set {0} to \"{1}\" - {2}", 5);

        public static readonly MessageString BadNumeric = new MessageString("{0} is not a legal numerical constant", 6);

        public static readonly MessageString BadDepCycle = new MessageString("Cyclic dependency error - {0} cycle {1}", 7);

        public static readonly MessageString DuplicateDefs = new MessageString("The {0} has multiple definitions. See {1} and {2}", 8);

        public static readonly MessageString UndefinedSymbol = new MessageString("The {0} {1} is undefined.", 9);

        public static readonly MessageString PluginException = new MessageString("Plugin {0}.{1} threw an exception. {2}", 10);

        public static readonly MessageString QuotationError = new MessageString("Could not parse or render. {0}", 11);

        public static readonly MessageString AlreadyInstalledError = new MessageString("A program called {0} is already installed in this environment. You must uninstall it first.", 12);

        public static readonly MessageString AmbiguousSymbol = new MessageString("The {0} {1} is ambiguous. See {2} and {3}", 13);

        public static readonly MessageString BadTypeDecl = new MessageString("The type {0} is badly defined; {1}", 13);

        public static readonly MessageString BadComposition = new MessageString("Cannot compose {0}; {1}", 14);

        public static readonly MessageString BadTransform = new MessageString("The transform {0} is badly defined; {1}", 15);

        public static readonly MessageString BadTransModelArgType = new MessageString("Argument {0} of {1} is badly typed; got {2} but expected {3}.", 16);

        public static readonly MessageString BadTransOutputType = new MessageString("Output {0} is badly typed; got {1} but expected {2}.", 16);

        public static readonly MessageString BadTransValueArgType = new MessageString("Argument {0} of {1} is badly typed.", 16);   

        public static readonly MessageString BadArgType = new MessageString("Argument {0} of function {1} is badly typed.", 16);

        public static readonly MessageString BadArgTypes = new MessageString("The arguments of function {0} are badly typed.", 16);

        public static readonly MessageString BadConstraint = new MessageString("This constraint is unsatisfiable.", 17);

        public static readonly MessageString UnsafeArgType = new MessageString("Argument {0} of function {1} is unsafe. Some values of type {2} are not allowed here.", 18);

        public static readonly MessageString UncoercibleArgType = new MessageString("Argument {0} of function {1} is badly typed. Cannot coerce values of type {2}.", 19);

        public static readonly MessageString AmbiguousCoercibleArg = new MessageString("Argument {0} of function {1} has an ambiguous coercion; possibly \"{2}\" -> \"{3}\".", 20);

        public static readonly MessageString FindHidesError = new MessageString("Variable {0} in parent scope is hidden by find.", 21);

        public static readonly MessageString UnorientedError = new MessageString("Variable {0} cannot be oriented.", 22);

        public static readonly MessageString TransUnorientedError = new MessageString("Model variable {0} cannot be oriented.", 22);

        public static readonly MessageString ModelGroundingError = new MessageString("Symbolic constant {0} does not stand for a ground term.", 23);

        public static readonly MessageString LabelClashError = new MessageString("The label name {0} clashes with a module name of the same name.", 24);

        public static readonly MessageString StratificationError = new MessageString("A set comprehension depends on itself. {0}", 25);

        public static readonly MessageString ArgNewnessError = new MessageString("The new-kind constructor {0} cannot accept derived values of type {1} in argument {2}.", 26);

        public static readonly MessageString SubArgNewnessError = new MessageString("The derived-kind sub constructor {0} cannot accept derived values of type {1} in argument {2}.", 26);

        public static readonly MessageString RelationalError = new MessageString("The constructor {0} cannot have relational constraints on itself; see argument {1}.", 27);

        public static readonly MessageString TotalityError = new MessageString("The function {0} requires totality on an argument supported by an infinite number of values; see argument {1}.", 28);

        public static readonly MessageString TransNewnessError = new MessageString("Transforms cannot define new-kind constructors.", 29);

        public static readonly MessageString DuplicateFindError = new MessageString("The find variable {0} is defined twice.", 30);

        public static readonly MessageString ModelNewnessError = new MessageString("Models cannot contain derived-kind constants / constructors.", 31);

        public static readonly MessageString EnumerationError = new MessageString("An enumeration cannot contain a symbolic constant", 32);

        public static readonly MessageString ModelCyclicDefError = new MessageString("Symbolic constant {0} is defined using itself.", 33);

        public static readonly MessageString ModelCmpError = new MessageString("Cannot include model {0}. It is not a model of a compatible domain.", 34);

        public static readonly MessageString DerivesError = new MessageString("Rule derives {0}; {1}.", 35);

        public static readonly MessageString ObjectGraphException = new MessageString("An exception occurred while creating an object graph. {0}", 36);

        public static readonly MessageString CardContractWarning = new MessageString("Cardinality contract ignores the constant / type {0}", 37);

        public static readonly MessageString CardNewnessError = new MessageString("Cardinality requirements cannot be placed on the derived-kind constant / constructor {0}.", 38);

        public static readonly MessageString PluginWarning = new MessageString("Plugin {0}.{1} reported a warning. {2}", 39);

        public static readonly MessageString NotImplemented = new MessageString("Feature not implemented: {0}", 40);

        public static readonly MessageString SubRuleUnsat = new MessageString("The sub constructor {0} is unsatisfiable.", 41);

        public static readonly MessageString SubRuleUntrig = new MessageString("The sub constructor {0} will never be triggered in this context.", 42);

        public static readonly MessageString UninstallError = new MessageString("The file {0} could not be uninstalled because it was not installed.", 43);

        public static readonly MessageString DataCnstLikeVarWarning = new MessageString("The variable {0} is named as if it were a data constant. All-caps should be reserved for data constants.", 44);

        public static readonly MessageString DataCnstLikeSymbWarning = new MessageString("The symbolic constant {0} is named as if it were a data constant. All-caps should be reserved for data constants.", 44);

        public static readonly MessageString ProductivityError = new MessageString("Program never produces values of the form {0}", 45);

        public static readonly MessageString ProductivityPartialError = new MessageString("Program produces only some values of the form {0}. Listing {1} cases...", 46);

        public static readonly MessageString ProductivityCaseWarning = new MessageString("Case {0}: {1}[{2} : {3}]", 47);

        public static readonly MessageString ProductivityWarning = new MessageString("Rule may construct any value accepted by constructor {0} at indices {1}", 48);

        public struct MessageString
        {
            private string msg;
            private int code;

            public string Message
            {
                get { return msg; }
            }

            public int Code
            {
                get { return code; }
            }

            public MessageString(string msg, int code)
            {
                this.msg = msg;
                this.code = code;
            }

            public string ToString(params object[] prms)
            {
                return Message == null ? string.Empty : string.Format(Message, prms);        
            }
        }
    }
}
