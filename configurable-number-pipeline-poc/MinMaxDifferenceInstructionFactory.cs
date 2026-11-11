using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class MinMaxDifferenceInstructionFactory : IInstructionFactory<NumberPipelineExecutionContext>
{
    public BindingResponse<NumberPipelineExecutionContext> BindInputs(IReadOnlyList<InstructionBase> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse<NumberPipelineExecutionContext>("Min-max difference instruction takes exactly 1 input.", typeof(MinMaxDifferenceInstruction));

        return inputs.Single() is InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse<NumberPipelineExecutionContext>([new InstructionFactoryBinding("input", validInput)], () => new MinMaxDifferenceInstruction(validInput), typeof(MinMaxDifferenceInstruction))
            : new FailedResponse<NumberPipelineExecutionContext>($"Min-max difference instruction requires one {typeof(InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext>)}.", typeof(MinMaxDifferenceInstruction));
    }
}
