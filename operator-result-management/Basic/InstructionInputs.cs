using operator_result_management.Basic;

namespace operator_result_management;

/// <summary>
/// The already registered direct inputs captured for one operation creation.
/// Only declared dependencies can be resolved; lookups never register or execute work.
/// </summary>
public sealed class InstructionInputs<TContext> where TContext : ExecutionContextBase
{
    private readonly IReadOnlyDictionary<InstructionBase, OperationInstanceBase> _operations;

    internal InstructionInputs(IReadOnlyDictionary<InstructionBase, OperationInstanceBase> operations)
    {
        _operations = operations;
    }

    public OperationInstanceBase GetOperation(InstructionBase<TContext> instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return _operations.TryGetValue(instruction, out var operation)
            ? operation
            : throw new InvalidOperationException("Operation input must be declared as a direct instruction dependency.");
    }

    public OperationInstanceBase<TResult> GetOperation<TResult>(InstructionBase<TResult, TContext> instruction)
    {
        var operation = GetOperation((InstructionBase<TContext>)instruction);
        return operation is OperationInstanceBase<TResult> typed
            ? typed
            : throw new InvalidOperationException("Instruction input is registered with a different result type.");
    }
}
