using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class DirectInputInstructionFactory : IInstructionFactory
{
    public BindingResponse BindInputs(IReadOnlyList<IInstruction> inputs)
    {
        if (inputs.Count != 0)
            return new FailedResponse("Direct input instruction takes no inputs.", typeof(DirectInputInstruction));

        return new ReadyResponse([], () => new DirectInputInstruction(), typeof(DirectInputInstruction));
    }
}
