using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class ProcessTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AddingRootPreservesExplicitMembershipAndDependencyLinks()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = process.Add(new Step(40));
        var left = process.Add(new Step(1, input));
        var right = process.Add(new Step(2, input));
        var root = new Step(0, left, right);
        Step added = process.Add(root);

        Assert.Same(root, added);
        Assert.Equal(new InstructionBase[] { input, left, right, root }, process.Instructions);
        Assert.Same(input, Assert.Single(left.Dependencies));
        Assert.Same(input, Assert.Single(right.Dependencies));
        Assert.Equal(new InstructionBase[] { left, right }, root.Dependencies);
        var instructions = process.Instructions;
        Assert.Same(root, process.Add(root));
        Assert.Same(instructions, process.Instructions);
    }

    [Fact]
    public async Task AddingInstructionsInlinePreservesConcreteTypesAndSharedExecution()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        Step input = process.Add(new Step(40));
        Step left = process.Add(new Step(1, input));
        Step right = process.Add(new Step(2, input));
        var context = new ExecutionContextBase();
        context.Register(process);

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(new InstructionBase[] { input, left, right }, process.Instructions);
        Assert.Equal(41, (await context.Register(left).Promise()).GetResult());
        Assert.Equal(42, (await context.Register(right).Promise()).GetResult());
        Assert.All(context.Operations.Values, operation =>
            Assert.Equal(1, Assert.IsType<TestOperation>(operation).InvocationCount));
    }

    [Fact]
    public void AddingAnExistingDependencyReturnsTheSameTypedInstruction()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        InstructionBase<int, ExecutionContextBase> input = new Step(40);
        process.Add(input);
        process.Add(new Step(2, input));
        var instructions = process.Instructions;

        InstructionBase<int, ExecutionContextBase> added = process.Add(input);

        Assert.Same(input, added);
        Assert.Same(instructions, process.Instructions);
        Assert.Equal(2, process.Instructions.Count);
    }

    [Fact]
    public async Task AddingCompletionOnlyInstructionsReturnsTheSameInstruction()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var instruction = new CompletionStep();

        CompletionStep added = process.Add(instruction);
        Assert.Same(instruction, added);
        var instructions = process.Instructions;
        Assert.Same(instruction, process.Add(instruction));
        Assert.Same(instructions, process.Instructions);
        var context = new ExecutionContextBase();
        context.Register(process);
        var operation = context.Register(added);

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.True(operation.Completion.IsCompletedSuccessfully);
        Assert.Single(context.Operations);
    }

    [Fact]
    public void InstructionCollectionsAreReadOnlyMembershipSnapshots()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        var output = new Step(2, input);
        process.Add(input);
        process.Add(output);
        IReadOnlyList<InstructionBase<ExecutionContextBase>> instructions = process.Instructions;
        ProcessBase untypedProcess = process;

        Assert.Same(instructions, untypedProcess.Instructions);
        Assert.Same(input, instructions[0]);
        Assert.Same(output, instructions[1]);
        if (instructions is IList<InstructionBase<ExecutionContextBase>> mutableInstructions)
            Assert.Throws<NotSupportedException>(() => mutableInstructions.Clear());
        var added = new Step(3);
        process.Add(added);

        Assert.Equal(new InstructionBase[] { input, output }, instructions);
        Assert.Equal(new InstructionBase[] { input, output, added }, process.Instructions);
        Assert.Same(process.Instructions, untypedProcess.Instructions);
    }

    [Fact]
    public async Task AddingTypedDependentsRunsEveryStepAndSharesUpstreamExecution()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var left = process.Add(new Step(1, input));
        var right = process.Add(new Step(2, input));

        Assert.Equal(new InstructionBase[] { input, left, right }, process.Instructions);
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
        var sink = process.Add(new Step(2, input));
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
        var dependent = process.Add(new Step(2, input));
        var instructions = process.Instructions;
        var fullRequest = new ProcessRunRequest<ExecutionContextBase>(process);
        InstructionBase<ExecutionContextBase>[] selection = [input];
        var request = new ProcessRunRequest<ExecutionContextBase>(process, selection);
        selection[0] = dependent;
        var laterStep = process.Add(new Step(1, dependent));

        Assert.Equal(2, instructions.Count);
        Assert.Equal(2, fullRequest.SelectedInstructions.Count);
        Assert.Same(input, fullRequest.SelectedInstructions[0]);
        Assert.Same(dependent, fullRequest.SelectedInstructions[1]);
        Assert.Equal(3, process.Instructions.Count);
        Assert.Same(input, Assert.Single(request.SelectedInstructions));
        if (request.SelectedInstructions is IList<InstructionBase<ExecutionContextBase>> mutableSelection)
            Assert.Throws<NotSupportedException>(() => mutableSelection.Clear());
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
    public void InvalidRunSelectionsAreRejectedWithoutChangingDefinition()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var instructions = process.Instructions;

        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [input, null!]));
        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [input, new Step(40)]));

        Assert.Same(instructions, process.Instructions);
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

        var dependent = process.Add(new Step(2, input));
        Assert.Same(input, Assert.Single(captured.SelectedInstructions));
        Assert.Single(context.Operations);
        Assert.False(context.Operations.ContainsKey(dependent));
        Assert.Equal(2, process.Instructions.Count);
        completion.SetResult(new ImmediateResult<int>(40));
        await execution.WaitAsync(_timeout);
        Assert.Equal(40, (await context.Register(input).Promise().WaitAsync(_timeout)).GetResult());

        var nextContext = new ExecutionContextBase();
        var nextRequest = nextContext.Register(process);
        Assert.Equal(2, nextRequest.SelectedInstructions.Count);
        Assert.NotSame(context.Operations[input], nextContext.Operations[input]);
        await nextContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(42, (await nextContext.Register(dependent).Promise().WaitAsync(_timeout)).GetResult());

        var replayContext = new ExecutionContextBase();
        captured.Register(replayContext);
        await replayContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Single(replayContext.Operations);
        Assert.False(replayContext.Operations.ContainsKey(dependent));
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
        ProcessBase definition = process;
        var empty = process.Instructions;
        var input = new Step(40);

        process.Add(input);
        var first = process.Instructions;
        process.Add(input);
        Assert.Same(first, process.Instructions);
        process.Add(new Step(2, input));
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
        process.Add(input);
        Assert.Same(left, process.Add(left));
        Assert.Same(right, process.Add(right));
        Assert.Equal(3, process.Instructions.Count);
        var unknown = new EqualStep(2, input) { Id = id };
        Assert.Throws<ArgumentException>("selectedInstructions", () =>
            new ProcessRunRequest<ExecutionContextBase>(process, [unknown]));
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
        first.DeclaredDependencies = Array.AsReadOnly<InstructionBase>([selfCycle ? first : second]);
        second.DeclaredDependencies = Array.AsReadOnly<InstructionBase>([first]);
        Assert.Throws<InvalidOperationException>(() => process.Add(first));

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
            DeclaredDependencies = invalid switch
            {
                "null-list" => null!,
                "null-entry" => Array.AsReadOnly<InstructionBase>([new Step(1), null!]),
                "wrong-context" => Array.AsReadOnly<InstructionBase>([new Step(1), new SpecializedInstruction()]),
                _ => throw new ArgumentOutOfRangeException(nameof(invalid))
            }
        };
        Assert.Throws<InvalidOperationException>(() => process.Add(instruction));
        Assert.Same(before, process.Instructions);
    }

    [Fact]
    public async Task EmptyProcessRegistersNoWorkThroughDefaultEntryPoints()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var context = new ExecutionContextBase();
        var request = new ProcessRunRequest<ExecutionContextBase>(process);
        Assert.Empty(request.SelectedInstructions);
        context.Register(request);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Empty(context.Operations);

        var fullContext = new ExecutionContextBase();
        fullContext.Register(process);
        await fullContext.ExecuteAsync().WaitAsync(_timeout);
        Assert.Empty(fullContext.Operations);
    }

    [Fact]
    public void ProcessMethodsRejectNullArguments()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        process.Add(input);
        var instructions = process.Instructions;
        InstructionBase<int, ExecutionContextBase> nullInstruction = null!;
        ExecutionContextBase nullContext = null!;
        ProcessBase<ExecutionContextBase> nullProcess = null!;
        ProcessRunRequest<ExecutionContextBase> nullRequest = null!;
        var request = new ProcessRunRequest<ExecutionContextBase>(process);

        Assert.Throws<ArgumentNullException>("instruction", () => process.Add(nullInstruction));
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
    public void MissingDependenciesAreRejectedWithoutAddingAnyInstructions()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = new Step(40);
        var intermediate = new Step(1, input);
        var output = new Step(1, intermediate);
        var empty = process.Instructions;

        Assert.Throws<InvalidOperationException>(() => process.Add(output));
        Assert.Same(empty, process.Instructions);
        Assert.Empty(process.Instructions);

        process.Add(input);
        var before = process.Instructions;
        Assert.Throws<InvalidOperationException>(() => process.Add(output));
        Assert.Same(before, process.Instructions);
        Assert.Same(input, Assert.Single(process.Instructions));

        process.Add(intermediate);
        Assert.Same(output, process.Add(output));
        Assert.Equal(new InstructionBase[] { input, intermediate, output }, process.Instructions);
        Assert.Empty(empty);
        Assert.Same(input, Assert.Single(before));
    }

    [Fact]
    public void MixedKnownAndMissingDependenciesFailAtomically()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var known = process.Add(new Step(40));
        var missing = new Step(1);
        var output = new Step(0, known, missing, known);
        var before = process.Instructions;

        Assert.Throws<InvalidOperationException>(() => process.Add(output));

        Assert.Same(before, process.Instructions);
        Assert.Same(known, Assert.Single(process.Instructions));
        process.Add(missing);
        Assert.Same(output, process.Add(output));
        Assert.Equal(new InstructionBase[] { known, missing, output }, process.Instructions);
    }

    [Fact]
    public void EqualInstructionsAndDuplicateIdsDoNotSatisfyDependencyMembership()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var id = Guid.NewGuid();
        var member = process.Add(new EqualStep(40) { Id = id });
        var missing = new EqualStep(40) { Id = id };
        var output = new EqualStep(2, missing);
        var before = process.Instructions;

        Assert.Throws<InvalidOperationException>(() => process.Add(output));

        Assert.Same(before, process.Instructions);
        Assert.Same(member, Assert.Single(process.Instructions));
        process.Add(missing);
        Assert.Same(output, process.Add(output));
        Assert.Equal(3, process.Instructions.Count);
    }

    [Fact]
    public void ExecutionContextOrOtherProcessMembershipDoesNotSatisfyDependencyMembership()
    {
        var other = new ProcessBase<ExecutionContextBase>();
        var input = other.Add(new Step(40));
        var context = new ExecutionContextBase();
        context.Register(input);
        var process = new ProcessBase<ExecutionContextBase>();
        var before = process.Instructions;
        var output = new Step(2, input);

        Assert.Throws<InvalidOperationException>(() => process.Add(output));

        Assert.Same(before, process.Instructions);
        Assert.Empty(process.Instructions);
        Assert.Single(context.Operations);
        process.Add(input);
        Assert.Same(output, process.Add(output));
    }

    [Fact]
    public void ReAddingMemberValidatesMissingTransitiveDependenciesAfterEdits()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var upstream = process.Add(new MalformedInstruction());
        var root = process.Add(new MalformedInstruction { DeclaredDependencies = [upstream] });
        var missing = new MalformedInstruction();
        upstream.DeclaredDependencies = [missing];
        var before = process.Instructions;

        Assert.Throws<InvalidOperationException>(() => process.Add(root));

        Assert.Same(before, process.Instructions);
        Assert.Equal(new InstructionBase[] { upstream, root }, process.Instructions);
        process.Add(missing);
        var after = process.Instructions;
        Assert.Same(root, process.Add(root));
        Assert.Same(after, process.Instructions);
        Assert.Equal(new InstructionBase[] { upstream, root, missing }, process.Instructions);
    }

    [Fact]
    public void ReAddingMemberRejectsCyclesAfterDependencyEdits()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var upstream = process.Add(new MalformedInstruction());
        var root = process.Add(new MalformedInstruction { DeclaredDependencies = [upstream] });
        upstream.DeclaredDependencies = [root];
        var before = process.Instructions;

        Assert.Throws<InvalidOperationException>(() => process.Add(root));

        Assert.Same(before, process.Instructions);
    }

    [Fact]
    public async Task ConcurrentAdditionsKeepAllRootsAndOneSharedDependency()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var input = process.Add(new Step(40));
        var outputs = Enumerable.Range(0, 32).Select(value => new Step(value, input)).ToArray();

        var added = await Task.WhenAll(outputs.Select(output => Task.Run(() => process.Add(output)))).WaitAsync(_timeout);

        for (int i = 0; i < outputs.Length; i++)
            Assert.Same(outputs[i], added[i]);
        Assert.Equal(33, process.Instructions.Count);
        Assert.Single(process.Instructions, instruction => ReferenceEquals(instruction, input));
        Assert.All(outputs, output => Assert.Contains(process.Instructions,
            instruction => ReferenceEquals(instruction, output)));
    }

    [Fact]
    public async Task ConcurrentFailedAdditionsDoNotAddMissingDependenciesOrRoots()
    {
        var process = new ProcessBase<ExecutionContextBase>();
        var member = process.Add(new Step(0));
        var before = process.Instructions;
        var missing = new Step(40);
        var outputs = Enumerable.Range(0, 32).Select(value => new Step(value, missing)).ToArray();

        await Task.WhenAll(outputs.Select(output => Task.Run(() =>
            Assert.Throws<InvalidOperationException>(() => process.Add(output))))).WaitAsync(_timeout);

        Assert.Same(before, process.Instructions);
        Assert.Same(member, Assert.Single(process.Instructions));
    }

    private sealed class CompletionStep : InstructionBase<ExecutionContextBase>
    {
        protected override OperationInstanceBase CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            new CompletionOperation(context);

        private sealed class CompletionOperation(ExecutionContextBase context) : OperationInstanceBase(context)
        {
            protected override Task ExecuteCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }

    private class Step : InstructionBase<int, ExecutionContextBase>
    {
        private readonly int _value;
        private readonly IReadOnlyList<InstructionBase<int, ExecutionContextBase>> _dependencies;

        public Step(int value, params InstructionBase<int, ExecutionContextBase>[] dependencies) : base(dependencies)
        {
            _value = value;
            _dependencies = Array.AsReadOnly(dependencies.ToArray());
        }

        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
        {
            var operations = _dependencies.Select(dependency => inputs.GetOperation(dependency)).ToArray();
            return new TestOperation(context, async token =>
            {
                var results = await Task.WhenAll(operations.Select(operation => operation.Promise().WaitAsync(token)));
                return new ImmediateResult<int>(_value + results.Sum(result => result.GetResult()));
            });
        }
    }

    private sealed class EqualStep(int value, params InstructionBase<int, ExecutionContextBase>[] dependencies)
        : Step(value, dependencies)
    {
        public override bool Equals(object? obj) => obj is EqualStep;

        public override int GetHashCode() => 0;
    }

    private sealed class PendingInstruction(Task<IOperationResult<int>> completion)
        : InstructionBase<int, ExecutionContextBase>
    {
        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            new TestOperation(context, token => completion.WaitAsync(token));
    }

    private sealed class RegistrationCallbackInstruction(Action onRegistration)
        : InstructionBase<int, ExecutionContextBase>
    {
        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
        {
            onRegistration();
            return new TestOperation(context,
                _ => Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(42)));
        }
    }

    private sealed class SpecializedContext : ExecutionContextBase
    {
    }

    private sealed class SpecializedInstruction : InstructionBase<SpecializedContext>
    {
        protected override OperationInstanceBase CreateOperation(
            SpecializedContext context, InstructionInputs<SpecializedContext> inputs) =>
            throw new InvalidOperationException("Incompatible instruction must not register.");
    }

    private sealed class MalformedInstruction : InstructionBase<ExecutionContextBase>
    {
        public IReadOnlyList<InstructionBase> DeclaredDependencies { get; set; } = Array.Empty<InstructionBase>();
        public override IReadOnlyList<InstructionBase> Dependencies => DeclaredDependencies;

        protected override OperationInstanceBase CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
            => throw new InvalidOperationException("Malformed instruction must not execute.");
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
}
