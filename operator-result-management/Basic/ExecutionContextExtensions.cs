namespace operator_result_management
{
    /// <summary>
    /// Explicit entry points for registering instructions, complete processes, or run selections.
    /// </summary>
    public static class ExecutionContextExtensions
    {
        /// <summary>
        /// Registers an instruction and its transitive dependencies without executing them.
        /// Reusing the same instruction instance in this context returns the same operation;
        /// a separate context creates independent operations. Register every selected instruction
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

        /// <summary>
        /// Captures the process's current instruction references in a run request and registers
        /// them without executing. Returns that request for observing or replaying the selection.
        /// Later process additions affect future requests only.
        /// The request retains live instruction references, not snapshots of their state;
        /// replay in a fresh context uses their configuration at registration time.
        /// </summary>
        public static ProcessRunRequest<TContext> Register<TContext>(
            this TContext context, IProcess<TContext> process)
            where TContext : IExecutionContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(process);
            var request = new ProcessRunRequest<TContext>(process);
            request.Register(context);
            return request;
        }

        /// <summary>
        /// Registers a fixed selection of live instruction references and their current
        /// upstream dependencies without executing. The selection does not snapshot instruction state.
        /// Use a fresh context to run only this subset. The request does not modify its process.
        /// </summary>
        public static ProcessRunRequest<TContext> Register<TContext>(
            this TContext context, ProcessRunRequest<TContext> request)
            where TContext : IExecutionContext
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(request);
            request.Register(context);
            return request;
        }
    }
}
