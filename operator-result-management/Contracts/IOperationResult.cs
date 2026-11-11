namespace operator_result_management
{
    /// <summary>
    /// Represents the resulting value from execution of an operation.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IOperationResult<out TResult>
    {
        TResult GetResult();
    }
}
