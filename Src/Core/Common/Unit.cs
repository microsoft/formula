namespace Microsoft.Formula.Common
{
    using System;

    /// <summary>
    /// A value type containing only a single member.
    /// </summary>
    public struct Unit
    {
        public static int Compare(Unit u1, Unit u2)
        {
            return 0;
        }
    }
}
