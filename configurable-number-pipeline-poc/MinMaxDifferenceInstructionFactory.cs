using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class MinMaxDifferenceInstructionFactory : IInstructionFactory
{
    public BindingResponse BindInputs(IReadOnlyList<IInstruction> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse("Min-max difference instruction takes exactly 1 input.", typeof(MinMaxDifferenceInstruction));

        return inputs.Single() is IInstruction<ImmutableList<int>, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse([new InstructionFactoryBinding("input", validInput)], () => new MinMaxDifferenceInstruction(validInput), typeof(MinMaxDifferenceInstruction))
            : new FailedResponse($"Min-max difference instruction requires one {typeof(IInstruction<ImmutableList<int>, NumberPipelineExecutionContext>)}.", typeof(MinMaxDifferenceInstruction));
    }
}
