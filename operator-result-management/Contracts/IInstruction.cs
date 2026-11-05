namespace operator_result_management
{
    public interface IInstruction
    {
        Guid Id {get;}
    }

    /// <summary>
    /// Represents a defined step in a repeatable process.
    /// Each individual instance of performing the process, represented by instances of <see cref="ExecutionContext"/>, can have an individual operation
    /// corresponding to the step.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IInstruction<in TContext> : IInstruction
     where TContext : IExecutionContext 
    {
        /// <summary>
        /// Returns or registers this step's operation and its dependencies in the given context.
        /// Registration must complete before executing the context.
        /// </summary>
        /// <param name="context"></param>
        /// <returns>The shared operation instance for this instruction and context.</returns>

        IOperationInstance GetOperation(TContext context);
    }

    public interface IInstruction<TResult, TContext> : IInstruction<TContext>
     where TContext : IExecutionContext
    {        
        IOperationInstance<TResult> GetTypedOperation(TContext context);
    }
}