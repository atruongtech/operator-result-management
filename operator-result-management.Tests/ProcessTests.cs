using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class ProcessTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AddingRootDiscoversDistinctStepsAndDependencyLinks()
    {
        IProcess<ExecutionContextBase> process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        var left = new Step(1, input);
        var right = new Step(2, input);
        var root = new Step(0, left, right);
        process.Add(root);

        Assert.Equal(new IInstruction[] { input, left, right, root }, process.Instructions);
        Assert.Same(input, Assert.Single(left.Dependencies));
        Assert.Same(input, Assert.Single(right.Dependencies));
        Assert.Equal(new IInstruction[] { left, right }, root.Dependencies);
        var instructions = process.Instructions;
        process.Add(root);
        Assert.Same(instructions, process.Instructions);
    }

    [Fact]
    public void InstructionCollectionsAreImmutableAndDependenciesAreReadOnly()
    {
        IProcess<ExecutionContextBase> process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        IInstruction<int, ExecutionContextBase>[] dependencies = [input];
        var output = new Step(2, dependencies);
        dependencies[0] = new Step(100);
        process.Add(output);
        ImmutableList<IInstruction<ExecutionContextBase>> instructions = process.Instructions;
        IProcess untypedProcess = process;

        Assert.Same(instructions, untypedProcess.Instructions);
        Assert.Same(input, instructions[0]);
        Assert.Same(output, instructions[1]);
        Assert.Same(input, Assert.Single(output.Dependencies));
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<IInstruction<int, ExecutionContextBase>>>(output.Dependencies).Clear());
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<IInstruction<ExecutionContextBase>>>(instructions).Clear());
        var added = new Step(3);
        var separateList = instructions.Add(added);
        Assert.Equal(3, separateList.Count);
        Assert.Same(added, separateList[2]);
        Assert.Same(instructions, process.Instructions);
        process.Add(added);

        Assert.Equal(2, instructions.Count);
        Assert.Equal(3, process.Instructions.Count);
        Assert.Same(process.Instructions, untypedProcess.Instructions);
    }

    [Fact]
    public async Task TypedAppendingRunsEveryStepAndSharesUpstreamExecution()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var left = process.Append(input, source => new Step(1, source));
        var right = process.Append(input, source => new Step(2, source));

        Assert.Equal(new IInstruction[] { input, left, right }, process.Instructions);
        var context = new ExecutionContextBase();
        var request = context.Register(process);
        Assert.Equal(3, context.Operations.Count);
        Assert.Equal(3, request.SelectedInstructions.Count);
        Assert.All(context.Operations.Values, operation =>
        {
            Assert.Equal(0, Assert.IsType<TestOperation>(operation).InvocationCount);
        });

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(40, (await context.Register(input).Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(41, (await context.Register(left).Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(42, (await context.Register(right).Promise().WaitAsync(_timeout)).GetResult());
        Assert.All(context.Operations.Values, operation =>
        {
            Assert.Equal(1, Assert.IsType<TestOperation>(operation).InvocationCount);
        });
    }

    [Fact]
    public async Task RunSelectionCanTargetNonSinkWithoutChangingProcess()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var sink = process.Append(input, source => new Step(2, source));
        var unrelated = new Step(100);
        process.Add(unrelated);
        var instructions = process.Instructions;
        var request = new ProcessRunRequest<ExecutionContextBase>(process, [input]);
        var context = new ExecutionContextBase();

        Assert.Same(request, context.Register(request));
        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(3, instructions.Count);
        Assert.Same(input, Assert.Single(request.SelectedInstructions));
        Assert.Single(context.Operations);
        Assert.False(context.Operations.ContainsKey(sink));
        Assert.False(context.Operations.ContainsKey(unrelated));
        Assert.Equal(40, (await context.Register(input).Promise().WaitAsync(_timeout)).GetResult());

        var nextContext = new ExecutionContextBase();
        var nextRequest = new ProcessRunRequest<ExecutionContextBase>(process, [sink]);
        nextRequest.Register(nextContext);
        await nextContext.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(2, nextContext.Operations.Count);
        Assert.False(nextContext.Operations.ContainsKey(unrelated));
        Assert.Equal(42, (await nextContext.Register(sink).Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(instructions, process.Instructions);
        Assert.Same(input, Assert.Single(request.SelectedInstructions));

        var fullContext = new ExecutionContextBase();
        Assert.Equal(instructions, fullContext.Register(process).SelectedInstructions);
        await fullContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(3, fullContext.Operations.Count);
        Assert.Equal(100, (await fullContext.Register(unrelated).Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public async Task RunRequestCapturesInstructionReferencesWithoutFollowingLaterEdits()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var dependent = process.Append(input, source => new Step(2, source));
        var instructions = process.Instructions;
        var fullRequest = new ProcessRunRequest<ExecutionContextBase>(process);
        IInstruction<ExecutionContextBase>[] selection = [input];
        var request = new ProcessRunRequest<ExecutionContextBase>(process, selection);
        selection[0] = dependent;
        var laterStep = process.Append(dependent, source => new Step(1, source));

        Assert.Equal(2, instructions.Count);
        Assert.Equal(2, fullRequest.SelectedInstructions.Count);
        Assert.Same(input, fullRequest.SelectedInstructions[0]);
        Assert.Same(dependent, fullRequest.SelectedInstructions[1]);
        Assert.Equal(3, process.Instructions.Count);
        Assert.Same(input, Assert.Single(request.SelectedInstructions));
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<IInstruction<ExecutionContextBase>>>(request.SelectedInstructions).Clear());
        var laterRequest = new ProcessRunRequest<ExecutionContextBase>(process, [laterStep]);
        Assert.Same(laterStep, Assert.Single(laterRequest.SelectedInstructions));

        var context = new ExecutionContextBase();
        context.Register(request);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Single(context.Operations);
        Assert.False(context.Operations.ContainsKey(dependent));
        Assert.False(context.Operations.ContainsKey(laterStep));
        Assert.Equal(40, (await context.Register(input).Promise().WaitAsync(_timeout)).GetResult());

        var capturedFullContext = new ExecutionContextBase();
        fullRequest.Register(capturedFullContext);
        await capturedFullContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(2, capturedFullContext.Operations.Count);
        Assert.False(capturedFullContext.Operations.ContainsKey(laterStep));
    }

    [Fact]
    public async Task EmptyRunSelectionRegistersNoWorkWithoutChangingDefinition()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(new Step(42));
        var instructions = process.Instructions;
        var request = new ProcessRunRequest<ExecutionContextBase>(process, []);
        var context = new ExecutionContextBase();

        context.Register(request);
        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Empty(request.SelectedInstructions);
        Assert.Empty(context.Operations);
        Assert.Same(instructions, process.Instructions);
        Assert.Single(process.Instructions);
    }

    [Fact]
    public void InvalidRunSelectionsAreRejectedWithoutChangingDefinitionOrContext()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var instructions = process.Instructions;
        var context = new ExecutionContextBase();

        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [input, null!]));
        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [input, new Step(40)]));

        Assert.Same(instructions, process.Instructions);
        Assert.Empty(context.Operations);
    }

    [Fact]
    public async Task EditingDefinitionDuringExecutionDoesNotChangeCapturedRun()
    {
        var completion = new TaskCompletionSource<IOperationResult<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new PendingInstruction(completion.Task);
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(input);
        var context = new ExecutionContextBase();
        var captured = context.Register(process);
        var execution = context.ExecuteAsync();
        Assert.False(execution.IsCompleted);

        var appended = process.Append(input, source => new Step(2, source));
        Assert.Same(input, Assert.Single(captured.SelectedInstructions));
        Assert.Single(context.Operations);
        Assert.False(context.Operations.ContainsKey(appended));
        Assert.Equal(2, process.Instructions.Count);
        completion.SetResult(new ImmediateResult<int>(40));
        await execution.WaitAsync(_timeout);
        Assert.Equal(40, (await context.Register(input).Promise().WaitAsync(_timeout)).GetResult());

        var nextContext = new ExecutionContextBase();
        var nextRequest = nextContext.Register(process);
        Assert.Equal(2, nextRequest.SelectedInstructions.Count);
        Assert.NotSame(context.Operations[input], nextContext.Operations[input]);
        await nextContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(42, (await nextContext.Register(appended).Promise().WaitAsync(_timeout)).GetResult());

        var replayContext = new ExecutionContextBase();
        captured.Register(replayContext);
        await replayContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Single(replayContext.Operations);
        Assert.False(replayContext.Operations.ContainsKey(appended));
    }

    [Fact]
    public async Task AddingStepsDuringRegistrationDoesNotChangeCapturedInstructionSet()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var laterStep = new Step(100);
        var original = new RegistrationCallbackInstruction(() => process.Add(laterStep));
        process.Add(original);
        var context = new ExecutionContextBase();

        var request = context.Register(process);

        Assert.Same(original, Assert.Single(request.SelectedInstructions));
        Assert.Equal(2, process.Instructions.Count);
        Assert.Single(context.Operations);
        Assert.False(context.Operations.ContainsKey(laterStep));
        await context.ExecuteAsync().WaitAsync(_timeout);

        var nextContext = new ExecutionContextBase();
        var nextRequest = nextContext.Register(process);
        Assert.Equal(2, nextRequest.SelectedInstructions.Count);
        await nextContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(2, nextContext.Operations.Count);
        Assert.Equal(100, (await nextContext.Register(laterStep).Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public void ClientCanDetectEffectiveEditsByComparingInstructionCollections()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        IProcess definition = process;
        var empty = process.Instructions;
        var input = new Step(40);

        process.Add(input);
        var first = process.Instructions;
        process.Add(input);
        Assert.Same(first, process.Instructions);
        process.Append(input, source => new Step(2, source));
        var second = process.Instructions;

        Assert.NotSame(empty, first);
        Assert.NotSame(first, second);
        Assert.Empty(empty);
        Assert.Single(first);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, definition.Instructions.Count);
        Assert.Same(second, definition.Instructions);
    }

    [Fact]
    public async Task ReferenceIdentityIgnoresOverriddenEqualityAndDuplicateIds()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var id = Guid.NewGuid();
        var input = new EqualStep(40) { Id = id };
        var left = new EqualStep(1, input) { Id = id };
        var right = new EqualStep(2, input) { Id = id };
        Assert.True(input.Equals(left));
        process.Add(left);
        process.Add(right);
        Assert.Equal(3, process.Instructions.Count);
        var unknown = new EqualStep(2, input) { Id = id };
        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [unknown]));
        var factoryCalled = false;
        Assert.Throws<ArgumentException>("input", () =>
            process.Append(unknown, source =>
            {
                factoryCalled = true;
                return new Step(1, source);
            }));
        Assert.False(factoryCalled);

        var context = new ExecutionContextBase();
        var request = new ProcessRunRequest<ExecutionContextBase>(process, [right, right]);
        Assert.Same(right, Assert.Single(request.SelectedInstructions));
        var branches = new ProcessRunRequest<ExecutionContextBase>(process, [left, right, left]);
        Assert.Equal(2, branches.SelectedInstructions.Count);
        Assert.Same(left, branches.SelectedInstructions[0]);
        Assert.Same(right, branches.SelectedInstructions[1]);
        context.Register(request);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(2, context.Operations.Count);
        Assert.False(context.Operations.ContainsKey(left));
        Assert.Equal(42, (await context.Register(right).Promise().WaitAsync(_timeout)).GetResult());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclesAreRejectedAtomically(bool selfCycle)
    {
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(new Step(42));
        var before = process.Instructions;
        var first = new MalformedInstruction();
        var second = new MalformedInstruction();
        first.Dependencies = Array.AsReadOnly<IInstruction>([selfCycle ? first : second]);
        second.Dependencies = Array.AsReadOnly<IInstruction>([first]);
        var error = Assert.Throws<InvalidOperationException>(() => process.Add(first));

        Assert.Contains("Cyclic", error.Message);
        Assert.Same(before, process.Instructions);
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("null-entry")]
    [InlineData("wrong-context")]
    public void InvalidDependencyMetadataIsRejectedAtomically(string invalid)
    {
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(new Step(42));
        var before = process.Instructions;
        var instruction = new MalformedInstruction
        {
            Dependencies = invalid switch
            {
                "null-list" => null!,
                "null-entry" => Array.AsReadOnly<IInstruction>([new Step(1), null!]),
                "wrong-context" => Array.AsReadOnly<IInstruction>([new Step(1), new DirectInputInstruction(1)]),
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            }
        };
        Assert.Throws<InvalidOperationException>(() => process.Add(instruction));
        Assert.Same(before, process.Instructions);
    }

    [Fact]
    public void AppendRejectsMissingDirectDependencyAndPropagatesFactoryErrors()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var before = process.Instructions;
        var error = new InvalidOperationException("Factory failed.");

        Assert.Throws<InvalidOperationException>(() => process.Append(input, _ => new Step(2)));
        Assert.Throws<InvalidOperationException>(() =>
            process.Append(input, source => new Step(1, new Step(1, source))));
        Assert.Throws<InvalidOperationException>(() =>
            process.Append<int, int>(input, _ => null!));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            process.Append<int, int>(input, _ => throw error)));
        Assert.Same(before, process.Instructions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultRunRequestsRegisterAllStepsIncludingEmptyDefinitions(bool hasSteps)
    {
        var process = new ProcessBase<ExecutionContextBase>();
        if (hasSteps)
        {
            process.Add(new Step(42));
            process.Add(new Step(100));
        }
        var context = new ExecutionContextBase();
        var request = new ProcessRunRequest<ExecutionContextBase>(process);
        Assert.Equal(process.Instructions, request.SelectedInstructions);
        context.Register(request);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(hasSteps ? 2 : 0, context.Operations.Count);

        var fullContext = new ExecutionContextBase();
        fullContext.Register(process);
        await fullContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(hasSteps ? 2 : 0, fullContext.Operations.Count);
    }

    [Fact]
    public void ProcessMethodsRejectNullArguments()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var instructions = process.Instructions;
        IInstruction<int, ExecutionContextBase> nullInstruction = null!;
        ExecutionContextBase nullContext = null!;
        IProcess<ExecutionContextBase> nullProcess = null!;
        ProcessRunRequest<ExecutionContextBase> nullRequest = null!;
        var request = new ProcessRunRequest<ExecutionContextBase>(process);

        Assert.Throws<ArgumentNullException>("instruction", () => process.Add(nullInstruction));
        Assert.Throws<ArgumentNullException>("input", () =>
            process.Append(nullInstruction, source => new Step(2, source)));
        Assert.Throws<ArgumentNullException>("factory", () => process.Append<int, int>(input, null!));
        Assert.Throws<ArgumentNullException>("context", () => nullContext.Register(process));
        Assert.Throws<ArgumentNullException>("process", () => new ExecutionContextBase().Register(nullProcess));
        Assert.Throws<ArgumentNullException>("process", () =>
            new ProcessRunRequest<ExecutionContextBase>(null!));
        Assert.Throws<ArgumentNullException>("process", () =>
            new ProcessRunRequest<ExecutionContextBase>(null!, []));
        Assert.Throws<ArgumentNullException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, null!));
        Assert.Throws<ArgumentNullException>("context", () => request.Register(nullContext));
        Assert.Throws<ArgumentNullException>("context", () => nullContext.Register(request));
        Assert.Throws<ArgumentNullException>("request", () => new ExecutionContextBase().Register(nullRequest));
        Assert.Same(instructions, process.Instructions);
    }

    [Fact]
    public async Task SharedFailurePropagatesToAllSelectedInstructionsWithoutChangingDefinition()
    {
        var completion = new TaskCompletionSource<IOperationResult<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new PendingInstruction(completion.Task);
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(input);
        var left = process.Append(input, source => new Step(1, source));
        var right = process.Append(input, source => new Step(2, source));
        var context = new ExecutionContextBase();
        var instructions = process.Instructions;
        context.Register(new ProcessRunRequest<ExecutionContextBase>(process, [left, right]));
        var execution = context.ExecuteAsync();
        var error = new InvalidDataException("Upstream failed.");
        completion.SetException(error);

        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => execution.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Register(left).Promise().WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Register(right).Promise().WaitAsync(_timeout)));
        Assert.Same(execution, context.ExecuteAsync());
        Assert.Same(instructions, process.Instructions);
        Assert.Equal(1, Assert.IsType<TestOperation>(context.Operations[input]).InvocationCount);
    }

    [Fact]
    public async Task ContextCancellationCancelsProcessOperationsWithoutChangingDefinition()
    {
        var completion = new TaskCompletionSource<IOperationResult<int>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var input = new PendingInstruction(completion.Task);
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(input);
        var output = process.Append(input, source => new Step(2, source));
        using var cancellation = new CancellationTokenSource();
        var context = new ExecutionContextBase(cancellation.Token);
        var instructions = process.Instructions;
        context.Register(process);
        var execution = context.ExecuteAsync();
        cancellation.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution.WaitAsync(_timeout));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Register(output).Promise().WaitAsync(_timeout));
        Assert.All(context.Operations.Values, operation => Assert.True(operation.Execute().IsCanceled));
        Assert.Same(instructions, process.Instructions);
    }

    [Fact]
    public async Task ConcurrentAdditionsKeepAllRootsAndOneSharedDependency()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        var outputs = Enumerable.Range(0, 32).Select(value => new Step(value, input)).ToArray();

        await Task.WhenAll(outputs.Select(output => Task.Run(() => process.Add(output)))).WaitAsync(_timeout);

        Assert.Equal(33, process.Instructions.Count);
        Assert.Single(process.Instructions, instruction => ReferenceEquals(instruction, input));
        Assert.All(outputs, output => Assert.Contains(process.Instructions,
            instruction => ReferenceEquals(instruction, output)));
    }

    [Fact]
    public void NumberPipelineExposesEveryStepWithoutPromptingOrExecuting()
    {
        var process = new ProcessBase<NumberPipelineExecutionContext>();
        var input = new DirectInputInstruction(12);
        process.Add(input);
        var multiply = process.Append(input,
            source => new UserInputMultiplyInstruction(source));
        var factor = process.Append(multiply, source => new FactorInstruction(source));
        var context = new NumberPipelineExecutionContext(new UserInterfaceProvider());

        var request = context.Register(process);

        Assert.Equal(new IInstruction[] { input, multiply, factor }, process.Instructions);
        Assert.Equal(3, request.SelectedInstructions.Count);
        Assert.Equal(3, context.Operations.Count);
        Assert.False(context.Register(factor).Promise().IsCompleted);
        Assert.Same(input, Assert.Single(multiply.Dependencies));
        Assert.Same(multiply, Assert.Single(factor.Dependencies));
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<IInstruction>>(factor.Dependencies).Clear());
    }

    private class Step : IInstruction<int, ExecutionContextBase>
    {
        private readonly int _value;
        private readonly IReadOnlyList<IInstruction<int, ExecutionContextBase>> _dependencies;

        public Guid Id { get; init; } = Guid.NewGuid();

        public IReadOnlyList<IInstruction> Dependencies => _dependencies;

        public Step(int value, params IInstruction<int, ExecutionContextBase>[] dependencies)
        {
            _value = value;
            _dependencies = Array.AsReadOnly(dependencies.ToArray());
        }

        public IOperationInstance<int> GetTypedOperation(ExecutionContextBase context)
        {
            return context.GetOrAddOperation(this, () =>
            {
                var operations = _dependencies.Select(dependency => dependency.GetTypedOperation(context)).ToArray();
                return new TestOperation(context, async token =>
                {
                    var results = await Task.WhenAll(operations.Select(operation => operation.Promise().WaitAsync(token)));
                    return new ImmediateResult<int>(_value + results.Sum(result => result.GetResult()));
                });
            });
        }

        public IOperationInstance GetOperation(ExecutionContextBase context) => GetTypedOperation(context);
    }

    private sealed class EqualStep(int value, params IInstruction<int, ExecutionContextBase>[] dependencies)
        : Step(value, dependencies)
    {
        public override bool Equals(object? obj) => obj is EqualStep;

        public override int GetHashCode() => 0;
    }

    private sealed class PendingInstruction(Task<IOperationResult<int>> completion)
        : IInstruction<int, ExecutionContextBase>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public IReadOnlyList<IInstruction> Dependencies => Array.Empty<IInstruction>();

        public IOperationInstance<int> GetTypedOperation(ExecutionContextBase context)
            => context.GetOrAddOperation(this, () => new TestOperation(context, token => completion.WaitAsync(token)));

        public IOperationInstance GetOperation(ExecutionContextBase context) => GetTypedOperation(context);
    }

    private sealed class RegistrationCallbackInstruction(Action onRegistration)
        : IInstruction<int, ExecutionContextBase>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public IReadOnlyList<IInstruction> Dependencies => Array.Empty<IInstruction>();

        public IOperationInstance<int> GetTypedOperation(ExecutionContextBase context)
        {
            return context.GetOrAddOperation(this, () =>
            {
                onRegistration();
                return new TestOperation(context,
                    _ => Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(42)));
            });
        }

        public IOperationInstance GetOperation(ExecutionContextBase context) => GetTypedOperation(context);
    }

    private sealed class MalformedInstruction : IInstruction<ExecutionContextBase>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public IReadOnlyList<IInstruction> Dependencies { get; set; } = Array.Empty<IInstruction>();

        public IOperationInstance GetOperation(ExecutionContextBase context)
            => throw new InvalidOperationException("Malformed instruction must not execute.");
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
}
