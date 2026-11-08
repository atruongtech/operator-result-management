using System.Collections.Immutable;

namespace operator_result_management.Basic;

public class InstructionFactoryBinding(string ParameterName, IInstruction BoundInstruction)
{
    public string ParameterName { get; } = ParameterName;
    public IInstruction BoundInstruction { get; } = BoundInstruction;
}

public abstract class BindingResponse
{
    
}

public class ReadyResponse(ImmutableList<InstructionFactoryBinding> bindings, Func<IInstruction> create) : BindingResponse
{
    public ImmutableList<InstructionFactoryBinding> Bindings { get; } = bindings;
    public Func<IInstruction> Create { get; } = create;
}

public class FailedResponse(string Reason) : BindingResponse
{
    public string Reason { get; } = Reason;
}

public interface IInstructionFactory
{
    BindingResponse BindInputs(IReadOnlyList<IInstruction> inputs);
}