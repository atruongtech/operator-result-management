namespace operator_result_management
{

    public interface IOperationInstance
    {
        /// <summary>
        /// Starts the operation. Implementations based on
        /// <see cref="Basic.OperationInstanceBase{T}"/> execute once per instance and
        /// return the same task on every call, without retrying failures or cancellation.
        /// Execution errors are reported through both this task and the result promise.
        /// Cancellation uses the owning context's token and cancels both tasks cooperatively.
        /// </summary>
        Task Execute();
    }

    /// <summary>
    /// Represents an operation that can produce a result.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IOperationInstance<TResult> : IOperationInstance
    {
        /// <summary>
        /// Observes the eventual result without starting execution.
        /// Implementations based on <see cref="Basic.OperationInstanceBase{T}"/> publish
        /// results only after successful execution and propagate execution failures or
        /// cancellation to this task. Execute the owning context to start the registered graph,
        /// or call Execute to start only this operation.
        /// All consumers share the published result; immutable result values are recommended.
        /// Consumers of mutable values must coordinate access.
        /// </summary>
        /// <returns>Task that returns when the operation's result is completed.</returns>
        Task<IOperationResult<TResult>> Promise();
    }

    /// <summary>
    /// Represents the resulting value from execution of an operation.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IOperationResult<out TResult>
    {
        TResult GetResult();
    }
}