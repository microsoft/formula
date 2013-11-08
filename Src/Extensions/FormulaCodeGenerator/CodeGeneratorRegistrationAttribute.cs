namespace Microsoft.VisualStudio.Shell
{
    using System;
    using System.Diagnostics.Contracts;
    using System.Globalization;
    //// using Microsoft.Internal.CodeGenerator.Host;

    /// <summary>
    /// <para>
    /// This attribute adds a custom file generator registry entry for specific file 
    /// type.</para>
    /// <para>For Example:<code>
    ///   [HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\VisualStudio\9.0\Generators\
    ///		{fae04ec1-301f-11d3-bf4b-00c04f79efbc}\MyGenerator]
    ///			"CLSID"="{AAAA53CC-3D4F-40a2-BD4D-4F3419755476}"
    ///         "GeneratesDesignTimeSource" = d'1'</code>
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CodeGeneratorRegistrationAttribute : RegistrationAttribute
    {
        /// <summary>Inherited from VS SDK</summary>
        private string contextGuid;

        /// <summary>Inherited from VS SDK</summary>
        private bool generatesDesignTimeSource = true;

        /// <summary>Inherited from VS SDK</summary>
        private bool generatesSharedDesignTimeSource = false;

        /// <summary>Inherited from VS SDK</summary>
        private Guid generatorGuid;

        /// <summary>Inherited from VS SDK</summary>
        private string generatorName;

        /// <summary>Inherited from VS SDK</summary>
        private string generatorRegKeyName;

        /// <summary>Inherited from VS SDK</summary>
        private Type generatorType;

        /// <summary>
        /// Initializes a new instance of the <see cref="CodeGeneratorRegistrationAttribute"/> class to register a custom
        /// code generator for the provided context. 
        /// </summary>
        /// <param name="generatorType">The type of Code generator. TokenType that implements IVsSingleFileGenerator</param>
        /// <param name="generatorName">The generator name</param>
        /// <param name="contextGuid">The context GUID this code generator would appear under.</param>
        public CodeGeneratorRegistrationAttribute(Type generatorType, string generatorName, string contextGuid)
        {
            Contract.Assert(generatorType != null);
            Contract.Assert(contextGuid != null);
            Contract.Assert(!string.IsNullOrWhiteSpace(generatorName));

            this.contextGuid = contextGuid;
            this.generatorType = generatorType;
            this.generatorName = generatorName;
            this.generatorRegKeyName = generatorName;
            this.generatorGuid = generatorType.GUID;
        }

        /// <summary>
        /// Gets the Guid representing the project type
        /// </summary>
        public string ContextGuid
        {
            get { return this.contextGuid; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the generator generates design time source.
        /// </summary>
        public bool GeneratesDesignTimeSource
        {
            get { return this.generatesDesignTimeSource; }
            set { this.generatesDesignTimeSource = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the generator generates shared design time source.
        /// </summary>
        public bool GeneratesSharedDesignTimeSource
        {
            get { return this.generatesSharedDesignTimeSource; }
            set { this.generatesSharedDesignTimeSource = value; }
        }

        /// <summary>
        /// Gets the Guid representing the generator type
        /// </summary>
        public Guid GeneratorGuid
        {
            get { return this.generatorGuid; }
        }

        /// <summary>
        /// Gets the Generator name 
        /// </summary>
        public string GeneratorName
        {
            get { return this.generatorName; }
        }

        /// <summary>
        /// Gets or sets the Generator registry key name under 
        /// </summary>
        public string GeneratorRegKeyName
        {
            get { return this.generatorRegKeyName; }
            set { this.generatorRegKeyName = value; }
        }

        /// <summary>
        /// Gets the generator TokenType
        /// </summary>
        public Type GeneratorType
        {
            get { return this.generatorType; }
        }

        /// <summary>
        /// Gets the generator base key name
        /// </summary>
        private string GeneratorRegKey
        {
            get { return string.Format(CultureInfo.InvariantCulture, @"Generators\{0}\{1}", this.ContextGuid, this.GeneratorRegKeyName); }
        }


        /// <summary>
        /// Called to register this attribute with the given context.  The context
        /// contains the location where the registration information should be placed.
        /// It also contains such as the type being registered, and path information.
        /// This method is called both for registration and unregistration.  The difference is
        /// that unregistering just uses a hive that reverses the changes applied to it.
        /// </summary>
        /// <param name="context">The registration context.</param>
        public override void Register(RegistrationContext context)
        {
            using (Key childKey = context.CreateKey(this.GeneratorRegKey))
            {
                childKey.SetValue(string.Empty, this.GeneratorName);
                childKey.SetValue("CLSID", this.GeneratorGuid.ToString("B"));

                if (this.GeneratesDesignTimeSource)
                {
                    childKey.SetValue("GeneratesDesignTimeSource", 1);
                }

                if (this.GeneratesSharedDesignTimeSource)
                {
                    childKey.SetValue("GeneratesSharedDesignTimeSource", 1);
                }
            }
        }

        /// <summary>
        /// Unregister this file extension.
        /// </summary>
        /// <param name="context">The registration context.</param>
        public override void Unregister(RegistrationContext context)
        {
            context.RemoveKey(this.GeneratorRegKey);
        }
    }
}
