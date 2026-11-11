using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public static class FactorInstructionFactory
{
    public static InstructionFactory<NumberPipelineExecutionContext> Create()
    {
        var schema = InstructionFactory.For<NumberPipelineExecutionContext>();
        var input = schema.Input<int>("input");
        return schema.Creates(inputs => new FactorInstruction(inputs.Get(input)));
    }
}
