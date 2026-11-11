namespace operator_result_management.Basic;

/// <summary>
/// Executes once and publishes completion. Derive directly for completion-only work,
/// or use OperationInstanceBase&lt;TResult&gt; to publish a typed result.
/// </summary>
public abstract class OperationInstanceBase
{
    private readonly OperationCompletionState _completion;
    private readonly CancellationToken _cancellationToken;
    private int _started;

    protected OperationInstanceBase(ExecutionContextBase context)
        : this(context, new CompletionOnlyState())
    {
    }

    private protected OperationInstanceBase(ExecutionContextBase context, OperationCompletionState completion)
    {
        ArgumentNullException.ThrowIfNull(context);
        _cancellationToken = context.CancellationToken;
        _completion = completion;
    }

    /// <summary>
    /// Observes success, failure, or cancellation without starting execution.
    /// Remains pending until executed. This is the same task returned by Execute().
    /// </summary>
    public Task Completion => _completion.Task;

    /// <summary>
    /// Starts execution once using the owning context's cancellation token.
    /// Concurrent, repeated, and reentrant calls return Completion without retrying.
    /// Work failures are published through that task, not thrown synchronously.
    /// Override the work hook rather than hiding this lifecycle method.
    /// </summary>
    public Task Execute()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _ = ExecuteOnceAsync();
        return Completion;
    }

    /// <summary>
    /// Performs completion-only work, once on activation and never during construction.
    /// Return a non-null task and observe cancellation during waits and long-running work.
    /// Do not await this operation's own completion.
    /// </summary>
    protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken);

    private async Task ExecuteOnceAsync()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var execution = ExecuteCoreAsync(_cancellationToken)
                ?? throw new InvalidOperationException("Operation execution returned a null task.");
            await execution.ConfigureAwait(false);
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

    private sealed class CompletionOnlyState : OperationCompletionState
    {
        private readonly TaskCompletionSource _source =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task Task => _source.Task;
        public override void SetResult() => _source.SetResult();
        public override void SetCanceled(CancellationToken token) => _source.SetCanceled(token);
        public override void SetException(Exception exception) => _source.SetException(exception);
    }
}

internal abstract class OperationCompletionState
{
    public abstract Task Task { get; }
    public abstract void SetResult();
    public abstract void SetCanceled(CancellationToken token);
    public abstract void SetException(Exception exception);
}
