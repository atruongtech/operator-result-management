using System.Collections.Immutable;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public static class DistributeInstructionFactory
{
    public static InstructionFactory<NumberPipelineExecutionContext> Create()
    {
        var schema = InstructionFactory.For<NumberPipelineExecutionContext>();
        var value = schema.Input<int>("toDistribute");
        var values = schema.Input<ImmutableList<int>>("distributeAmong");
        return schema.Creates(inputs => new DistributeInstruction(inputs.Get(value), inputs.Get(values)));
    }
}
