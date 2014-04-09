namespace Microsoft.Formula.Common.Rules
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Numerics;
    using System.Text;
    using System.Threading;

    using API;
    using API.ASTQueries;
    using API.Nodes;
    using Compiler;
    using Extras;
    using Terms;

    public class ActivationStatistics
    {
        private delegate void Writer();
        private SpinLock rwLock = new SpinLock();

        private CoreRule rule;
        private BigInteger minPend = -1;
        private BigInteger maxPend = -1;
        private BigInteger totalPend = 0;
        private BigInteger totalFailures = 0;
        private BigInteger totalActivations = 0;
        private BigInteger crntPendCount = 0;
        private BigInteger crntFailCount = 0;

        public int RuleId
        {
            get { return rule.RuleId; }
        }

        /// <summary>
        /// Returns a negative value if TotalActivations = 0.
        /// The smallest number of pended extensions for a single activation.
        /// </summary>
        public BigInteger MinPends
        {
            get 
            {
                return Read(() => minPend);
            }
        }

        /// <summary>
        /// Returns a negative value if TotalActivations = 0.
        /// The largest number of pended extensions for a single activation
        /// </summary>
        public BigInteger MaxPends
        {
            get 
            {
                return Read(() => minPend);
            }
        }

        /// <summary>
        /// The total number of pended extensions over all activations.
        /// </summary>
        public BigInteger TotalPends
        {
            get
            {
                return Read(() => totalPend);
            }
        }

        /// <summary>
        /// The total number of failed extensions over all activations.
        /// </summary>
        public BigInteger TotalFailures
        {
            get
            {
                return Read(() => totalFailures);
            }
        }

        /// <summary>
        /// The total number of activations.
        /// </summary>
        public BigInteger TotalActivations
        {
            get
            {
                return Read(() => totalActivations);
            }
        }

        internal ActivationStatistics(CoreRule rule)
        {
            this.rule = rule;
        }

        public void PrintRule(System.IO.TextWriter writer)
        {
            rule.Debug_PrintRule();
        }

        internal void BeginActivation()
        {
            Contract.Assert(crntPendCount == 0 && crntFailCount == 0);
            Write(() => ++totalActivations);
        }

        internal void IncPendCount()
        {
            ++crntPendCount;
        }

        internal void IncFailCount()
        {
            ++crntFailCount;
        }

        internal void EndActivation()
        {
            Write(() =>
                {
                    minPend = minPend < 0 ? crntPendCount : BigInteger.Min(minPend, crntPendCount);
                    maxPend = maxPend < 0 ? crntPendCount : BigInteger.Max(maxPend, crntPendCount);
                    totalPend += crntPendCount;
                    totalFailures += crntFailCount;
                    if (crntPendCount == 0 && crntFailCount == 0)
                    {
                        ++totalFailures;
                    }
                });
            crntPendCount = 0;
            crntFailCount = 0;
        }

        private U Read<U>(Func<U> reader)
        {
            bool gotLock = false;
            try
            {
                rwLock.Enter(ref gotLock);
                return reader();
            }
            finally
            {
                if (gotLock)
                {
                    rwLock.Exit();
                }
            }
        }

        private void Write(Writer writer)
        {
            bool gotLock = false;
            try
            {
                rwLock.Enter(ref gotLock);
                writer();
            }
            finally
            {
                if (gotLock)
                {
                    rwLock.Exit();
                }
            }
        }
    }
}