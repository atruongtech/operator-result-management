using System;
using operator_result_management;

namespace configurable_number_pipeline_poc;

public class NumberPipelineExecutionContext : IExecutionContext
{
    private Dictionary<IInstruction, IExecutionContextContainer> _operations;

    public IReadOnlyDictionary<IInstruction, IExecutionContextContainer> Operations => _operations;

    public UserInterfaceProvider PromptProvider { get; }

    public NumberPipelineExecutionContext(UserInterfaceProvider promptProvider)
    {
        _operations = [];
        PromptProvider = promptProvider;
    }

    public bool AddOperation(IInstruction instruction, IExecutionContextContainer contextContainer)
    {
        return _operations.TryAdd(instruction, contextContainer);
    }
}
