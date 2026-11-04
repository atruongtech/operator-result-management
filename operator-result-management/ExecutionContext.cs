namespace operator_result_management
{
    public record ExecutionContext
    {
        IReadOnlyDictionary<IInstruction, IExecutionContextContainer> Operations { get; init; } = new Dictionary<IInstruction, IExecutionContextContainer>();   
    }
}