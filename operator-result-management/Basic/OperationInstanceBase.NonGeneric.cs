namespace operator_result_management.Basic;

/// <summary>
/// Executes once and publishes completion without a result value.
/// </summary>
public abstract class OperationInstanceBase : IOperationInstance
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lazy<Task> _execution;
    private readonly CancellationToken _cancellationToken;

    protected OperationInstanceBase(IExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _cancellationToken = context.CancellationToken;
        _execution = new(() =>
        {
            _ = ExecuteOnceAsync();
            return Completion;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Observes completion without starting execution. Remains pending until executed.
    /// </summary>
    public Task Completion => _completion.Task;

    /// <summary>
    /// Starts execution once. Returns Completion on every call and never retries.
    /// </summary>
    public Task Execute() => _execution.Value;

    /// <summary>
    /// Performs the work. Observe cancellation during waits and long-running operations.
    /// </summary>
    protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);

    private async Task ExecuteOnceAsync()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            await ExecuteCoreAsync(_cancellationToken).ConfigureAwait(false);
            _cancellationToken.ThrowIfCancellationRequested();
            _completion.SetResult();
        }
        catch (OperationCanceledException exception)
        {
            _completion.SetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _completion.SetException(exception);
        }
    }
}
