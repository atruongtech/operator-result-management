using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class OperationLifecycleTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReentrantExecutionObservesSameTaskWithoutStartingWorkAgain(bool typed)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocationCount = 0;
        OperationInstanceBase? operation = null;
        operation = CreateOperation(typed, new ExecutionContextBase(), _ =>
        {
            invocationCount++;
            Assert.Same(operation!.Completion, operation.Execute());
            Assert.False(operation.Completion.IsCompleted);
            return release.Task;
        });
        var completion = operation.Completion;

        var execution = operation.Execute();

        Assert.Same(completion, execution);
        Assert.False(execution.IsCompleted);
        Assert.Equal(1, invocationCount);
        release.SetResult();
        await completion.WaitAsync(_timeout);
        Assert.Same(completion, operation.Execute());
        Assert.Equal(1, invocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullWorkTaskFaultsSharedCompletionWithoutSynchronousThrowOrRetry(bool typed)
    {
        var context = new ExecutionContextBase();
        var invocationCount = 0;
        OperationInstanceBase operation = typed
            ? new ResultWork<int>(context, _ =>
            {
                invocationCount++;
                return null!;
            })
            : new CompletionWork(context, _ =>
            {
                invocationCount++;
                return null!;
            });
        var completion = operation.Completion;
        Task? execution = null;

        Assert.Null(Record.Exception(() => { execution = operation.Execute(); }));

        Assert.Same(completion, execution);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => completion.WaitAsync(_timeout));
        if (operation is ResultWork<int> resultOperation)
        {
            Assert.Same(completion, resultOperation.Promise());
            Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
                () => resultOperation.Promise().WaitAsync(_timeout)));
        }
        Assert.Same(completion, operation.Execute());
        Assert.Equal(1, invocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkStartsOnActivatingThreadAndNotDuringConstructionOrObservation(bool typed)
    {
        var activatingThread = Environment.CurrentManagedThreadId;
        int? workThread = null;
        var operation = CreateOperation(typed, new ExecutionContextBase(), _ =>
        {
            workThread = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        });

        var completion = operation.Completion;
        Assert.Null(workThread);
        Assert.False(completion.IsCompleted);
        if (operation is ResultWork<int> typedOperation)
            Assert.Same(completion, typedOperation.Promise());
        Assert.Null(workThread);

        Assert.Same(completion, operation.Execute());
        Assert.Equal(activatingThread, workThread);
        await completion.WaitAsync(_timeout);
        Assert.True(completion.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreCanceledContextSkipsWorkAndCancelsSharedCompletion(bool typed)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invocationCount = 0;
        var operation = CreateOperation(typed, new ExecutionContextBase(cancellation.Token), _ =>
        {
            invocationCount++;
            return Task.CompletedTask;
        });
        var completion = operation.Completion;

        Assert.False(completion.IsCompleted);
        Assert.Same(completion, operation.Execute());
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => completion.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(completion.IsCanceled);
        Assert.Equal(0, invocationCount);
        Assert.Same(completion, operation.Execute());
        if (operation is ResultWork<int> typedOperation)
            Assert.Same(completion, typedOperation.Promise());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationBeforePublicationRejectsNonCooperativeSuccess(bool typed)
    {
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken receivedToken = default;
        var operation = CreateOperation(typed, new ExecutionContextBase(cancellation.Token), token =>
        {
            receivedToken = token;
            return release.Task;
        });
        var execution = operation.Execute();

        cancellation.Cancel();
        Assert.False(execution.IsCompleted);
        release.SetResult();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, receivedToken);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(operation.Completion.IsCanceled);
        Assert.Same(execution, operation.Execute());
        if (operation is ResultWork<int> typedOperation)
            Assert.Same(execution, typedOperation.Promise());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAfterPublicationDoesNotChangeSuccessfulOutcome(bool typed)
    {
        using var cancellation = new CancellationTokenSource();
        var operation = CreateOperation(typed, new ExecutionContextBase(cancellation.Token),
            _ => Task.CompletedTask);
        var execution = operation.Execute();
        await execution.WaitAsync(_timeout);

        cancellation.Cancel();

        Assert.True(execution.IsCompletedSuccessfully);
        Assert.Same(execution, operation.Execute());
        if (operation is ResultWork<int> typedOperation)
            Assert.Equal(42, (await typedOperation.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public async Task NullWrappedValueIsSuccessfulResult()
    {
        var result = new ImmediateResult<string?>(null);
        var operation = new ResultWork<string?>(new ExecutionContextBase(),
            _ => Task.FromResult<IOperationResult<string?>>(result));
        OperationInstanceBase untyped = operation;

        Assert.Same(operation.Promise(), untyped.Execute());
        await untyped.Completion.WaitAsync(_timeout);

        Assert.Same(result, await operation.Promise().WaitAsync(_timeout));
        Assert.Null((await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.True(untyped.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public void ConstructorsRejectNullContext()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CompletionWork(null!, _ => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() =>
            new ResultWork<int>(null!,
                _ => Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(42))));
    }

    private static OperationInstanceBase CreateOperation(
        bool typed, ExecutionContextBase context, Func<CancellationToken, Task> work)
        => typed
            ? new ResultWork<int>(context, async token =>
            {
                await work(token);
                return new ImmediateResult<int>(42);
            })
            : new CompletionWork(context, work);

    private sealed class CompletionWork(ExecutionContextBase context, Func<CancellationToken, Task> work)
        : OperationInstanceBase(context)
    {
        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken) => work(cancellationToken);
    }

    private sealed class ResultWork<TResult>(
        ExecutionContextBase context, Func<CancellationToken, Task<IOperationResult<TResult>>> work)
        : OperationInstanceBase<TResult>(context)
    {
        protected override Task<IOperationResult<TResult>> ExecuteResultAsync(CancellationToken cancellationToken)
            => work(cancellationToken);
    }

}
