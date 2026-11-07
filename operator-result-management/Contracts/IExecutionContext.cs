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
        /// Only the registry entries are copied. Keys and values are the original
        /// instruction and operation objects, not deep-copy snapshots. Mutable instruction
        /// keys expose their current state, not necessarily the state used by this run.
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
        /// Factories must capture run-specific configuration and upstream operations so
        /// later instruction edits cannot affect the registered operation. This method
        /// does not snapshot instruction state or refresh an existing registration.
        /// New registrations are rejected after execution starts; existing lookups remain valid.
        /// </summary>
        TOperation GetOrAddOperation<TOperation>(IInstruction instruction, Func<TOperation> factory)
            where TOperation : class, IOperationInstance;

        /// <summary>
        /// Freezes the completed graph, starts all registered operations, and awaits them all.
        /// Repeated calls return the same task, including after failure or cancellation.
        /// Register the complete process or a run's selected instructions before calling
        /// this method; individual registration discovers upstream dependencies, not downstream consumers.
        /// Each distinct registered operation is started once by this context.
        /// Consumers awaiting a shared operation's promise observe the same result, failure,
        /// or cancellation. Execution does not retry; create a new context for a new run.
        /// </summary>
        Task ExecuteAsync();
    }
}