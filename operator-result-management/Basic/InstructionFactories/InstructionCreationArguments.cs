namespace operator_result_management.Basic;

/// <summary>
/// An immutable snapshot of validated instruction references for one successful binding.
/// Instruction definitions themselves are not copied.
/// </summary>
public sealed class InstructionCreationArguments<TContext> where TContext : ExecutionContextBase
{
    private readonly object _owner;
    private readonly InstructionBase[] _inputs;

    internal InstructionCreationArguments(object owner, InstructionBase[] inputs)
    {
        _owner = owner;
        _inputs = inputs;
    }

    public InstructionBase<TResult, TContext> Get<TResult>(InstructionCreationParameterKey<TResult, TContext> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateOwner(key.Definition);
        return GetInstruction<TResult>(key.Definition.Index);
    }

    public IReadOnlyList<InstructionBase<TResult, TContext>> Get<TResult>(
        InstructionCreationParameterGroupKey<TResult, TContext> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateOwner(key.Definition);
        var start = key.Definition.Index;
        var values = new InstructionBase<TResult, TContext>[_inputs.Length - start];
        for (var index = 0; index < values.Length; index++)
            values[index] = GetInstruction<TResult>(start + index);
        return Array.AsReadOnly(values);
    }

    private void ValidateOwner(FactoryParameterDefinition definition)
    {
        if (!ReferenceEquals(_owner, definition.Owner))
            throw new InvalidOperationException("Parameter key belongs to a different factory schema.");
    }

    private InstructionBase<TResult, TContext> GetInstruction<TResult>(int index) =>
        _inputs[index] is InstructionBase<TResult, TContext> instruction
            ? instruction
            : throw new InvalidOperationException("Instruction argument does not match the parameter key's instruction type.");
}
