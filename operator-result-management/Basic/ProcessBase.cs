using System.Collections.Immutable;

namespace operator_result_management
{
    /// <summary>
    /// Reference-identified instruction graph. Run selection, execution, and client
    /// change notifications are separate from the process definition.
    /// </summary>
    public class ProcessBase<TContext> : IProcess<TContext> where TContext : IExecutionContext
    {
        private readonly Lock _sync = new();
        private ImmutableList<IInstruction<TContext>> _instructions = ImmutableList<IInstruction<TContext>>.Empty;

        public ImmutableList<IInstruction<TContext>> Instructions
        {
            get
            {
                lock (_sync)
                    return _instructions;
            }
        }

        IReadOnlyList<IInstruction> IProcess.Instructions => Instructions;

        public void Add(IInstruction<TContext> instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);

            lock (_sync)
            {
                var graph = GetDependencyGraph(instruction);
                var instructions = _instructions.ToBuilder();
                var known = new HashSet<IInstruction>(_instructions, ReferenceEqualityComparer.Instance);
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

        public IInstruction<TResult, TContext> Append<TInput, TResult>(
            IInstruction<TInput, TContext> input,
            Func<IInstruction<TInput, TContext>, IInstruction<TResult, TContext>> factory)
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

        private void RequireMember(IInstruction instruction, string parameterName)
        {
            if (!_instructions.Any(step => ReferenceEquals(step, instruction)))
                throw new ArgumentException("Instruction does not belong to this process.", parameterName);
        }

        private static List<IInstruction<TContext>> GetDependencyGraph(IInstruction<TContext> instruction)
        {
            var graph = new List<IInstruction<TContext>>();
            var visited = new HashSet<IInstruction>(ReferenceEqualityComparer.Instance);
            var visiting = new HashSet<IInstruction>(ReferenceEqualityComparer.Instance);
            Visit(instruction);
            return graph;

            void Visit(IInstruction<TContext> step)
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
                    if (dependency is not IInstruction<TContext> typedDependency)
                        throw new InvalidOperationException("Instruction dependency does not support this process's execution context.");
                    Visit(typedDependency);
                }

                visiting.Remove(step);
                graph.Add(step);
            }
        }
    }
}
