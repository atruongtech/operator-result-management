using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class ExecutionContextTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RegisteringRootRegistersAndExecutesItsDependencies()
    {
        var context = new ExecutionContextBase();
        var input = new GraphInstruction(36);
        var output = new GraphInstruction(0, input);
        var operation = context.Register(output);

        Assert.Equal(2, context.Operations.Count);
        Assert.False(operation.Promise().IsCompleted);
        var execution = context.ExecuteAsync();
        await execution.WaitAsync(_timeout);

        Assert.Equal(36, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(operation, context.Register(output));
        Assert.All(context.Operations.Values, registered =>
            Assert.Equal(1, Assert.IsType<TestOperation>(registered).InvocationCount));
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task ConcurrentRootLookupsShareEntireOperationGraph()
    {
        var context = new ExecutionContextBase();
        var output = new GraphInstruction(0, new GraphInstruction(36));
        var start = NewCompletionSource<bool>();
        var callers = Enumerable.Range(0, 64).Select(async _ =>
        {
            await start.Task;
            return context.Register(output);
        }).ToArray();
        start.SetResult(true);
        var operations = await Task.WhenAll(callers).WaitAsync(_timeout);

        Assert.All(operations, operation => Assert.Same(operations[0], operation));
        Assert.Equal(2, context.Operations.Count);
        await context.ExecuteAsync().WaitAsync(_timeout);
    }

    [Fact]
    public async Task RegisteringDiamondGraphSharesUpstreamWorkAcrossRoots()
    {
        var context = new ExecutionContextBase();
        var input = new GraphInstruction(40);
        var left = new GraphInstruction(1, input);
        var right = new GraphInstruction(2, input);
        var output = new GraphInstruction(0, left, right);

        var leftOperation = context.Register(left);
        var rightOperation = context.Register(right);
        var outputOperation = context.Register(output);
        var inputOperation = context.Register(input);
        var operations = context.Operations;

        Assert.Equal(4, operations.Count);
        Assert.Same(inputOperation, operations[input]);
        Assert.Same(leftOperation, operations[left]);
        Assert.Same(rightOperation, operations[right]);
        Assert.Same(outputOperation, operations[output]);
        Assert.All(operations.Values, operation =>
        {
            Assert.Equal(0, Assert.IsType<TestOperation>(operation).InvocationCount);
        });
        Assert.False(outputOperation.Promise().IsCompleted);
        Assert.Same(inputOperation, context.Register(input));

        var execution = context.ExecuteAsync();
        await execution.WaitAsync(_timeout);

        Assert.Equal(41, (await leftOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await rightOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(83, (await outputOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(execution, context.ExecuteAsync());
        Assert.All(operations.Values, operation =>
        {
            Assert.Equal(1, Assert.IsType<TestOperation>(operation).InvocationCount);
        });
    }

    [Fact]
    public async Task SeparateInstancesWithSameConfigurationAndIdRemainDistinct()
    {
        var context = new ExecutionContextBase();
        var id = Guid.NewGuid();
        var first = new GraphInstruction(42) { Id = id };
        var second = new GraphInstruction(42) { Id = id };

        var firstOperation = context.Register(first);
        var secondOperation = context.Register(second);

        Assert.NotSame(firstOperation, secondOperation);
        Assert.Equal(2, context.Operations.Count);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(42, (await firstOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await secondOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(1, Assert.IsType<TestOperation>(firstOperation).InvocationCount);
        Assert.Equal(1, Assert.IsType<TestOperation>(secondOperation).InvocationCount);
    }

    [Fact]
    public async Task OverriddenEqualityDoesNotMergeInstructionsOrCreateFalseCycles()
    {
        var context = new ExecutionContextBase();
        var input = new EqualInstruction(40);
        var left = new EqualInstruction(1, input);
        var right = new EqualInstruction(2, input);
        Assert.True(left.Equals(input));
        Assert.True(left.Equals(right));

        var leftOperation = context.Register(left);
        var rightOperation = context.Register(right);
        var inputOperation = context.Register(input);
        var snapshot = context.Operations;

        Assert.Equal(3, snapshot.Count);
        Assert.Same(inputOperation, snapshot[input]);
        Assert.Same(leftOperation, snapshot[left]);
        Assert.Same(rightOperation, snapshot[right]);
        Assert.False(snapshot.ContainsKey(new EqualInstruction(40)));
        Assert.Same(leftOperation, context.Register(left));
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(41, (await leftOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await rightOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(1, Assert.IsType<TestOperation>(inputOperation).InvocationCount);
    }

    [Fact]
    public async Task NewContextCreatesIndependentOperationsForSameInstructions()
    {
        var firstContext = new ExecutionContextBase();
        var secondContext = new ExecutionContextBase();
        var input = new GraphInstruction(40);
        var output = new GraphInstruction(2, input);
        var firstOperation = firstContext.Register(output);
        var secondOperation = secondContext.Register(output);

        Assert.NotSame(firstOperation, secondOperation);
        Assert.NotSame(firstContext.Operations[input], secondContext.Operations[input]);
        await firstContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.False(secondOperation.Promise().IsCompleted);
        Assert.All(secondContext.Operations.Values, operation =>
        {
            Assert.Equal(0, Assert.IsType<TestOperation>(operation).InvocationCount);
        });
        await secondContext.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(42, (await firstOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await secondOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.All(firstContext.Operations.Values.Concat(secondContext.Operations.Values), operation =>
        {
            Assert.Equal(1, Assert.IsType<TestOperation>(operation).InvocationCount);
        });
    }

    [Fact]
    public async Task UntypedRegistrationSharesTypedOperation()
    {
        var context = new ExecutionContextBase();
        var instruction = new GraphInstruction(42);
        InstructionBase<ExecutionContextBase> untypedInstruction = instruction;

        var operation = context.Register(untypedInstruction);

        Assert.Same(operation, context.Register(instruction));
        Assert.Single(context.Operations);
        Assert.Equal(0, Assert.IsType<TestOperation>(operation).InvocationCount);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(1, Assert.IsType<TestOperation>(operation).InvocationCount);
    }

    [Fact]
    public async Task RegisterRejectsNewInstructionsAfterExecutionButAllowsExistingOnes()
    {
        var context = new ExecutionContextBase();
        var instruction = new GraphInstruction(42);
        var operation = context.Register(instruction);
        InstructionBase<ExecutionContextBase> untypedInstruction = instruction;
        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Same(operation, context.Register(instruction));
        Assert.Same(operation, context.Register(untypedInstruction));
        Assert.Throws<InvalidOperationException>(() => context.Register(new GraphInstruction(42)));
        InstructionBase<ExecutionContextBase> newUntypedInstruction = new GraphInstruction(42);
        Assert.Throws<InvalidOperationException>(() => context.Register(newUntypedInstruction));
        Assert.Single(context.Operations);
    }

    [Fact]
    public void RegisterRejectsNullContextAndInstruction()
    {
        var context = new ExecutionContextBase();
        var instruction = new GraphInstruction(42);
        InstructionBase<ExecutionContextBase> untypedInstruction = instruction;
        ExecutionContextBase nullContext = null!;
        InstructionBase<int, ExecutionContextBase> nullInstruction = null!;
        InstructionBase<ExecutionContextBase> nullUntypedInstruction = null!;

        Assert.Throws<ArgumentNullException>("context", () => nullContext.Register(instruction));
        Assert.Throws<ArgumentNullException>("context", () => nullContext.Register(untypedInstruction));
        Assert.Throws<ArgumentNullException>("instruction", () => context.Register(nullInstruction));
        Assert.Throws<ArgumentNullException>("instruction", () => context.Register(nullUntypedInstruction));
        Assert.Empty(context.Operations);
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
            context.GetOrAddOperation(new TestInstruction(), () => new CountingOperation()));
        completion.SetResult(new ImmediateResult<int>(42));
        await execution.WaitAsync(_timeout);
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation(new TestInstruction(), () => new CountingOperation()));
    }

    [Fact]
    public void OperationsReturnsImmutableSnapshot()
    {
        var context = new ExecutionContextBase();
        var snapshot = context.Operations;
        var instruction = new TestInstruction();
        var operation = context.GetOrAddOperation(instruction, () => new CountingOperation());

        Assert.Empty(snapshot);
        Assert.Same(operation, context.Operations[instruction]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<InstructionBase, OperationInstanceBase>>(context.Operations);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        Assert.Single(context.Operations);
    }

    [Fact]
    public void RegistrationRejectsTypeMismatch()
    {
        var context = new ExecutionContextBase();
        var instruction = new TestInstruction();
        context.GetOrAddOperation(instruction, () => new CountingOperation());

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
            context.GetOrAddOperation<CountingOperation>(instruction, () => throw error)));
        Assert.Empty(context.Operations);
        Assert.Throws<InvalidOperationException>(() =>
            context.GetOrAddOperation<CountingOperation>(instruction, () => null!));
        Assert.Empty(context.Operations);

        var operation = context.GetOrAddOperation(instruction, () => new CountingOperation());
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
                    context.GetOrAddOperation(first, () => new CountingOperation()))));

        Assert.Contains("Cyclic", error.Message);
        Assert.Empty(context.Operations);
        context.GetOrAddOperation(first, () => new CountingOperation());
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
                return new CountingOperation();
            }));

        context.GetOrAddOperation(instruction, () => new CountingOperation());
        await context.ExecuteAsync().WaitAsync(_timeout);
    }

    [Fact]
    public async Task SynchronousWorkFailureStillStartsRemainingOperations()
    {
        var context = new ExecutionContextBase();
        var error = new InvalidOperationException("Work threw synchronously.");
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
        var context = new ExecutionContextBase();
        var completion = NewCompletionSource<IOperationResult<int>>();
        var upstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => completion.Task.WaitAsync(token)));
        var downstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => upstream.Promise().WaitAsync(token)));
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
    public async Task PreCanceledContextCancelsAllRegisteredPromisesWithoutExecutingCore()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new ExecutionContextBase(cancellation.Token);
        var inputInstruction = new GraphInstruction(36);
        var middleInstruction = new GraphInstruction(1, inputInstruction);
        var outputInstruction = new GraphInstruction(2, middleInstruction);
        var output = context.Register(outputInstruction);
        var input = context.Register(inputInstruction);
        var middle = context.Register(middleInstruction);
        cancellation.Cancel();

        var execution = context.ExecuteAsync();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(execution.IsCanceled);
        await AssertCanceledPromiseAsync(input, cancellation.Token);
        await AssertCanceledPromiseAsync(middle, cancellation.Token);
        await AssertCanceledPromiseAsync(output, cancellation.Token);
        Assert.All(context.Operations.Values, operation => Assert.True(operation.Execute().IsCanceled));
        Assert.All(context.Operations.Values, operation =>
            Assert.Equal(0, Assert.IsType<TestOperation>(operation).InvocationCount));
        Assert.Same(execution, context.ExecuteAsync());
    }

    [Fact]
    public async Task ContextCancellationInterruptsRunningWorkAndDependencyWaits()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new ExecutionContextBase(cancellation.Token);
        var completion = NewCompletionSource<IOperationResult<int>>();
        CancellationToken receivedToken = default;
        var upstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token =>
            {
                receivedToken = token;
                return completion.Task.WaitAsync(token);
            }));
        var downstream = context.GetOrAddOperation(new TestInstruction(),
            () => new TestOperation(context, token => upstream.Promise().WaitAsync(token)));
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
            context.GetOrAddOperation(new TestInstruction(), () => new CountingOperation()));
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
        OperationInstanceBase<T> operation, CancellationToken token)
    {
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.Promise().WaitAsync(_timeout));
        Assert.Equal(token, error.CancellationToken);
        Assert.True(operation.Promise().IsCanceled);
        Assert.Same(operation.Completion, operation.Execute());
        Assert.True(operation.Execute().IsCanceled);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestInstruction : InstructionBase;

    private class GraphInstruction(int value, params GraphInstruction[] dependencies)
        : InstructionBase<int, ExecutionContextBase>(dependencies)
    {
        private readonly IReadOnlyList<GraphInstruction> _dependencies =
            Array.AsReadOnly(dependencies.ToArray());

        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
        {
            var operations = _dependencies.Select(dependency => inputs.GetOperation(dependency)).ToArray();
            return new TestOperation(context, async token =>
            {
                var results = await Task.WhenAll(operations.Select(operation => operation.Promise().WaitAsync(token)));
                return new ImmediateResult<int>(value + results.Sum(result => result.GetResult()));
            });
        }
    }

    private sealed class EqualInstruction(int value, params GraphInstruction[] dependencies)
        : GraphInstruction(value, dependencies)
    {
        public override bool Equals(object? obj) => obj is EqualInstruction;

        public override int GetHashCode() => 0;
    }

    private sealed class TestOperation(
        ExecutionContextBase context, Func<CancellationToken, Task<IOperationResult<int>>> core)
        : OperationInstanceBase<int>(context)
    {
        private int _invocationCount;
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        protected override Task<IOperationResult<int>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            return core(cancellationToken);
        }
    }

    private sealed class ThrowingOperation(Exception error) : OperationInstanceBase(new ExecutionContextBase())
    {
        public int InvocationCount { get; private set; }

        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw error;
        }
    }

    private sealed class CountingOperation() : OperationInstanceBase(new ExecutionContextBase())
    {
        public int InvocationCount { get; private set; }

        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.CompletedTask;
        }
    }
}
