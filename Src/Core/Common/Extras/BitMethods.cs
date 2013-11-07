namespace Microsoft.Formula.Common.Extras
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;

    internal static class BitMethods
    {
        private static readonly uint CHUNKSIZE = 8 * sizeof(uint);
        private static readonly uint MASKTWO = 0x55555555;
        private static readonly uint MASKNIB = 0x33333333;
        private static readonly uint MASKBYT = 0x0f0f0f0f;
        private static readonly uint MASKPOP = 0x0000003f;

        /// <summary>
        /// Returns the number of 1's in bytes
        /// </summary>
        internal static uint PopulationCount(this uint bytes)
        {
            bytes -= (bytes >> 1) & MASKTWO;
            bytes = ((bytes >> 2) & MASKNIB) + (bytes & MASKNIB);
            bytes = ((bytes >> 4) + bytes) & MASKBYT;
            bytes += bytes >> 8;
            bytes += bytes >> 16;
            return bytes & MASKPOP;
        }

        /// <summary>
        /// Returns the number of more significant 0's after the most significant 1.
        /// </summary>
        internal static uint LeadingZeroCount(this uint bytes)
        {
            bytes |= bytes >> 1;
            bytes |= bytes >> 2;
            bytes |= bytes >> 4;
            bytes |= bytes >> 8;
            bytes |= bytes >> 16;
            return CHUNKSIZE - bytes.PopulationCount();
        }

        /// <summary>
        /// Returns the index of the most significant 1. The bytes must be non-zero
        /// </summary>
        internal static uint MostSignificantOne(this uint bytes)
        {
            Contract.Requires(bytes != 0);
            bytes |= bytes >> 1;
            bytes |= bytes >> 2;
            bytes |= bytes >> 4;
            bytes |= bytes >> 8;
            bytes |= bytes >> 16;
            return bytes.PopulationCount() - 1;
        }

        /// <summary>
        /// If b is a positive big integer, then returns the largest n where 2^n is less than or equal to b.
        /// </summary>
        internal static uint MostSignificantOne(this BigInteger b)
        {
            Contract.Requires(b.Sign > 0);
            byte sigByte = 0;
            var arr = b.ToByteArray();
            var sigIndex = arr.Length - 1;
            while (sigIndex >= 0 && (sigByte = arr[sigIndex]) == 0)
            {
                --sigIndex;
            }

            Contract.Assert(sigIndex >= 0);

            if (sigIndex > 0)
            {
                return MostSignificantOne(sigByte) + 8 * ((uint)sigIndex);
            }
            else
            {
                return MostSignificantOne(sigByte);
            }
        }
    }
}
