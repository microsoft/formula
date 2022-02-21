namespace Microsoft.Jupyter.Core
{
    internal static class Constants
    {
        internal static KernelProperties PROPERTIES = new KernelProperties
        {
            FriendlyName = "Formula",

            KernelName = "Formula",

            KernelVersion = "1.0.0",

            DisplayName = "Formula",

            LanguageName = "Formula",

            LanguageVersion = "0.1",

            LanguageMimeType = MimeTypes.PlainText,

            LanguageFileExtension = ".4mlnb",

            Description = "A simple kernel that echos its input."
        };
    }
}
