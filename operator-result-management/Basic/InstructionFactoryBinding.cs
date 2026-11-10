using System.Collections.Immutable;

namespace operator_result_management.Basic;

public class InstructionFactoryBinding(string ParameterName, IInstruction BoundInstruction)
{
    public string ParameterName { get; } = ParameterName;
    public IInstruction BoundInstruction { get; } = BoundInstruction;
}

public abstract class BindingResponse<TContext>(Type boundInstructionType)
    where TContext : IExecutionContext
{
    public Type BoundInstructionType { get; } = boundInstructionType;
}

public class ReadyResponse<TContext>(
    ImmutableList<InstructionFactoryBinding> bindings,
    Func<IInstruction<TContext>> create,
    Type boundInstructionType) : BindingResponse<TContext>(boundInstructionType)
    where TContext : IExecutionContext
{
    public ImmutableList<InstructionFactoryBinding> Bindings { get; } = bindings;
    public Func<IInstruction<TContext>> Create { get; } = create;
}

public class FailedResponse<TContext>(string Reason, Type boundInstructionType)
    : BindingResponse<TContext>(boundInstructionType)
    where TContext : IExecutionContext
{
    public string Reason { get; } = Reason;
}
