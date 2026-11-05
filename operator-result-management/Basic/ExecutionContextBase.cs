using System.Collections.ObjectModel;

namespace operator_result_management
{
    public class ExecutionContextBase : IExecutionContext
    {
        private readonly object _sync = new();
        private readonly Dictionary<IInstruction, IOperationInstance> _operations = [];
        private readonly HashSet<IInstruction> _creating = [];
        private Lazy<Task>? _execution;

        public IReadOnlyDictionary<IInstruction, IOperationInstance> Operations
        {
            get
            {
                lock (_sync)
                    return new ReadOnlyDictionary<IInstruction, IOperationInstance>(
                        new Dictionary<IInstruction, IOperationInstance>(_operations));
            }
        }

        public CancellationToken CancellationToken { get; }

        public ExecutionContextBase(CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
        }

        public TOperation GetOrAddOperation<TOperation>(IInstruction instruction, Func<TOperation> factory)
            where TOperation : class, IOperationInstance
        {
            ArgumentNullException.ThrowIfNull(instruction);
            ArgumentNullException.ThrowIfNull(factory);

            lock (_sync)
            {
                if (_operations.TryGetValue(instruction, out var existing))
                {
                    if (existing is TOperation operation)
                        return operation;

                    throw new InvalidOperationException("Instruction is registered with a different operation type.");
                }

                if (_execution is not null)
                    throw new InvalidOperationException("Cannot register operations after execution has started.");

                if (!_creating.Add(instruction))
                    throw new InvalidOperationException("Cyclic instruction dependency detected during registration.");

                try
                {
                    var operation = factory()
                        ?? throw new InvalidOperationException("Operation factory returned null.");
                    _operations.Add(instruction, operation);
                    return operation;
                }
                finally
                {
                    _creating.Remove(instruction);
                }
            }
        }

        public Task ExecuteAsync()
        {
            Lazy<Task> execution;
            lock (_sync)
            {
                if (_creating.Count != 0)
                    throw new InvalidOperationException("Cannot execute while operation registration is in progress.");

                if (_execution is null)
                {
                    var operations = _operations.Values
                        .Distinct<IOperationInstance>(ReferenceEqualityComparer.Instance).ToArray();
                    _execution = new(() => ExecuteAllAsync(operations),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                }

                execution = _execution;
            }

            return execution.Value;
        }

        private async Task ExecuteAllAsync(IOperationInstance[] operations)
        {
            await Task.WhenAll(operations.Select(ExecuteOperationAsync)).ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task ExecuteOperationAsync(IOperationInstance operation)
        {
            await operation.Execute().ConfigureAwait(false);
        }
    }
}