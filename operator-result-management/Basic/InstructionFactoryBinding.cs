using System.Collections.Immutable;

namespace operator_result_management.Basic;

public class InstructionFactoryBinding(string ParameterName, InstructionBase BoundInstruction)
{
    public string ParameterName { get; } = ParameterName;
    public InstructionBase BoundInstruction { get; } = BoundInstruction;
}

public abstract class BindingResponse<TContext>(Type boundInstructionType)
    where TContext : ExecutionContextBase
{
    public Type BoundInstructionType { get; } = boundInstructionType;
}

public class ReadyResponse<TContext>(
    ImmutableList<InstructionFactoryBinding> bindings,
    Func<InstructionBase<TContext>> create,
    Type boundInstructionType) : BindingResponse<TContext>(boundInstructionType)
    where TContext : ExecutionContextBase
{
    public ImmutableList<InstructionFactoryBinding> Bindings { get; } = bindings;
    public Func<InstructionBase<TContext>> Create { get; } = create;
}

public class FailedResponse<TContext>(string Reason, Type boundInstructionType)
    : BindingResponse<TContext>(boundInstructionType)
    where TContext : ExecutionContextBase
{
    public string Reason { get; } = Reason;
}
