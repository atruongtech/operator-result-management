namespace operator_result_management
{
    /// <summary>
    /// Owns the operation graph, cancellation token, and execution of one process run.
    /// </summary>
    public interface IExecutionContext
    {
        /// <summary>
        /// Returns a read-only snapshot of registered executable operations.
        /// </summary>
        IReadOnlyDictionary<IInstruction, IOperationInstance> Operations { get; }

        /// <summary>
        /// Shared cooperative cancellation for this run. The caller owns and disposes
        /// the associated CancellationTokenSource; operations capture this token at construction.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Atomically returns or creates the operation for an instruction.
        /// Factories may register dependencies but must not execute or wait for operations.
        /// New registrations are rejected after execution starts; existing lookups remain valid.
        /// </summary>
        TOperation GetOrAddOperation<TOperation>(IInstruction instruction, Func<TOperation> factory)
            where TOperation : class, IOperationInstance;

        /// <summary>
        /// Freezes the completed graph, starts all registered operations, and awaits them all.
        /// Repeated calls return the same task, including after failure or cancellation.
        /// Register all roots and their dependencies before calling this method.
        /// </summary>
        Task ExecuteAsync();
    }
}