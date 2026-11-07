namespace operator_result_management
{
    /// <summary>
    /// Defines a step whose instance identifies shared work within an execution context.
    /// Separate instances represent separate steps, even with identical configuration.
    /// Configuration and dependencies may be mutable. On registration, implementations
    /// must capture the configuration and upstream operations needed by that run so later
    /// instruction edits do not affect registered operations. Copy mutable configuration
    /// where needed; the context does not deep-copy instructions or their configuration.
    /// Callers must coordinate edits with process graph traversal and registration.
    /// </summary>
    public interface IInstruction
    {
        /// <summary>
        /// Stable metadata identifier for this instruction. Not used as the registry key.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Current direct upstream dependencies, in input order. Must contain every
        /// instruction whose operation this step consumes, with no null entries.
        /// Dependencies must remain stable during graph traversal and registration.
        /// Registration must resolve these same dependencies in the owning context;
        /// later edits must not rewire already registered operations.
        /// </summary>
        IReadOnlyList<IInstruction> Dependencies { get; }
    }

    /// <summary>
    /// Represents a defined step in a repeatable process.
    /// The same instruction instance shares one operation per context; a new context
    /// creates an independent operation for a new run.
    /// </summary>
    /// <typeparam name="TContext">The context required by this step.</typeparam>
    public interface IInstruction<in TContext> : IInstruction
     where TContext : IExecutionContext 
    {
        /// <summary>
        /// Returns or registers this step's operation and its dependencies in the given context.
        /// Implementations must use context.GetOrAddOperation with this instruction as the key,
        /// resolve dependencies in the same context, and not start execution during registration.
        /// The factory must capture run-specific configuration and upstream operations,
        /// rather than defer reads of mutable instruction state until execution.
        /// Later edits apply to new contexts, not an operation already registered here.
        /// Prefer context.Register(instruction) at the consumer entry point.
        /// New registrations must complete before executing the context.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The shared operation instance for this instruction and context.</returns>

        IOperationInstance GetOperation(TContext context);
    }

    /// <summary>
    /// Defines a step with a typed result and the same per-context sharing contract.
    /// </summary>
    public interface IInstruction<TResult, TContext> : IInstruction<TContext>
     where TContext : IExecutionContext
    {        
        /// <summary>
        /// Returns or registers the shared typed operation and its transitive dependencies,
        /// without executing them. Must obey the same registration contract as GetOperation.
        /// </summary>
        IOperationInstance<TResult> GetTypedOperation(TContext context);
    }
}