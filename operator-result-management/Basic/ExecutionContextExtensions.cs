namespace operator_result_management
{
    /// <summary>
    /// Explicit entry points for registering the desired outputs of a process run.
    /// </summary>
    public static class ExecutionContextExtensions
    {
        /// <summary>
        /// Registers an instruction and its transitive dependencies without executing them.
        /// Reusing the same instruction instance in this context returns the same operation;
        /// a separate context creates independent operations. Register every desired output
        /// before execution starts. Existing registrations remain accessible afterward.
        /// </summary>
        public static IOperationInstance<TResult> Register<TResult, TContext>(
            this TContext context, IInstruction<TResult, TContext> instruction)
            where TContext : IExecutionContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(instruction);
            return instruction.GetTypedOperation(context);
        }

        /// <summary>
        /// Registers an instruction and its transitive dependencies without executing them.
        /// Uses the same reference-identity and registration-lifetime rules as typed registration.
        /// </summary>
        public static IOperationInstance Register<TContext>(
            this TContext context, IInstruction<TContext> instruction)
            where TContext : IExecutionContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(instruction);
            return instruction.GetOperation(context);
        }
    }
}
