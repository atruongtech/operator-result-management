using System.Collections.Immutable;

namespace operator_result_management.Basic;

/// <summary>
/// Defines typed input requirements without exposing binding protocol details.
/// </summary>
public static class InstructionFactory
{
    public static InstructionFactorySchema<TContext> For<TContext>()
        where TContext : ExecutionContextBase => new();
}

/// <summary>
/// Declares any number of fixed inputs followed by at most one variadic group.
/// Creates freezes this schema. Schema construction is not thread-safe.
/// </summary>
public sealed class InstructionFactorySchema<TContext> where TContext : ExecutionContextBase
{
    private readonly List<FactoryParameterDefinition> _parameterDefinitions = [];
    private bool _frozen;

    internal InstructionFactorySchema() { }

    public InstructionCreationParameterKey<TResult, TContext> Input<TResult>(string parameterName) =>
        new(Add<TResult>(parameterName, null));

    public InstructionCreationParameterGroupKey<TResult, TContext> Many<TResult>(
        string parameterName, int minimumCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumCount);
        return new(Add<TResult>(parameterName, minimumCount));
    }

    /// <summary>
    /// Captures the schema and defers instruction construction until ReadyResponse.Create.
    /// The created instruction must have exactly the declared type and bound dependencies.
    /// </summary>
    public InstructionFactory<TContext> Creates<TInstruction>(
        Func<InstructionCreationArguments<TContext>, TInstruction> create)
        where TInstruction : InstructionBase<TContext>
    {
        ArgumentNullException.ThrowIfNull(create);
        EnsureMutable();
        _frozen = true;
        return new InstructionFactory<TContext>(
            this, _parameterDefinitions.ToArray(), create, typeof(TInstruction));
    }

    private FactoryParameterDefinition Add<TResult>(string parameterName, int? minimumCount)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);
        if (_parameterDefinitions.Any(definition => definition.Name == parameterName))
            throw new ArgumentException("Input parameter names must be unique.", nameof(parameterName));
        if (_parameterDefinitions.Count > 0 && _parameterDefinitions[^1].MinimumCount.HasValue)
            throw new InvalidOperationException("A variadic input group must be the last declaration.");

        var definition = new FactoryParameterDefinition(
            this, _parameterDefinitions.Count, parameterName, typeof(InstructionBase<TResult, TContext>),
            input => input is InstructionBase<TResult, TContext>, minimumCount);
        _parameterDefinitions.Add(definition);
        return definition;
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException("The instruction factory schema is frozen.");
    }
}

/// <summary>
/// Binds candidate instructions using a frozen schema and library-owned validation.
/// Create factories through InstructionFactory.For and the schema's Creates method.
/// </summary>
public sealed class InstructionFactory<TContext> where TContext : ExecutionContextBase
{
    private readonly object _owner;
    private readonly FactoryParameterDefinition[] _definitions;
    private readonly Func<InstructionCreationArguments<TContext>, InstructionBase<TContext>> _create;
    private readonly Type _instructionType;

    internal InstructionFactory(
        object owner,
        FactoryParameterDefinition[] definitions,
        Func<InstructionCreationArguments<TContext>, InstructionBase<TContext>> create,
        Type instructionType)
    {
        _owner = owner;
        _definitions = definitions;
        _create = create;
        _instructionType = instructionType;
    }

    public BindingResponse<TContext> BindInputs(IReadOnlyList<InstructionBase> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var snapshot = inputs.ToArray();
        var variadic = _definitions.Length > 0 && _definitions[^1].MinimumCount.HasValue
            ? _definitions[^1]
            : null;
        var fixedCount = _definitions.Length - (variadic is null ? 0 : 1);
        var minimumCount = (long)fixedCount + (variadic?.MinimumCount ?? 0);
        if (variadic is null ? snapshot.Length != fixedCount : snapshot.Length < minimumCount)
        {
            var requirement = variadic is null ? $"exactly {fixedCount}" : $"at least {minimumCount}";
            return Failure($"Instruction requires {requirement} inputs; received {snapshot.Length}.");
        }

        var bindings = ImmutableList.CreateBuilder<InstructionFactoryBinding>();
        foreach (var definition in _definitions)
        {
            var end = definition.MinimumCount.HasValue ? snapshot.Length : definition.Index + 1;
            for (var index = definition.Index; index < end; index++)
            {
                if (!definition.Accepts(snapshot[index]))
                    return Failure(
                        $"Input '{definition.Name}' at index {index} requires {definition.InstructionType}; " +
                        $"received {snapshot[index]?.GetType().ToString() ?? "null"}.");
                bindings.Add(new InstructionFactoryBinding(definition.Name, snapshot[index]));
            }
        }

        var arguments = new InstructionCreationArguments<TContext>(_owner, snapshot);
        return new ReadyResponse<TContext>(bindings.ToImmutable(), () =>
        {
            var instruction = _create(arguments);
            if (instruction is null || instruction.GetType() != _instructionType)
                throw new InvalidOperationException(
                    $"The factory must create an instruction of type {_instructionType}.");

            var dependencies = instruction.Dependencies;
            if (dependencies is null || dependencies.Count != snapshot.Length)
                throw DependencyMismatch();
            for (var index = 0; index < snapshot.Length; index++)
            {
                if (!ReferenceEquals(dependencies[index], snapshot[index]))
                    throw DependencyMismatch();
            }
            return instruction;
        }, _instructionType);
    }

    private FailedResponse<TContext> Failure(string reason) =>
        new(reason, _instructionType);

    private static InvalidOperationException DependencyMismatch() =>
        new("Created instruction dependencies must match bound inputs by reference and order.");
}

internal sealed record FactoryParameterDefinition(
    object Owner,
    int Index,
    string Name,
    Type InstructionType,
    Func<InstructionBase, bool> Accepts,
    int? MinimumCount);
