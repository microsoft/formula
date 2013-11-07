namespace Microsoft.Formula.Common
{
    /// <summary>
    /// Used to pass a success flag between lambdas and compiler generated
    /// enumerables, which cannot have out/ref parameters.
    /// </summary>
    public class SuccessToken
    {
        //// TODO: Since this is public these should be protected by spin locks
        public bool Result
        {
            get;
            private set;
        }

        public SuccessToken()
        {
            Result = true;
        }

        public void Failed()
        {
            Result = false;
        }
    }
}
