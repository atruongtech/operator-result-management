namespace operator_result_management
{
    /// <summary>
    /// Owns the operation graph, cancellation token, and execution of one process run.
    /// Instruction identity is reference identity, not Id or value equality.
    /// Reuse an instruction instance to share work within a run; use a new context
    /// for independent operations and execution.
    /// </summary>
    public interface IExecutionContext
    {
        /// <summary>
        /// Returns a read-only snapshot of registered executable operations,
        /// keyed by instruction reference identity.
        /// </summary>
        IReadOnlyDictionary<IInstruction, IOperationInstance> Operations { get; }

        /// <summary>
        /// Shared cooperative cancellation for this run. The caller owns and disposes
        /// the associated CancellationTokenSource; operations capture this token at construction.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Atomically returns or creates the operation for an instruction.
        /// The same instruction instance shares one operation in this context, regardless
        /// of Id or overridden equality. Separate instruction instances remain distinct.
        /// Factories may register dependencies but must not execute or wait for operations.
        /// Instruction implementations must use this method to register their operations
        /// and resolve all dependencies in the same context.
        /// New registrations are rejected after execution starts; existing lookups remain valid.
        /// </summary>
        TOperation GetOrAddOperation<TOperation>(IInstruction instruction, Func<TOperation> factory)
            where TOperation : class, IOperationInstance;

        /// <summary>
        /// Freezes the completed graph, starts all registered operations, and awaits them all.
        /// Repeated calls return the same task, including after failure or cancellation.
        /// Register every desired output before calling this method; instruction registration
        /// discovers upstream dependencies, not downstream consumers.
        /// Each distinct registered operation is started once by this context.
        /// Consumers awaiting a shared operation's promise observe the same result, failure,
        /// or cancellation. Execution does not retry; create a new context for a new run.
        /// </summary>
        Task ExecuteAsync();
    }
}