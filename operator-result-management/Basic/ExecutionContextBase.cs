using System.Collections.ObjectModel;
using operator_result_management.Basic;

namespace operator_result_management
{
    /// <summary>
    /// Owns the operation graph, cancellation token, and execution of one process run.
    /// Instruction identity is reference identity, not Id or value equality.
    /// Reuse an instruction instance to share work within a run; use a new context
    /// for independent operations and execution. Derive to supply run-specific services;
    /// registration, scope ownership, and execution are managed by the library.
    /// </summary>
    public class ExecutionContextBase
    {
        private sealed class RegistrationState
        {
            public object Sync { get; } = new();
            public Dictionary<InstructionBase, ExecutionContextBase> Owners { get; } =
                new(ReferenceEqualityComparer.Instance);
        }

        private RegistrationState _registration = new();
        private readonly Dictionary<InstructionBase, OperationInstanceBase> _operations =
            new(ReferenceEqualityComparer.Instance);
        private IReadOnlyDictionary<InstructionBase, OperationInstanceBase>? _imports;
        private ExecutionContextBase? _parent;
        private readonly HashSet<InstructionBase> _creating = new(ReferenceEqualityComparer.Instance);
        private bool _registrationClosed;
        private Lazy<Task>? _execution;

        /// <summary>
        /// Returns a read-only snapshot of locally owned registered executable operations,
        /// keyed by instruction reference identity.
        /// Conditional body contexts exclude imported outer operations from this mapping.
        /// Only the registry entries are copied. Keys and values are the original
        /// instruction and operation objects, not deep-copy snapshots. Mutable instruction
        /// keys expose their current state, not necessarily the state used by this run.
        /// </summary>
        public IReadOnlyDictionary<InstructionBase, OperationInstanceBase> Operations
        {
            get
            {
                lock (_registration.Sync)
                    return new ReadOnlyDictionary<InstructionBase, OperationInstanceBase>(
                        new Dictionary<InstructionBase, OperationInstanceBase>(
                            _operations, ReferenceEqualityComparer.Instance));
            }
        }

        /// <summary>
        /// Shared cooperative cancellation for this run. The caller owns and disposes
        /// the associated CancellationTokenSource; operations capture this token at construction.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        public ExecutionContextBase(CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Atomically returns or creates the operation for an instruction.
        /// The same instruction instance shares one operation in this context, regardless
        /// of Id or overridden equality. Separate instruction instances remain distinct.
        /// Factories may register dependencies but must not execute or wait for operations.
        /// InstructionBase owns registration and dependency resolution through this method.
        /// Factories must capture run-specific configuration and upstream operations so
        /// later instruction edits cannot affect the registered operation. This method
        /// does not snapshot instruction state or refresh an existing registration.
        /// New registrations are rejected after execution starts; existing lookups remain valid.
        /// Conditional body scopes freeze registration when prepared. Declared imported
        /// instructions resolve to outer operations without adding local ownership.
        /// References to instructions owned by another scope require an explicit import.
        /// </summary>
        internal TOperation GetOrAddOperation<TOperation>(InstructionBase instruction, Func<TOperation> factory)
            where TOperation : OperationInstanceBase
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

        /// <summary>
        /// Freezes the completed graph, starts all locally owned registered operations,
        /// and awaits them all. A conditional operation activates its owned body separately.
        /// Repeated calls return the same task, including after failure or cancellation.
        /// Register the complete process or a run's selected instructions before calling
        /// this method; individual registration discovers upstream dependencies, not downstream consumers.
        /// Each distinct registered operation is started once by this context.
        /// Consumers awaiting a shared operation's promise observe the same result, failure,
        /// or cancellation. Execution does not retry; create a new context for a new run.
        /// </summary>
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
                        .Distinct<OperationInstanceBase>(ReferenceEqualityComparer.Instance).ToArray();
                    _execution = new(() => ExecuteAllAsync(operations),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                }

                execution = _execution;
            }

            return execution.Value;
        }

        internal void AttachScope(
            ExecutionContextBase parent,
            IReadOnlyDictionary<InstructionBase, OperationInstanceBase> imports)
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

        private async Task ExecuteAllAsync(OperationInstanceBase[] operations)
        {
            await Task.WhenAll(operations.Select(ExecuteOperationAsync)).ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task ExecuteOperationAsync(OperationInstanceBase operation)
        {
            await operation.Execute().ConfigureAwait(false);
        }
    }
}