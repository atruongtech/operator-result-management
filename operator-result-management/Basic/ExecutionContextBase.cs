using System.Collections.ObjectModel;

namespace operator_result_management
{
    public class ExecutionContextBase : IExecutionContext
    {
        private sealed class RegistrationState
        {
            public object Sync { get; } = new();
            public Dictionary<IInstruction, ExecutionContextBase> Owners { get; } =
                new(ReferenceEqualityComparer.Instance);
        }

        private RegistrationState _registration = new();
        private readonly Dictionary<IInstruction, IOperationInstance> _operations =
            new(ReferenceEqualityComparer.Instance);
        private IReadOnlyDictionary<IInstruction, IOperationInstance>? _imports;
        private ExecutionContextBase? _parent;
        private readonly HashSet<IInstruction> _creating = new(ReferenceEqualityComparer.Instance);
        private bool _registrationClosed;
        private Lazy<Task>? _execution;

        public IReadOnlyDictionary<IInstruction, IOperationInstance> Operations
        {
            get
            {
                lock (_registration.Sync)
                    return new ReadOnlyDictionary<IInstruction, IOperationInstance>(
                        new Dictionary<IInstruction, IOperationInstance>(
                            _operations, ReferenceEqualityComparer.Instance));
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

            lock (_registration.Sync)
            {
                if (_operations.TryGetValue(instruction, out var existing) ||
                    (_imports is not null && _imports.TryGetValue(instruction, out existing)))
                {
                    if (existing is TOperation operation)
                        return operation;

                    throw new InvalidOperationException("Instruction is registered with a different operation type.");
                }

                if (_execution is not null)
                    throw new InvalidOperationException("Cannot register operations after execution has started.");
                if (_registrationClosed)
                    throw new InvalidOperationException("Cannot register operations after the scope has been frozen.");

                if (_registration.Owners.TryGetValue(instruction, out var owner) &&
                    !ReferenceEquals(owner, this))
                    throw new InvalidOperationException(
                        "Instruction belongs to another execution scope. Declare outer inputs explicitly; body-local instructions cannot be referenced outside their scope.");

                if (!_creating.Add(instruction))
                    throw new InvalidOperationException("Cyclic instruction dependency detected during registration.");

                _registration.Owners[instruction] = this;
                try
                {
                    var operation = factory()
                        ?? throw new InvalidOperationException("Operation factory returned null.");
                    _operations.Add(instruction, operation);
                    return operation;
                }
                catch
                {
                    _registration.Owners.Remove(instruction);
                    throw;
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
            lock (_registration.Sync)
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

        internal void AttachScope(
            ExecutionContextBase parent,
            IReadOnlyDictionary<IInstruction, IOperationInstance> imports)
        {
            if (ReferenceEquals(this, parent) || _parent is not null || _operations.Count != 0 ||
                _creating.Count != 0 || _execution is not null || _registrationClosed)
                throw new InvalidOperationException("A conditional body requires a fresh, unused execution context.");
            if (CancellationToken != parent.CancellationToken)
                throw new InvalidOperationException("A conditional body must use its parent's cancellation token.");

            _parent = parent;
            _imports = imports;
            _registration = parent._registration;
        }

        internal void FreezeRegistration()
        {
            lock (_registration.Sync)
                _registrationClosed = true;
        }

        internal void DiscardScope()
        {
            lock (_registration.Sync)
            {
                var ownedInstructions = _registration.Owners
                    .Where(entry => IsWithinScope(entry.Value)).Select(entry => entry.Key).ToArray();
                foreach (var instruction in ownedInstructions)
                    _registration.Owners.Remove(instruction);
            }

            bool IsWithinScope(ExecutionContextBase context)
            {
                for (ExecutionContextBase? scope = context; scope is not null; scope = scope._parent)
                    if (ReferenceEquals(scope, this))
                        return true;
                return false;
            }
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