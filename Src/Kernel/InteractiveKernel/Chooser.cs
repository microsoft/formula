using System;
using Microsoft.Formula.CommandLine;

namespace Microsoft.Jupyter.Core
{
    public class Chooser : IChooser
    {
        public int _choice = -1;
        public Chooser()
        {
            Interactive = true;
        }

        public bool Interactive { get; set; }

        public bool GetChoice(out DigitChoiceKind choice)
        {
            if(_choice != -1)
            {
                choice = (DigitChoiceKind)_choice;
                return true;
            }
            choice = DigitChoiceKind.Zero;
            return false;
        }

        public void SetChoice(int num)
        {
            _choice = num;
        }
    }
}
