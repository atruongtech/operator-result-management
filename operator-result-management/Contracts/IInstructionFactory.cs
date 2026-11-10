namespace operator_result_management.Basic;

/// <summary>
/// Binds candidate inputs to instructions that support the specified execution context.
/// Implementations validate input count, result types, and context compatibility.
/// </summary>
public interface IInstructionFactory<TContext> where TContext : IExecutionContext
{
    BindingResponse<TContext> BindInputs(IReadOnlyList<IInstruction> inputs);
}