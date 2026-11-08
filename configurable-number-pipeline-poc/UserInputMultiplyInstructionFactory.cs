using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class UserInputMultiplyInstructionFactory : IInstructionFactory
{
    public BindingResponse BindInputs(IReadOnlyList<IInstruction> inputs)
    {
        if (inputs.Count != 1)
            return new FailedResponse("User input multiply instruction takes exactly 1 input.", typeof(UserInputMultiplyInstruction));

        return inputs.Single() is IInstruction<int, NumberPipelineExecutionContext> validInput
            ? new ReadyResponse([new InstructionFactoryBinding("input", validInput)], () => new UserInputMultiplyInstruction(validInput), typeof(UserInputMultiplyInstruction))
            : new FailedResponse($"User input multiply instruction requires one {typeof(IInstruction<int, NumberPipelineExecutionContext>)}.", typeof(UserInputMultiplyInstruction));
    }
}
