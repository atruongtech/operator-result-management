using System;
using operator_result_management;

namespace configurable_number_pipeline_poc;

public class NumberPipelineExecutionContext : IExecutionContext
{
    private Dictionary<IInstruction, IExecutionContextContainer> _operations;

    public IReadOnlyDictionary<IInstruction, IExecutionContextContainer> Operations => _operations;   

    public NumberPipelineExecutionContext()
    {
        _operations = [];
    }

    public bool AddOperation(IInstruction instruction, IExecutionContextContainer contextContainer)
    {
        return _operations.TryAdd(instruction, contextContainer);
    }
}
