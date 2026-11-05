namespace operator_result_management.Basic
{
    public abstract class OperationInstanceBase<T> : IOperationInstance<T>
    {
        protected TaskCompletionSource<IOperationResult<T>> _taskCompletionSource;

        public OperationInstanceBase()
        {
            _taskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public abstract Task Execute();

        public Task<IOperationResult<T>> Promise() => _taskCompletionSource.Task;

        protected bool Fulfill(IOperationResult<T> result)
        {
            return _taskCompletionSource.TrySetResult(result);
        }
    }
}