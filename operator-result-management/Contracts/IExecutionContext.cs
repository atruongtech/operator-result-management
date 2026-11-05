namespace operator_result_management
{
    /// <summary>
    /// Represents one instance of performing a process.
    /// </summary>
    public interface IExecutionContext
    {
        IReadOnlyDictionary<IInstruction, IExecutionContextContainer> Operations { get; }

        bool AddOperation(IInstruction instruction, IExecutionContextContainer contextContainer);
    }

    public interface IExecutionContextContainer
    {
        Type ValueType {get;}
    }

    public interface IExecutionContextContainer<TResult> : IExecutionContextContainer
    {
        TResult Value {get;}
    }

    public sealed record ExecutionContextContainer<TResult>(TResult Value) : IExecutionContextContainer<TResult>
    {
        public Type ValueType => typeof(TResult);
    }
}