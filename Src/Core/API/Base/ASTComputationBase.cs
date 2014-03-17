namespace Microsoft.Formula.API
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Linq;
    using System.Text;
    using System.Threading;

    using Nodes;

    internal abstract class ASTComputationBase
    {
        protected ControlToken controlToken;

        internal static IControlToken MkControlToken()
        {
            return new ControlToken();
        }

        internal static IControlToken MkControlToken(CancellationToken cancel, int cancelCheckFreq)
        {
            return new ControlToken(cancel, cancelCheckFreq);
        }

        protected ASTComputationBase(ControlToken ctok)
        {
            controlToken = ctok;
        }

        internal interface IControlToken
        {
            void Suspend();
            bool IsSuspended { get; }
        }

        protected class ControlToken : IControlToken
        {
            private CancellationToken cancel;

            private int cancelCheckFreq;

            private long unfoldCount = 1;

            public bool IsSuspended
            {
                get;
                private set;
            }

            public void Suspend()
            {
                IsSuspended = true;
            }

            public void Resume()
            {
                IsSuspended = false;
            }

            public void Unfolded()
            {
                if (cancel != default(CancellationToken) && 
                    unfoldCount % cancelCheckFreq == 0)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        IsSuspended = true;
                    }

                    unfoldCount = 1;
                }
                else
                {
                    ++unfoldCount;
                }
            }

            public ControlToken(CancellationToken cancel, int cancelCheckFreq)
            {
                Contract.Requires(cancelCheckFreq > 1);
                this.cancel = cancel;
                this.cancelCheckFreq = cancelCheckFreq;
            }

            public ControlToken()
            {
                cancel = default(CancellationToken);
                cancelCheckFreq = 0;
            }
        }
    }
}
