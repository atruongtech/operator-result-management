using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public static class DirectInputInstructionFactory
{
    public static InstructionFactory<NumberPipelineExecutionContext> Create() =>
        InstructionFactory.For<NumberPipelineExecutionContext>()
            .Creates(_ => new DirectInputInstruction());
}
