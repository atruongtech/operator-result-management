using System.Collections.Immutable;

namespace operator_result_management
{
    /// <summary>
    /// An editable process definition, not an execution or a UI component.
    /// Instructions form a dependency graph; display order does not imply sequential execution.
    /// Run selection and change notifications belong to callers, not the definition.
    /// </summary>
    public interface IProcess
    {
        /// <summary>
        /// Stable read-only collection of distinct instructions in dependency-first insertion order.
        /// Instructions are identified by reference, not Id or value equality.
        /// Edits replace this collection rather than mutate previously returned collections.
        /// </summary>
        IReadOnlyList<IInstruction> Instructions { get; }
    }

    /// <summary>
    /// A process definition whose instructions support the given execution context.
    /// Instructions must be immutable; edits add steps without changing existing runs.
    /// </summary>
    public interface IProcess<TContext> : IProcess where TContext : IExecutionContext
    {
        /// <summary>
        /// Immutable list of context-compatible instruction references.
        /// Instructions themselves are shared immutable definitions, not copies.
        /// Process retains the same collection until its graph changes, allowing clients
        /// to compare collection references when detecting effective edits.
        /// </summary>
        new ImmutableList<IInstruction<TContext>> Instructions { get; }

        /// <summary>
        /// Atomically adds a step and its transitive dependencies, validating context
        /// compatibility and cycles. Repeated additions reuse existing instruction instances.
        /// </summary>
        void Add(IInstruction<TContext> instruction);

        /// <summary>
        /// Creates and adds a typed dependent of an existing process instruction.
        /// The factory must return an instruction declaring input as a direct dependency.
        /// </summary>
        IInstruction<TResult, TContext> Append<TInput, TResult>(
            IInstruction<TInput, TContext> input,
            Func<IInstruction<TInput, TContext>, IInstruction<TResult, TContext>> factory);
    }
}
