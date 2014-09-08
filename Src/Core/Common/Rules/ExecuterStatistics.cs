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

    public sealed class ExecuterStatistics
    {
        private const int FineGrainedUpdateFreq = 1000;
        private delegate void Writer();
        private SpinLock execLock = new SpinLock();
        private LiftedInt nStrata = LiftedInt.Unknown;
        private LiftedInt currentStratum = LiftedInt.Unknown;
        private LiftedInt currentFixpointSize = LiftedInt.Unknown;
        private FixedDomMap<CoreRule, ActivationStatistics> activations = null;

        private int lastFxpAddTime = 0;

        /// <summary>
        /// The total number of core rules being executed.
        /// </summary>
        public LiftedInt NRules
        {
            get
            {
                return Read(() => activations == null ? LiftedInt.Unknown : activations.Count);
            }
        }

        /// <summary>
        /// Total number of strata to execute.
        /// </summary>
        public LiftedInt NStrata
        {
            get
            {
                return Read(() => nStrata);
            }

            internal set
            {
                Write(() => nStrata = value);
            }
        }

        /// <summary>
        /// The current stratum being executed.
        /// </summary>
        public LiftedInt CurrentStratum
        {
            get
            {
                return Read(() => currentStratum);
            }

            internal set
            {
                Write(() => currentStratum = value);
            }
        }

        /// <summary>
        /// The current size of the fixpoint.
        /// </summary>
        public LiftedInt CurrentFixpointSize
        {
            get
            {
                return Read(() => currentFixpointSize);
            }

            internal set
            {
                Write(() => currentFixpointSize = value);
            }
        }

        /// <summary>
        /// Returns null if activation statistics are not yet known.
        /// </summary>
        public IEnumerable<ActivationStatistics> Activations
        {
            get 
            {
                return Read(() => activations == null ? null : activations.Values); 
            }
        }

        internal Action<Term, ProgramName, Node, CancellationToken> FireAction
        {
            get;
            private set;
        }

        public ExecuterStatistics(Action<Term, ProgramName, Node, CancellationToken> fireAction)
        {
            FireAction = fireAction;
        }
        
        internal void SetRules(Set<CoreRule> rules)
        {
            Write(() =>
                {
                    Contract.Assert(activations == null);
                    activations = new FixedDomMap<CoreRule, ActivationStatistics>(rules, (r) => new ActivationStatistics(r));
                });
        }

        internal ActivationStatistics GetActivations(CoreRule rule)
        {
            return Read(() => activations[rule]);
        }

        internal void RecFxpAdd(int fixpointSize)
        {
            if (lastFxpAddTime % FineGrainedUpdateFreq == 0)
            {
                lastFxpAddTime = 1;
                CurrentFixpointSize = fixpointSize;
            }
            else
            {
                ++lastFxpAddTime;
            }
        }

        internal ActivationStatistics GetStatistics(CoreRule rule)
        {
            throw new NotImplementedException();
        }

        private T Read<T>(Func<T> reader)
        {
            bool gotLock = false;
            try
            {
                execLock.Enter(ref gotLock);
                return reader();
            }
            finally
            {
                if (gotLock)
                {
                    execLock.Exit();
                }
            }
        }

        private void Write(Writer writer)
        {
            bool gotLock = false;
            try
            {
                execLock.Enter(ref gotLock);
                writer();
            }
            finally
            {
                if (gotLock)
                {
                    execLock.Exit();
                }
            }
        }
    }
}
