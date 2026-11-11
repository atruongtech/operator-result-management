using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class FactorInstructionFactory : IInstructionFactory<NumberPipelineExecutionContext>
{
    public BindingResponse<NumberPipelineExecutionContext> BindInputs(IReadOnlyList<InstructionBase> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse<NumberPipelineExecutionContext>("Factor instruction takes exactly 1 input.", typeof(FactorInstruction));
        
        return inputs.Single() is InstructionBase<int, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse<NumberPipelineExecutionContext>([new InstructionFactoryBinding("input", validInput)], () => new FactorInstruction(validInput), typeof(FactorInstruction))
            : new FailedResponse<NumberPipelineExecutionContext>($"Factor instruction requires one {typeof(InstructionBase<int, NumberPipelineExecutionContext>)}.", typeof(FactorInstruction));
    }
}
