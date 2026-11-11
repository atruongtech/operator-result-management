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
    /// instructions through Add and Append rather than replace graph behavior.
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
        /// Atomically adds a step and its transitive dependencies, validating context
        /// compatibility and cycles. Repeated additions reuse instruction references.
        /// After dependency edits, call Add again to discover the current graph;
        /// previously added instructions remain in the collection.
        /// </summary>
        public void Add(InstructionBase<TContext> instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);

            lock (_sync)
            {
                var graph = GetDependencyGraph(instruction);
                var instructions = _instructions.ToBuilder();
                var known = new HashSet<InstructionBase>(_instructions, ReferenceEqualityComparer.Instance);
                foreach (var step in graph)
                {
                    if (known.Add(step))
                        instructions.Add(step);
                }

                if (instructions.Count == _instructions.Count)
                    return;

                _instructions = instructions.ToImmutable();
            }
        }

        /// <summary>
        /// Creates and adds a typed dependent of an existing process instruction.
        /// The factory must return an instruction declaring input as a direct dependency.
        /// </summary>
        public InstructionBase<TResult, TContext> Append<TInput, TResult>(
            InstructionBase<TInput, TContext> input,
            Func<InstructionBase<TInput, TContext>, InstructionBase<TResult, TContext>> factory)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(factory);
            lock (_sync)
                RequireMember(input, nameof(input));

            var instruction = factory(input)
                ?? throw new InvalidOperationException("Dependent instruction factory returned null.");
            if (instruction.Dependencies is null ||
                !instruction.Dependencies.Any(dependency => ReferenceEquals(dependency, input)))
                throw new InvalidOperationException("Appended instruction must declare the input as a direct dependency.");

            Add(instruction);
            return instruction;
        }

        private void RequireMember(InstructionBase instruction, string parameterName)
        {
            if (!_instructions.Any(step => ReferenceEquals(step, instruction)))
                throw new ArgumentException("Instruction does not belong to this process.", parameterName);
        }

        private static List<InstructionBase<TContext>> GetDependencyGraph(InstructionBase<TContext> instruction)
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
