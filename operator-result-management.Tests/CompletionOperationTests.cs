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
        OperationInstanceBase untyped = operation;
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

    private sealed class CompletionOperation(ExecutionContextBase context, Func<CancellationToken, Task> work)
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
