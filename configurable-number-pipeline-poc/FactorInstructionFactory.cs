using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class FactorInstructionFactory : IInstructionFactory
{
    public BindingResponse BindInputs(IReadOnlyList<IInstruction> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse("Factor instruction takes exactly 1 input.");
        
        return inputs.Single() is IInstruction<int, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse([new InstructionFactoryBinding("input", validInput)], () => new FactorInstruction(validInput))
            : new FailedResponse($"Factor instruction requires one {typeof(IInstruction<int, NumberPipelineExecutionContext>)}.");
    }
}
