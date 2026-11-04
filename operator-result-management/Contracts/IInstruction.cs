namespace operator_result_management
{
    public interface IInstruction
    {
        Guid Id { get; }
    }

    /// <summary>
    /// Represents a defined step in a repeatable process.
    /// Each individual instance of performing the process, represented by instances of <see cref="ExecutionContext"/>, can have an individual operation
    /// corresponding to the step.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IInstruction<TResult> : IInstruction
    {
        /// <summary>
        /// Return a handle to the instance corresponding to this step in a process
        /// for the given execution context.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>Task that returns when the corresponding instance is available.</returns>
        Task<IOperationInstance<TResult>> Promise(ExecutionContext context);
    }
}