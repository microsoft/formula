namespace Microsoft.Formula.API.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Nodes;
    using Compiler;

    public interface IQuoteParser
    {
        /// <summary>
        /// A description of this parser.
        /// </summary>
        string Description
        {
            get;
        }

        /// <summary>
        /// The string that should be used to prefix the Id of an unquote.
        /// </summary>
        string UnquotePrefix
        {
            get;
        }

        /// <summary>
        /// Gets the suggested data model for quotes.
        /// </summary>
        AST<Domain> SuggestedDataModel
        {
            get;
        }

        /// <summary>
        /// Some settings for this plugin.
        /// </summary>
        IEnumerable<Tuple<string, CnstKind, string>> SuggestedSettings
        {
            get;
        }

        /// <summary>
        /// Creates an instance of this parser. Specifies the module where the instance
        /// is attached, and the collection and instance names used to register this parser.
        /// For example: [ collectionName.instanceName = "parser at parser.dll" ]
        /// </summary>
        IQuoteParser CreateInstance(
                            AST<Node> module,
                            string collectionName,
                            string instanceName);

        /// <summary>
        /// Attempts to build an AST from a quote stream. The quote stream passes
        /// quotes while replacing unquotes with Id strings. The quote stream can 
        /// be "cancelled," which causes the stream to suddenly end. 
        /// However, implementations may provide additional cancelation logic.
        /// </summary>
        bool Parse(
                Configuration config,
                Stream quoteStream, 
                SourcePositioner positioner,
                out AST<Node> results, 
                out List<Flag> flags);

        /// <summary>
        /// Attempts to write an AST into a text stream. It is only required to convert quote-free ASTs where all children
        /// satisfy IsFuncOrAtom. Render should produce error flags if the AST contains other nodes. 
        /// If parts of the AST cannot be converted, then notifier should be called with a Flag of severity Error. 
        /// </summary>
        bool Render(
                Configuration config,
                TextWriter writer,
                AST<Node> ast, 
                out List<Flag> flags);     
    }
}
