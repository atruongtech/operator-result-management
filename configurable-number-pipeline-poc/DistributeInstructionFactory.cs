using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public sealed class DistributeInstructionFactory : IInstructionFactory<NumberPipelineExecutionContext>
{
    public BindingResponse<NumberPipelineExecutionContext> BindInputs(IReadOnlyList<InstructionBase> inputs)
    {
        if (inputs.Count != 2)
            return new FailedResponse<NumberPipelineExecutionContext>(
                "Distribute instruction takes exactly 2 inputs.", typeof(DistributeInstruction));

        return inputs[0] is InstructionBase<int, NumberPipelineExecutionContext> value
            && inputs[1] is InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> values
            ? new ReadyResponse<NumberPipelineExecutionContext>(
                [new InstructionFactoryBinding("toDistribute", value), new InstructionFactoryBinding("distributeAmong", values)],
                () => new DistributeInstruction(value, values), typeof(DistributeInstruction))
            : new FailedResponse<NumberPipelineExecutionContext>(
                $"Distribute instruction requires {typeof(InstructionBase<int, NumberPipelineExecutionContext>)} first and {typeof(InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext>)} second.",
                typeof(DistributeInstruction));
    }
}
