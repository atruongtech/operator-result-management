namespace operator_result_management
{
    /// <summary>
    /// Immutable run-specific selection of instruction references from a process.
    /// Only the selection is fixed; instruction objects are not copied and may be mutable.
    /// Replaying the request in a fresh context uses instruction state at registration,
    /// not necessarily the state used by an earlier run.
    /// Selected instructions need not be graph sinks. Their upstream dependencies are
    /// discovered during registration, not stored as selection state on the process.
    /// </summary>
    public sealed class ProcessRunRequest<TContext> where TContext : IExecutionContext
    {
        /// <summary>
        /// Distinct explicitly selected instructions in selection order. The executable
        /// graph also includes their transitive dependencies; inspect context.Operations
        /// after registration for that graph. An empty selection registers no work.
        /// </summary>
        public IReadOnlyList<IInstruction<TContext>> SelectedInstructions { get; }

        /// <summary>
        /// Captures references to every instruction currently in the process.
        /// Later additions do not change this request. Instruction state is not snapshotted.
        /// </summary>
        public ProcessRunRequest(IProcess<TContext> process)
        {
            ArgumentNullException.ThrowIfNull(process);
            SelectedInstructions = Array.AsReadOnly(process.Instructions.ToArray());
        }

        /// <summary>
        /// Captures a run selection without modifying its process definition.
        /// Null entries and instruction instances outside the process are rejected;
        /// duplicate references are selected once, regardless of Id or overridden equality.
        /// </summary>
        public ProcessRunRequest(
            IProcess<TContext> process,
            IEnumerable<IInstruction<TContext>> selectedInstructions)
        {
            ArgumentNullException.ThrowIfNull(process);
            ArgumentNullException.ThrowIfNull(selectedInstructions);
            var members = new HashSet<IInstruction>(process.Instructions, ReferenceEqualityComparer.Instance);
            var selected = new HashSet<IInstruction>(ReferenceEqualityComparer.Instance);
            var instructions = new List<IInstruction<TContext>>();
            foreach (var instruction in selectedInstructions)
            {
                if (instruction is null)
                    throw new ArgumentException("Selected instructions must not contain null.", nameof(selectedInstructions));
                if (!members.Contains(instruction))
                    throw new ArgumentException("Selected instruction does not belong to the process.", nameof(selectedInstructions));
                if (selected.Add(instruction))
                    instructions.Add(instruction);
            }

            SelectedInstructions = Array.AsReadOnly(instructions.ToArray());
        }

        /// <summary>
        /// Registers the selection and its transitive dependencies without executing.
        /// Use a fresh context for partial execution: separately registered operations
        /// would also execute. Later process or caller selection edits do not change this request.
        /// Instruction configuration and dependencies are captured by instruction
        /// implementations when creating operations, not when constructing this request.
        /// </summary>
        public void Register(TContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            foreach (var instruction in SelectedInstructions)
                context.Register(instruction);
        }
    }
}
