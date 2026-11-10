using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class DirectInputInstructionFactory : IInstructionFactory<NumberPipelineExecutionContext>
{
    public BindingResponse<NumberPipelineExecutionContext> BindInputs(IReadOnlyList<IInstruction> inputs)
    {
        if (inputs.Count != 0)
            return new FailedResponse<NumberPipelineExecutionContext>("Direct input instruction takes no inputs.", typeof(DirectInputInstruction));

        return new ReadyResponse<NumberPipelineExecutionContext>([], () => new DirectInputInstruction(), typeof(DirectInputInstruction));
    }
}
