namespace Microsoft.Formula.Extensions.CodeGenerator
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Contracts;
    using System.Globalization;

    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Shell.Interop;
    //// using Microsoft.Internal.CodeGenerator.Host;

    /// <summary>
    /// Encapsulates an instance of <see cref="IVsGeneratorProgress"/> and exposes
    /// an easier interface for generators to use to generate errors or warnings
    /// surfaced to the user via the VS IDE.
    /// </summary>
    public sealed class ProgressReporter
    {
        /// <summary>The instance of <see cref="IVsGeneratorProgress"/> used to surface generator warnings or errors the VS IDE.</summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private readonly IVsGeneratorProgress bridge;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProgressReporter"/> class.
        /// </summary>
        /// <param name="bridge">The bridge.</param>
        public ProgressReporter(IVsGeneratorProgress bridge)
            : base()
        {
            Contract.Assert(bridge != null);
            this.bridge = bridge;
        }

        /// <summary>
        /// Reports a generator error message to the VS IDE.
        /// </summary>
        /// <param name="line">The 1-based line number. 0 for none.</param>
        /// <param name="column">The 1-based column number. 0 for none.</param>
        /// <param name="message">The error message.</param>
        /// <returns><c>true</c>, if error reporting succeeded; <c>false</c>, otherwise.</returns>
        public bool Error(string fileName, int line, int column, string message)
        {
            Trace.WriteLine(String.Format(CultureInfo.CurrentCulture, "FormulaCodeGenerator : ERROR : {0} ({1}, {2}) : {3}", fileName, line, column, message));
            return this.ReportMessage(false, line, column, message);
        }

        /// <summary>
        /// Reports a debug message to the .NET trace listeners.
        /// </summary>
        /// <param name="line">The 1-based line number. 0 for none.</param>
        /// <param name="column">The 1-based column number. 0 for none.</param>
        /// <param name="message">The message.</param>
        public void Info(string fileName, int line, int column, string message)
        {
            Trace.WriteLine(String.Format(CultureInfo.CurrentCulture, "FormulaCodeGenerator : INFO : {0} ({1}, {2}) : {3}", fileName, line, column, message));
        }

        /// <summary>
        /// Reports a generator warning message to the VS IDE.
        /// </summary>
        /// <param name="line">The 1-based line number. 0 for none.</param>
        /// <param name="column">The 1-based column number. 0 for none.</param>
        /// <param name="message">The warning message.</param>
        /// <returns><c>true</c>, if warning reporting succeeded; <c>false</c>, otherwise.</returns>
        public bool Warning(string fileName, int line, int column, string message)
        {
            Trace.WriteLine(String.Format(CultureInfo.CurrentCulture, "FormulaCodeGenerator : WARNING : {0} ({1}, {2}) : {3}", fileName, line, column, message));
            return this.ReportMessage(true, line, column, message);
        }

        /// <summary>
        /// Maps a user provided input location to the format expected by the VS IDE task list.
        /// </summary>
        /// <param name="userLine">The user provided line number.</param>
        /// <param name="userColumn">The user provided column number.</param>
        /// <param name="mappedLine">The mapped line number.</param>
        /// <param name="mappedColumn">The mapped column number.</param>
        private static void MapLocation(int userLine, int userColumn, out uint mappedLine, out uint mappedColumn)
        {
            if (userLine == 0)
            {
                unchecked { mappedLine = (uint)-1; }
            }
            else
            {
                mappedLine = (uint) (userLine - 1);
            }

            if (userColumn == 0 || userLine == 0)
            {
                unchecked { mappedColumn = (uint)-1; }
            }
            else
            {
                mappedColumn = (uint) userColumn;
            }
        }

        /// <summary>
        /// Maps a VS status integer to <c>true</c> or <c>false</c>.
        /// </summary>
        /// <param name="status">The VS status integer.</param>
        /// <returns><c>true</c>, if <paramref name="status"/> equals <see cref="F:VSConstants.S_OK"/>; <c>false</c>, otherwise.</returns>
        private static bool MapStatus(int status)
        {
            switch (status)
            {
                case VSConstants.S_OK:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reports a warning or error message to the VS IDE.
        /// </summary>
        /// <param name="isError">Indicates if the message is an error or a warning.</param>
        /// <param name="line">The line number.</param>
        /// <param name="column">The column number.</param>
        /// <param name="message">The message.</param>
        /// <param name="arguments">The optional arguments to add to the message if <paramref name="message"/> contains formatting placeholders.</param>
        /// <returns><c>true</c>, if error reporting succeeded; <c>false</c>, otherwise.</returns>
        private bool ReportMessage(bool isWarning, int line, int column, string message)
        {
            message = !string.IsNullOrEmpty(message) ? message : ("Unknown " + (isWarning ? "warning" : "error"));
            line = line >= 0 ? line : 0;
            column = column >= 0 ? column : 0;

            uint mLine = 0;
            uint mColumn = 0;

            ProgressReporter.MapLocation(line, column, out mLine, out mColumn);
            int vsStatus = 0;
            int level = (isWarning ? 1 : 0);
            this.bridge.GeneratorError(level, 0, message, mLine, mColumn);
            return ProgressReporter.MapStatus(vsStatus);
        }
    }
}
