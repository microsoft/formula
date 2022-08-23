using Microsoft.Formula.Common;

namespace FormulaPluginTest;

using Microsoft.Formula.Common.Terms;

public class SineFunction : OpPluginFunc
{
    public static BaseSortKind[] ArgTypes = new[] {BaseSortKind.Real};

    public override string GetName()
    {
        return "sin";
    }
    
    public override BaseSortKind[] GetArgTypes()
    {
        return ArgTypes;
    }

    public override BaseSortKind GetReturnType()
    {
        return BaseSortKind.Real;
    }

    public override Rational Evaluate(Rational[] args)
    {
        return new Rational(Math.Sin(RationalToDouble(args[0])));

    }
}