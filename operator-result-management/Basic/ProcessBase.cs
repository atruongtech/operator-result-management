using System.Collections.Immutable;

namespace operator_result_management
{
    /// <summary>
    /// Read-only, context-independent view of a process definition for display and selection.
    /// Execution and graph edits use the context-typed ProcessBase.
    /// </summary>
    public abstract class ProcessBase
    {
        /// <summary>
        /// Distinct instructions in dependency-first insertion order, identified by reference.
        /// Membership edits replace the snapshot without changing previously returned lists.
        /// Instruction objects are shared references, not deep-copy snapshots.
        /// </summary>
        public abstract IReadOnlyList<InstructionBase> Instructions { get; }
    }

    /// <summary>
    /// Library-owned, reference-identified instruction graph. Custom processes compose
    /// instructions through Add rather than replace graph behavior.
    /// Run selection, execution, and client change notifications are separate.
    /// </summary>
    public class ProcessBase<TContext> : ProcessBase where TContext : ExecutionContextBase
    {
        private readonly Lock _sync = new();
        private ImmutableList<InstructionBase<TContext>> _instructions = ImmutableList<InstructionBase<TContext>>.Empty;

        /// <summary>
        /// Read-only snapshot of context-compatible instruction references. The same list
        /// is returned until membership changes. Instructions may be mutable; coordinate
        /// edits with traversal and registration. Editing instructions does not replace
        /// this list, revalidate dependencies, or change already registered operations.
        /// </summary>
        public sealed override IReadOnlyList<InstructionBase<TContext>> Instructions
        {
            get
            {
                lock (_sync)
                    return _instructions;
            }
        }

        /// <summary>
        /// Atomically adds only the supplied step. All direct and transitive dependencies
        /// must already be process members by reference identity. Validates context
        /// compatibility and cycles before changing membership.
        /// After dependency edits, add any new dependencies first, then call Add again
        /// to revalidate the step. Existing members are not removed or reordered.
        /// Returns the supplied instruction with its static type preserved, even if
        /// it was already a member.
        /// </summary>
        public TInstruction Add<TInstruction>(TInstruction instruction)
            where TInstruction : InstructionBase<TContext>
        {
            ArgumentNullException.ThrowIfNull(instruction);

            lock (_sync)
            {
                var graph = GetDependencyGraph(instruction);
                var known = new HashSet<InstructionBase>(_instructions, ReferenceEqualityComparer.Instance);
                foreach (var step in graph)
                {
                    if (!ReferenceEquals(step, instruction) && !known.Contains(step))
                        throw new InvalidOperationException(
                            "Instruction dependencies must be added to the process before their dependent instruction.");
                }

                if (known.Contains(instruction))
                    return instruction;

                _instructions = _instructions.Add(instruction);
                return instruction;
            }
        }

        internal static List<InstructionBase<TContext>> GetDependencyGraph(InstructionBase<TContext> instruction)
        {
            var graph = new List<InstructionBase<TContext>>();
            var visited = new HashSet<InstructionBase>(ReferenceEqualityComparer.Instance);
            var visiting = new HashSet<InstructionBase>(ReferenceEqualityComparer.Instance);
            Visit(instruction);
            return graph;

            void Visit(InstructionBase<TContext> step)
            {
                if (visiting.Contains(step))
                    throw new InvalidOperationException("Cyclic instruction dependency detected in process definition.");
                if (!visited.Add(step))
                    return;

                visiting.Add(step);
                var dependencies = step.Dependencies
                    ?? throw new InvalidOperationException("Instruction dependencies must not be null.");
                foreach (var dependency in dependencies)
                {
                    if (dependency is null)
                        throw new InvalidOperationException("Instruction dependencies must not contain null.");
                    if (dependency is not InstructionBase<TContext> typedDependency)
                        throw new InvalidOperationException("Instruction dependency does not support this process's execution context.");
                    Visit(typedDependency);
                }

                visiting.Remove(step);
                graph.Add(step);
            }
        }
    }
}
