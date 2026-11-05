using configurable_number_pipeline_poc;
using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class ExecutionContextTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RegisteringRootRegistersAndExecutesItsDependencies()
    {
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider());
        var input = new DirectInputInstruction(36);
        var factor = new FactorInstruction(input);
        var operation = factor.GetTypedOperation(context);

        Assert.Equal(2, context.Operations.Count);
        Assert.False(operation.Promise().IsCompleted);
        var execution = context.ExecuteAsync();
        await execution.WaitAsync(_timeout);

        Assert.Equal(new[] { 2, 2, 3, 3 },
            (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(operation, factor.GetTypedOperation(context));
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task ConcurrentRootLookupsShareEntireOperationGraph()
    {
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider());
        var factor = new FactorInstruction(new DirectInputInstruction(36));
        var start = NewCompletionSource<bool>();
        var callers = Enumerable.Range(0, 64).Select(async _ =>
        {
            await start.Task;
            return factor.GetTypedOperation(context);
        }).ToArray();
        start.SetResult(true);
        var operations = await Task.WhenAll(callers).WaitAsync(_timeout);

        Assert.All(operations, operation => Assert.Same(operations[0], operation));
        Assert.Equal(2, context.Operations.Count);
        await context.ExecuteAsync().WaitAsync(_timeout);
    }

    [Fact]
    public async Task StartsAllOperationsBeforeAwaitingDependencies()
    {
        var context = new ExecutionContextBase();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var upstream = new TestOperation(context, token => completion.Task.WaitAsync(token));
        var downstream = new TestOperation(context, async token => await upstream.Promise().WaitAsync(token));
        context.GetOrAddOperation(new TestInstruction(), () => downstream);
        context.GetOrAddOperation(new TestInstruction(), () => upstream);

        var execution = context.ExecuteAsync();

        Assert.Equal(1, downstream.InvocationCount);
        Assert.Equal(1, upstream.InvocationCount);
        Assert.False(execution.IsCompleted);
        completion.SetResult(new ImmediateResult<int>(42));
        await execution.WaitAsync(_timeout);
        Assert.Equal(42, (await downstream.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public async Task ConcurrentExecutionCallsShareAggregateTask()
    {
        var context = new ExecutionContextBase();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => completion.Task.WaitAsync(token)));
        var start = NewCompletionSource<bool>();
        var executions = new Task[64];
        var callers = Enumerable.Range(0, executions.Length).Select(async index =>
        {
            await start.Task;
            executions[index] = context.ExecuteAsync();
        }).ToArray();
        start.SetResult(true);
        await Task.WhenAll(callers).WaitAsync(_timeout);

        Assert.All(executions, execution => Assert.Same(executions[0], execution));
        Assert.Equal(1, operation.InvocationCount);
        completion.SetResult(new ImmediateResult<int>(42));
        await Task.WhenAll(executions).WaitAsync(_timeout);
        Assert.Same(executions[0], context.ExecuteAsync());
    }

    [Fact]
    public async Task ExecutionFreezesRegistrationButAllowsExistingLookup()
    {
        var context = new ExecutionContextBase();
        var instruction = new TestInstruction();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = context.GetOrAddOperation(instruction,
            () => new TestOperation(context, token => completion.Task.WaitAsync(token)));
        var execution = context.ExecuteAsync();

        Assert.Same(operation, context.GetOrAddOperation<TestOperation>(instruction,
            () => throw new Exception("Existing lookup must not invoke factory.")));
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(new TestInstruction(), () => new DirectInputOperation(context, 1)));
        completion.SetResult(new ImmediateResult<int>(42));
        await execution.WaitAsync(_timeout);
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(new TestInstruction(), () => new DirectInputOperation(context, 1)));
    }

    [Fact]
    public void OperationsReturnsImmutableSnapshot()
    {
        var context = new ExecutionContextBase();
        var snapshot = context.Operations;
        var instruction = new TestInstruction();
        var operation = context.GetOrAddOperation(instruction, () => new DirectInputOperation(context, 1));

        Assert.Empty(snapshot);
        Assert.Same(operation, context.Operations[instruction]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<IInstruction, IOperationInstance>>(context.Operations);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Single(context.Operations);
    }

    [Fact]
    public void RegistrationRejectsTypeMismatch()
    {
        var context = new ExecutionContextBase();
        var instruction = new TestInstruction();
        context.GetOrAddOperation(instruction, () => new DirectInputOperation(context, 1));

        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(instruction,
                () => new TestOperation(context,
                    _ => Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(1)))));
    }

    [Fact]
    public void FailedFactoryDoesNotPoisonRegistration()
    {
        var context = new ExecutionContextBase();
        var instruction = new TestInstruction();
        var error = new InvalidOperationException("Factory failed.");

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation<DirectInputOperation>(instruction, () => throw error)));
        Assert.Empty(context.Operations);
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation<DirectInputOperation>(instruction, () => null!));
        Assert.Empty(context.Operations);

        var operation = context.GetOrAddOperation(instruction, () => new DirectInputOperation(context, 1));
        Assert.Same(operation, context.Operations[instruction]);
    }

    [Fact]
    public void RecursiveRegistrationDetectsCycles()
    {
        var context = new ExecutionContextBase();
        var first = new TestInstruction();
        var second = new TestInstruction();

        var error = Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(first, () =>
                context.GetOrAddOperation(second, () =>
                    context.GetOrAddOperation(first, () => new DirectInputOperation(context, 1)))));

        Assert.Contains("Cyclic", error.Message);
        Assert.Empty(context.Operations);
        context.GetOrAddOperation(first, () => new DirectInputOperation(context, 1));
    }

    [Fact]
    public async Task CannotExecuteInsideRegistrationFactory()
    {
        var context = new ExecutionContextBase();
        var instruction = new TestInstruction();

        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(instruction, () =>
            {
                context.ExecuteAsync();
                return new DirectInputOperation(context, 1);
            }));

        context.GetOrAddOperation(instruction, () => new DirectInputOperation(context, 1));
        await context.ExecuteAsync().WaitAsync(_timeout);
    }

    [Fact]
    public async Task SynchronousExecuteFailureStillStartsRemainingOperations()
    {
        var context = new ExecutionContextBase();
        var error = new InvalidOperationException("Execute threw synchronously.");
        var failing = context.GetOrAddOperation(new TestInstruction(), () => new ThrowingOperation(error));
        var succeeding = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context,
                _ => Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(42))));
        var execution = context.ExecuteAsync();

        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.WaitAsync(_timeout)));
        Assert.Equal(1, succeeding.InvocationCount);
        Assert.Equal(1, failing.InvocationCount);
        Assert.Equal(42, (await succeeding.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(execution, context.ExecuteAsync());
        Assert.Equal(1, failing.InvocationCount);
    }

    [Fact]
    public async Task UpstreamFailureFaultsContextAndDependentPromise()
    {
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider());
        var completion = NewCompletionSource<IOperationResult<int>>();
        var upstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => completion.Task.WaitAsync(token)));
        var downstream = context.GetOrAddOperation(new TestInstruction(), () => new FactorOperation(context, upstream));
        var execution = context.ExecuteAsync();
        var error = new InvalidDataException("Upstream failed.");

        completion.SetException(error);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(() => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => upstream.Promise().WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => downstream.Promise().WaitAsync(_timeout)));
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task PreCanceledContextCancelsAllRegisteredPromisesWithoutPrompting()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider(), cancellation.Token);
        var inputInstruction = new DirectInputInstruction(36);
        var multiplyInstruction = new UserInputMultiplyInstruction(inputInstruction);
        var factorInstruction = new FactorInstruction(multiplyInstruction);
        var factor = factorInstruction.GetTypedOperation(context);
        var input = inputInstruction.GetTypedOperation(context);
        var multiply = multiplyInstruction.GetTypedOperation(context);
        cancellation.Cancel();

        var execution = context.ExecuteAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(execution.IsCanceled);
        await AssertCanceledPromiseAsync(input, cancellation.Token);
        await AssertCanceledPromiseAsync(multiply, cancellation.Token);
        await AssertCanceledPromiseAsync(factor, cancellation.Token);
        Assert.All(context.Operations.Values, operation => Assert.True(operation.Execute().IsCanceled));
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task ContextCancellationInterruptsRunningWorkAndDependencyWaits()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider(), cancellation.Token);
        var completion = NewCompletionSource<IOperationResult<int>>();
        CancellationToken receivedToken = default;
        var upstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token =>
            {
                receivedToken = token;
                return completion.Task.WaitAsync(token);
            }));
        var downstream = context.GetOrAddOperation(new TestInstruction(), () => new FactorOperation(context, upstream));
        var execution = context.ExecuteAsync();
        Assert.Equal(cancellation.Token, receivedToken);
        Assert.False(execution.IsCompleted);

        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(execution.IsCanceled);
        await AssertCanceledPromiseAsync(upstream, cancellation.Token);
        await AssertCanceledPromiseAsync(downstream, cancellation.Token);
        Assert.Equal(1, upstream.InvocationCount);
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task CancellationBeforePublicationDiscardsNonCooperativeResult()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new ExecutionContextBase(cancellation.Token);
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, _ => completion.Task));
        var execution = context.ExecuteAsync();

        cancellation.Cancel();
        completion.SetResult(new ImmediateResult<int>(42));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(_timeout));
        await AssertCanceledPromiseAsync(operation, cancellation.Token);
    }

    [Fact]
    public async Task CancelingObserverWaitDoesNotCancelRun()
    {
        var context = new ExecutionContextBase();
        using var observerCancellation = new CancellationTokenSource();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var operation = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => completion.Task.WaitAsync(token)));
        var execution = context.ExecuteAsync();
        var observer = operation.Promise().WaitAsync(observerCancellation.Token);
        observerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.WaitAsync(_timeout));
        Assert.False(execution.IsCompleted);
        Assert.False(operation.Promise().IsCompleted);
        completion.SetResult(new ImmediateResult<int>(42));
        await execution.WaitAsync(_timeout);
        Assert.Equal(42, (await operation.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyContextCompletesAndFreezesRegistration(bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        var context = new ExecutionContextBase(cancellation.Token);
        if (canceled)
            cancellation.Cancel();

        var execution = context.ExecuteAsync();
        if (canceled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(_timeout));
            Assert.True(execution.IsCanceled);
        }
        else
        {
            await execution.WaitAsync(_timeout);
            cancellation.Cancel();
            Assert.True(execution.IsCompletedSuccessfully);
        }

        Assert.Same(execution, context.ExecuteAsync());
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(new TestInstruction(), () => new DirectInputOperation(context, 1)));
    }

    [Fact]
    public async Task SameOperationRegisteredUnderTwoInstructionsExecutesOnce()
    {
        var context = new ExecutionContextBase();
        var operation = new CountingOperation();
        context.GetOrAddOperation(new TestInstruction(), () => operation);
        context.GetOrAddOperation(new TestInstruction(), () => operation);

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(1, operation.InvocationCount);
    }

    private static async Task AssertCanceledPromiseAsync<T>(
        IOperationInstance<T> operation, CancellationToken token)
    {
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.Promise().WaitAsync(_timeout));
        Assert.Equal(token, error.CancellationToken);
        Assert.True(operation.Promise().IsCanceled);
        Assert.True(operation.Execute().IsCanceled);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestInstruction : IInstruction
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class TestOperation(
        IExecutionContext context, Func<CancellationToken, Task<IOperationResult<int>>> core)
        : OperationInstanceBase<int>(context)
    {
        private int _invocationCount;
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        protected override Task<IOperationResult<int>> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return core(cancellationToken);
        }
    }

    private sealed class ThrowingOperation(Exception error) : IOperationInstance
    {
        public int InvocationCount { get; private set; }

        public Task Execute()
        {
            InvocationCount++;
            throw error;
        }
    }

    private sealed class CountingOperation : IOperationInstance
    {
        public int InvocationCount { get; private set; }

        public Task Execute()
        {
            InvocationCount++;
            return Task.CompletedTask;
        }
    }
}
