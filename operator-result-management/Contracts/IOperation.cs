namespace operator_result_management
{
    /// <summary>
    /// Represents an operation that can produce a result.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IOperationInstance<TResult>
    {
        /// <summary>
        /// Return a handle to the eventual result of this operation.
        /// </summary>
        /// <returns>Task that returns when the operation's result is completed.</returns>
        Task<IOperationResult<TResult>> Promise();
    }

    /// <summary>
    /// Represents the resulting value from execution of an operation.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IOperationResult<TResult>
    {
        TResult GetResult();
    }
}