using System.Collections.Immutable;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public static class MinMaxDifferenceInstructionFactory
{
    public static InstructionFactory<NumberPipelineExecutionContext> Create()
    {
        var schema = InstructionFactory.For<NumberPipelineExecutionContext>();
        var input = schema.Input<ImmutableList<int>>("input");
        return schema.Creates(inputs => new MinMaxDifferenceInstruction(inputs.Get(input)));
    }
}
