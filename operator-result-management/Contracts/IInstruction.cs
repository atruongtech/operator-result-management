namespace operator_result_management
{
    /// <summary>
    /// Defines a step whose instance identifies shared work within an execution context.
    /// Separate instances represent separate steps, even with identical configuration.
    /// Configuration and dependencies must remain immutable after construction so that
    /// editing a process definition cannot change an existing run.
    /// </summary>
    public interface IInstruction
    {
        /// <summary>
        /// Stable metadata identifier for this instruction. Not used as the registry key.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Immutable direct upstream dependencies, in input order. Must contain every
        /// instruction whose operation this step consumes, with no null entries.
        /// Registration must resolve these same dependencies in the owning context.
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