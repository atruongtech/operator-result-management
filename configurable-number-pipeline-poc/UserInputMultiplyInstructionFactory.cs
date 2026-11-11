using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class UserInputMultiplyInstructionFactory : IInstructionFactory<NumberPipelineExecutionContext>
{
    public BindingResponse<NumberPipelineExecutionContext> BindInputs(IReadOnlyList<InstructionBase> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse<NumberPipelineExecutionContext>("User input multiply instruction takes exactly 1 input.", typeof(UserInputMultiplyInstruction));

        return inputs.Single() is InstructionBase<int, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse<NumberPipelineExecutionContext>([new InstructionFactoryBinding("input", validInput)], () => new UserInputMultiplyInstruction(validInput), typeof(UserInputMultiplyInstruction))
            : new FailedResponse<NumberPipelineExecutionContext>($"User input multiply instruction requires one {typeof(InstructionBase<int, NumberPipelineExecutionContext>)}.", typeof(UserInputMultiplyInstruction));
    }
}
