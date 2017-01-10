namespace Microsoft.Formula.CommandLine
{
    using System;

    public enum DigitChoiceKind
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9
    }

    /// <summary>
    /// Allows the user to select from a range of integer choices.
    /// Returns true if the user specified a choice. Otherwise returns false, if
    /// the user choice could not be retrieved.
    /// </summary>
    public interface IChooser
    {
        bool Interactive { get; set; }

        bool GetChoice(out DigitChoiceKind choice);
    }
}
