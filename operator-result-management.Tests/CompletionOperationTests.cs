using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class CompletionOperationTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task CompletionOnlyObservesAndConcurrentExecutionRunsOnce()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new CompletionOperation(new ExecutionContextBase(), _ => release.Task);
        IOperationInstance untyped = operation;
        var completion = untyped.Completion;
        Assert.Same(completion, untyped.Completion);
        Assert.False(completion.IsCompleted);
        Assert.Equal(0, operation.InvocationCount);
        var executions = new Task[20];

        await Task.WhenAll(Enumerable.Range(0, executions.Length).Select(index => Task.Run(() =>
        {
            executions[index] = operation.Execute();
        }))).WaitAsync(_timeout);

        Assert.Equal(1, operation.InvocationCount);
        Assert.All(executions, execution => Assert.Same(completion, execution));
        Assert.False(completion.IsCompleted);
        release.SetResult();
        await Task.WhenAll(executions).WaitAsync(_timeout);
        await completion.WaitAsync(_timeout);
        Assert.Same(completion, operation.Completion);
        Assert.Same(completion, operation.Execute());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureFaultsExecutionAndCompletionWithoutRetry(bool synchronous)
    {
        var error = new InvalidOperationException("Failed.");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new CompletionOperation(new ExecutionContextBase(),
            _ => synchronous ? throw error : release.Task);
        var completion = operation.Completion;

        var execution = operation.Execute();
        Assert.Same(completion, execution);
        if (!synchronous)
        {
            Assert.False(completion.IsCompleted);
            release.SetException(error);
        }

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => operation.Completion.WaitAsync(_timeout)));
        Assert.Same(execution, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationBeforeWorkOrBeforePublicationCancelsBothTasks(bool preCanceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (preCanceled)
            cancellation.Cancel();
        var operation = new CompletionOperation(new ExecutionContextBase(cancellation.Token), _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        var completion = operation.Completion;
        var execution = operation.Execute();

        Assert.Same(completion, execution);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(_timeout));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.Completion.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(operation.Completion.IsCanceled);
        Assert.Same(completion, operation.Execute());
        Assert.Equal(preCanceled ? 0 : 1, operation.InvocationCount);
    }

    [Fact]
    public async Task SynchronousSuccessReturnsPreobservedCompletion()
    {
        var operation = new CompletionOperation(new ExecutionContextBase(), _ => Task.CompletedTask);
        var completion = operation.Completion;

        Assert.False(completion.IsCompleted);
        Assert.Same(completion, operation.Execute());
        await completion.WaitAsync(_timeout);
        Assert.True(completion.IsCompletedSuccessfully);
        Assert.Same(completion, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CoreCancellationPreservesTokenAndSharedCompletion(bool synchronous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new CompletionOperation(new ExecutionContextBase(),
            _ => synchronous ? throw new OperationCanceledException(cancellation.Token) : release.Task);
        var completion = operation.Completion;

        var execution = operation.Execute();
        if (!synchronous)
            release.SetCanceled(cancellation.Token);

        Assert.Same(completion, execution);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => completion.WaitAsync(_timeout));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(completion.IsCanceled);
        Assert.Same(completion, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task CancelingCompletionWaitDoesNotCancelExecution()
    {
        using var observerCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new CompletionOperation(new ExecutionContextBase(), _ => release.Task);
        var observer = operation.Completion.WaitAsync(observerCancellation.Token);

        Assert.Equal(0, operation.InvocationCount);
        var execution = operation.Execute();
        observerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.WaitAsync(_timeout));
        Assert.False(execution.IsCompleted);

        release.SetResult();
        await execution.WaitAsync(_timeout);
        Assert.True(operation.Completion.IsCompletedSuccessfully);
    }

    private sealed class CompletionOperation(IExecutionContext context, Func<CancellationToken, Task> work)
        : OperationInstanceBase(context)
    {
        public int InvocationCount { get; private set; }

        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return work(cancellationToken);
        }
    }
}
