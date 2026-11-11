namespace operator_result_management.Basic
{
    /// <summary>
    /// Executes once per instance and publishes a result only after successful execution.
    /// </summary>
    public abstract class OperationInstanceBase<TResult> : OperationInstanceBase
    {
        private readonly ResultCompletionState _completion;

        protected OperationInstanceBase(ExecutionContextBase context)
            : this(context, new ResultCompletionState())
        {
        }

        private OperationInstanceBase(ExecutionContextBase context, ResultCompletionState completion)
            : base(context, completion)
        {
            _completion = completion;
        }

        /// <summary>
        /// Observes the result without starting execution. Faults or cancels when execution
        /// faults or cancels. Shares the exact task used by Completion and Execute().
        /// An operation that is never executed leaves its promise pending.
        /// All consumers share the published result; prefer immutable values or coordinate access.
        /// </summary>
        public Task<IOperationResult<TResult>> Promise() => _completion.Promise;

        /// <summary>
        /// Produces the result. Called once on first execution, never from the constructor.
        /// Returning a null task or result wrapper is an execution failure; a wrapper may
        /// contain a null value. Observe cancellation in asynchronous waits and long-running work.
        /// Capture configuration and upstream operations before execution. Do not await this
        /// operation's own completion or promise.
        /// </summary>
        protected abstract Task<IOperationResult<TResult>> ExecuteResultAsync(CancellationToken cancellationToken);

        protected sealed override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            var execution = ExecuteResultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Operation execution returned a null task.");
            _completion.Result = await execution.ConfigureAwait(false);
        }

        private sealed class ResultCompletionState : OperationCompletionState
        {
            private readonly TaskCompletionSource<IOperationResult<TResult>> _source =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public IOperationResult<TResult>? Result { private get; set; }
            public Task<IOperationResult<TResult>> Promise => _source.Task;
            public override Task Task => Promise;

            public override void SetResult()
            {
                var result = Result
                    ?? throw new InvalidOperationException("Operation execution returned a null result.");
                Result = null;
                _source.SetResult(result);
            }

            public override void SetCanceled(CancellationToken token)
            {
                Result = null;
                _source.SetCanceled(token);
            }

            public override void SetException(Exception exception)
            {
                Result = null;
                _source.SetException(exception);
            }
        }
    }
}