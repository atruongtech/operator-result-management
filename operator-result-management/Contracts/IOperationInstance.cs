namespace operator_result_management
{

    public interface IOperationInstance
    {
        /// <summary>
        /// Observes success, failure, or cancellation without starting execution.
        /// Remains pending until the operation executes. Returns the same task as
        /// <see cref="Execute"/> before, during, and after execution.
        /// </summary>
        Task Completion { get; }

        /// <summary>
        /// Starts the operation and returns <see cref="Completion"/>.
        /// Implementations based on either OperationInstanceBase execute once per instance,
        /// without retrying failures or cancellation. Execution errors and cooperative
        /// cancellation are published through this shared task and any typed result promise.
        /// Cancellation uses the owning context's token.
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