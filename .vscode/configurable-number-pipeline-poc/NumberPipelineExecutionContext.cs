using System;
using operator_result_management;

namespace configurable_number_pipeline_poc;

public class NumberPipelineExecutionContext : operator_result_management.ExecutionContext
{
    public UserInterfaceProvider PromptProvider { get; }

    public NumberPipelineExecutionContext(UserInterfaceProvider promptProvider, CancellationToken cancellationToken = default)
        : base(cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(promptProvider);
        PromptProvider = promptProvider;
    }
}
