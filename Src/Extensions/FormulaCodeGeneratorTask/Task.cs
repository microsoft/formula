namespace FormulaCodeGeneratorTask
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public class FormulaCodeGeneratorTask : Task
    {
        private static readonly char[] inputSplits = new char[] { ';' };
        private static readonly char[] paramSplits = new char[] { ',' };

        [Required]
        public string GeneratorInputs
        {
            get;
            set;
        }

        [Required]
        public string DefaultNamespace
        {
            get;
            set;
        }

        public string[] Outputs {
            get
            {
                List<string> outputs = new List<string>();
                var inputs = GeneratorInputs.Split(inputSplits, StringSplitOptions.None);
                foreach (var input in inputs)
                {
                    var parameters = input.Split(paramSplits, StringSplitOptions.None);
                    if (parameters.Length > 0)
                    {
                        string inputFile = parameters[0];
                        var outputFile = inputFile + ".g.cs";
                        outputs.Add(outputFile);
                    }
                }
                return outputs.ToArray();
            }
        }
        
        public override bool Execute()
        {
            var inputs = GeneratorInputs.Split(inputSplits, StringSplitOptions.None);
            var result = true;

            foreach (var input in inputs)
            {
                var parameters = input.Split(paramSplits, StringSplitOptions.None);
                if (parameters.Length != 5)
                {
                    result = false;
                    Log.LogError("Bad input: {0}", input);
                    continue;
                }

                var genItem = new GenerateItem(
                    parameters[0],
                    string.IsNullOrWhiteSpace(parameters[1]) ? DefaultNamespace : parameters[1],
                    parameters[2],
                    parameters[3],
                    parameters[4]);

                result = genItem.Generate(this) && result;
            }

            return result;
        }
    }
}
