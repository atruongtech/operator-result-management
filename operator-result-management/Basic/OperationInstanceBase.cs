namespace operator_result_management.Basic
{
    /// <summary>
    /// Executes once per instance and publishes a result only after successful execution.
    /// </summary>
    public abstract class OperationInstanceBase<T> : IOperationInstance<T>
    {
        private readonly TaskCompletionSource<IOperationResult<T>> _taskCompletionSource;
        private readonly Lazy<Task> _execution;

        public OperationInstanceBase()
        {
            _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _execution = new(ExecuteOnceAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// Starts execution once. Every caller receives the same task, including after
        /// failure or cancellation; subsequent calls do not retry execution.
        /// </summary>
        public Task Execute() => _execution.Value;

        /// <summary>
        /// Observes the result without starting execution. Faults or cancels when execution
        /// faults or cancels. An operation that is never executed leaves its promise pending.
        /// </summary>
        public Task<IOperationResult<T>> Promise() => _taskCompletionSource.Task;

        /// <summary>
        /// Produces the result. Called once on first execution, never from the constructor.
        /// Returning null is an execution failure.
        /// </summary>
        protected abstract Task<IOperationResult<T>> ExecuteCoreAsync();

        private async Task ExecuteOnceAsync()
        {
            try
            {
                var result = await ExecuteCoreAsync().ConfigureAwait(false);
                if (result is null)
                    throw new InvalidOperationException("Operation execution returned a null result.");

                _taskCompletionSource.SetResult(result);
            }
            catch (OperationCanceledException exception)
            {
                _taskCompletionSource.SetCanceled(exception.CancellationToken);
                throw;
            }
            catch (Exception exception)
            {
                _taskCompletionSource.SetException(exception);
                throw;
            }
        }
    }
}