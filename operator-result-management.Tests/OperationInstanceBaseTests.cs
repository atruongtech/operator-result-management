using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class OperationInstanceBaseTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PromiseDoesNotStartExecutionAndPublishesOnlyCompletedResult()
    {
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = new TestOperation(() => completion.Task);
        var promise = operation.Promise();
        IOperationInstance untyped = operation;
        var observedCompletion = untyped.Completion;

        Assert.Same(promise, operation.Promise());
        Assert.Same(promise, observedCompletion);
        Assert.Same(observedCompletion, untyped.Completion);
        Assert.False(observedCompletion.IsCompleted);
        Assert.False(promise.IsCompleted);
        Assert.Equal(0, operation.InvocationCount);

        var execution = operation.Execute();
        Assert.Same(observedCompletion, execution);
        Assert.False(execution.IsCompleted);
        Assert.False(promise.IsCompleted);
        Assert.Same(promise, operation.Promise());
        Assert.Equal(1, operation.InvocationCount);

        var result = new ImmediateResult<int>(42);
        completion.SetResult(result);

        await execution.WaitAsync(_timeout);
        Assert.Same(result, await promise.WaitAsync(_timeout));
        Assert.Same(execution, operation.Execute());
        Assert.Same(observedCompletion, untyped.Completion);
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task ConcurrentExecuteCallsShareTaskAndInvokeCoreOnce()
    {
        var start = NewCompletionSource<bool>();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = new TestOperation(() => completion.Task);
        var returnedTasks = new Task[64];
        var callers = Enumerable.Range(0, returnedTasks.Length).Select(async index =>
        {
            await start.Task;
            returnedTasks[index] = operation.Execute();
        }).ToArray();

        start.SetResult(true);
        await Task.WhenAll(callers).WaitAsync(_timeout);

        Assert.Equal(1, operation.InvocationCount);
        Assert.All(returnedTasks, task => Assert.Same(operation.Completion, task));
        Assert.False(operation.Promise().IsCompleted);

        completion.SetResult(new ImmediateResult<int>(42));
        await Task.WhenAll(returnedTasks).WaitAsync(_timeout);
        Assert.Equal(42, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(returnedTasks[0], operation.Execute());
    }

    [Fact]
    public async Task SynchronousFailureFaultsBothTasksAndDoesNotRetry()
    {
        var error = new InvalidOperationException("Synchronous failure.");
        var operation = new TestOperation(() => throw error);
        var promise = operation.Promise();
        var observedCompletion = operation.Completion;
        Task? execution = null;

        Assert.Null(Record.Exception(() => { execution = operation.Execute(); }));
        Assert.NotNull(execution);
        Assert.Same(observedCompletion, execution);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => promise.WaitAsync(_timeout)));
        Assert.True(execution.IsFaulted);
        Assert.True(promise.IsFaulted);
        Assert.Same(execution, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task AsynchronousFailureFaultsBothTasksAndDoesNotRetry()
    {
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = new TestOperation(() => completion.Task);
        var promise = operation.Promise();
        var observedCompletion = operation.Completion;
        var execution = operation.Execute();
        Assert.Same(observedCompletion, execution);
        var error = new InvalidDataException("Asynchronous failure.");

        completion.SetException(error);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => promise.WaitAsync(_timeout)));
        Assert.True(execution.IsFaulted);
        Assert.True(promise.IsFaulted);
        Assert.Same(execution, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task FailureAfterProducingLocalResultDoesNotPublishResult()
    {
        var completion = NewCompletionSource<bool>();
        var localResult = new ImmediateResult<int>(42);
        var error = new InvalidOperationException("Failure after computing result.");
        var operation = new TestOperation(async () =>
        {
            await completion.Task;
            if (localResult.GetResult() == 42)
                throw error;
            return localResult;
        });
        var execution = operation.Execute();

        Assert.False(operation.Promise().IsCompleted);
        completion.SetResult(true);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => operation.Promise().WaitAsync(_timeout)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationCancelsBothTasksAndPreservesToken(bool synchronous)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = new TestOperation(() => synchronous
            ? throw new OperationCanceledException(cancellation.Token)
            : completion.Task);
        var promise = operation.Promise();
        var observedCompletion = operation.Completion;
        var execution = operation.Execute();
        Assert.Same(observedCompletion, execution);

        if (!synchronous)
            completion.SetCanceled(cancellation.Token);

        var executionError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));
        var promiseError = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => promise.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, executionError.CancellationToken);
        Assert.Equal(cancellation.Token, promiseError.CancellationToken);
        Assert.True(execution.IsCanceled);
        Assert.True(promise.IsCanceled);
        Assert.Same(execution, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task NullResultFaultsBothTasks()
    {
        var operation = new TestOperation(() => Task.FromResult<IOperationResult<int>>(null!));
        var observedCompletion = operation.Completion;
        var execution = operation.Execute();
        Assert.Same(observedCompletion, execution);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.WaitAsync(_timeout));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => operation.Promise().WaitAsync(_timeout)));
        Assert.Same(execution, operation.Execute());
        Assert.Equal(1, operation.InvocationCount);
    }

    [Fact]
    public async Task DependencyFailureCompletesWhenAllInsteadOfHanging()
    {
        var completion = NewCompletionSource<IOperationResult<int>>();
        var upstream = new TestOperation(() => completion.Task);
        var downstream = new TestOperation(async () => await upstream.Promise());
        var downstreamExecution = downstream.Execute();
        var upstreamExecution = upstream.Execute();
        var aggregate = Task.WhenAll(upstreamExecution, downstreamExecution);
        var error = new InvalidDataException("Upstream failed.");

        completion.SetException(error);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => aggregate.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => downstream.Promise().WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => upstream.Promise().WaitAsync(_timeout)));
        Assert.True(upstreamExecution.IsFaulted);
        Assert.True(downstreamExecution.IsFaulted);
    }

    [Fact]
    public async Task DependentOperationCanStartBeforeUpstreamExecution()
    {
        var result = new ImmediateResult<int>(36);
        var upstream = new TestOperation(() => Task.FromResult<IOperationResult<int>>(result));
        var downstream = new TestOperation(() => upstream.Promise());
        var execution = downstream.Execute();

        Assert.False(execution.IsCompleted);
        Assert.Equal(0, upstream.InvocationCount);
        await upstream.Execute().WaitAsync(_timeout);
        await execution.WaitAsync(_timeout);

        Assert.Same(result, await downstream.Promise().WaitAsync(_timeout));
        Assert.Same(execution, downstream.Execute());
        Assert.Equal(1, upstream.InvocationCount);
        Assert.Equal(1, downstream.InvocationCount);
    }

    [Fact]
    public async Task UntypedDependencyCanObserveTypedCompletionWithoutStartingUpstream()
    {
        var upstream = new TestOperation(() =>
            Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(42)));
        IOperationInstance dependency = upstream;
        var downstream = new TestOperation(async () =>
        {
            await dependency.Completion;
            return new ImmediateResult<int>(7);
        });

        var execution = downstream.Execute();

        Assert.False(execution.IsCompleted);
        Assert.Equal(0, upstream.InvocationCount);
        Assert.Same(dependency.Completion, dependency.Execute());
        await execution.WaitAsync(_timeout);
        Assert.Equal(7, (await downstream.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await upstream.Promise().WaitAsync(_timeout)).GetResult());
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestOperation(Func<Task<IOperationResult<int>>> core)
        : OperationInstanceBase<int>(new ExecutionContextBase())
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        protected override Task<IOperationResult<int>> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return core();
        }
    }
}
