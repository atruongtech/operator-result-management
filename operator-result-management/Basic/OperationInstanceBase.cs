namespace operator_result_management.Basic
{
    /// <summary>
    /// Executes once per instance and publishes a result only after successful execution.
    /// </summary>
    public abstract class OperationInstanceBase<TResult> : IOperationInstance<TResult>
    {
        private readonly TaskCompletionSource<IOperationResult<TResult>> _taskCompletionSource;
        private readonly Lazy<Task> _execution;
        private readonly CancellationToken _cancellationToken;

        protected OperationInstanceBase(IExecutionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _cancellationToken = context.CancellationToken;
            _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _execution = new(() =>
            {
                _ = ExecuteOnceAsync();
                return Completion;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Observes completion without starting execution. Shares the typed result promise.
        /// </summary>
        public Task Completion => _taskCompletionSource.Task;

        /// <summary>
        /// Starts execution once. Every caller receives Completion, including after
        /// failure or cancellation; subsequent calls do not retry execution.
        /// Uses the owning context's token for cooperative cancellation.
        /// </summary>
        public Task Execute() => _execution.Value;

        /// <summary>
        /// Observes the result without starting execution. Faults or cancels when execution
        /// faults or cancels. An operation that is never executed leaves its promise pending.
        /// </summary>
        public Task<IOperationResult<TResult>> Promise() => _taskCompletionSource.Task;

        /// <summary>
        /// Produces the result. Called once on first execution, never from the constructor.
        /// Returning null is an execution failure. Observe the context's cancellation token
        /// in asynchronous waits and long-running work.
        /// </summary>
        protected abstract Task<IOperationResult<TResult>> ExecuteCoreAsync(CancellationToken cancellationToken);

        private async Task ExecuteOnceAsync()
        {
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteCoreAsync(_cancellationToken).ConfigureAwait(false);
                _cancellationToken.ThrowIfCancellationRequested();
                if (result is null)
                    throw new InvalidOperationException("Operation execution returned a null result.");

                _taskCompletionSource.SetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                _taskCompletionSource.SetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                _taskCompletionSource.SetException(exception);
            }
        }
    }
}