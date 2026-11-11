using System.Reflection;
using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class InstructionBaseTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ConstructorSuppliesIdentityAndCopiesDependencyCollection()
    {
        var input = new ProbeInstruction(40);
        InstructionBase[] dependencies = [input];
        var instruction = new ProbeInstruction(2, dependencies);
        dependencies[0] = new ProbeInstruction(100);

        Assert.NotEqual(Guid.Empty, instruction.Id);
        Assert.NotEqual(input.Id, instruction.Id);
        Assert.Same(input, Assert.Single(instruction.Dependencies));
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<InstructionBase>>(instruction.Dependencies).Clear());
        Assert.Throws<ArgumentNullException>(() => new ProbeInstruction(0, null!));
        Assert.Throws<ArgumentNullException>(() => new ProbeInstruction(0, [null!]));
    }

    [Fact]
    public async Task TypedAndUntypedEntryPointsShareOneCreationWithoutStartingWork()
    {
        var instruction = new ProbeInstruction(42);
        var context = new ExecutionContextBase();
        InstructionBase<ExecutionContextBase> untyped = instruction;

        var operation = instruction.GetTypedOperation(context);

        Assert.Same(operation, instruction.GetOperation(context));
        Assert.Same(operation, context.Register(instruction));
        Assert.Same(operation, context.Register(untyped));
        Assert.Equal(1, instruction.CreationCount);
        Assert.False(operation.Completion.IsCompleted);
        Assert.Equal(0, Assert.IsType<ValueOperation>(operation).InvocationCount);

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(42, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Same(operation, instruction.GetTypedOperation(context));
        Assert.Equal(1, instruction.CreationCount);
        Assert.Equal(1, Assert.IsType<ValueOperation>(operation).InvocationCount);
        var next = instruction.GetTypedOperation(new ExecutionContextBase());
        Assert.NotSame(operation, next);
        Assert.Equal(2, instruction.CreationCount);
    }

    [Fact]
    public async Task BaseRegistersEveryDeclaredInputBeforeCallingCreationHook()
    {
        var value = new ProbeInstruction(42);
        var completion = new CompletionInstruction();
        var instruction = new ProbeInstruction(0, value, completion)
        {
            Factory = (context, inputs) =>
            {
                Assert.Equal(2, context.Operations.Count);
                Assert.Same(context.Operations[value], inputs.GetOperation(value));
                Assert.Same(context.Operations[completion], inputs.GetOperation(completion));
                return new ValueOperation(context, 7);
            }
        };
        var context = new ExecutionContextBase();
        var operation = context.Register(instruction);

        Assert.Equal(3, context.Operations.Count);
        Assert.False(completion.GetOperation(context).Completion.IsCompleted);
        await context.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(7, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.True(completion.GetOperation(context).Completion.IsCompletedSuccessfully);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundInputsRejectUndeclaredDependenciesEvenIfAlreadyRegistered(bool preRegistered)
    {
        var declared = new ProbeInstruction(40);
        var undeclared = new ProbeInstruction(2);
        var context = new ExecutionContextBase();
        if (preRegistered)
            context.Register(undeclared);
        var instruction = new ProbeInstruction(0, declared)
        {
            Factory = (_, inputs) => inputs.GetOperation(undeclared)
        };

        var error = Assert.Throws<InvalidOperationException>(() => context.Register(instruction));

        Assert.Contains("direct instruction dependency", error.Message);
        Assert.False(context.Operations.ContainsKey(instruction));
        Assert.Equal(preRegistered, context.Operations.ContainsKey(undeclared));
        Assert.Equal(preRegistered ? 1 : 0, undeclared.CreationCount);
        instruction.Factory = (_, inputs) => inputs.GetOperation(declared);
        Assert.Same(declared.GetTypedOperation(context), context.Register(instruction));
    }

    [Fact]
    public void BoundInputsUseReferenceIdentityAndResolveDuplicateInputsOnce()
    {
        var id = Guid.NewGuid();
        var first = new EqualInstruction(1) { Id = id };
        var second = new EqualInstruction(2) { Id = id };
        var unknown = new EqualInstruction(3) { Id = id };
        var instruction = new ProbeInstruction(0, first, second, first)
        {
            Factory = (context, inputs) =>
            {
                Assert.NotSame(inputs.GetOperation(first), inputs.GetOperation(second));
                Assert.Throws<InvalidOperationException>(() => inputs.GetOperation(unknown));
                return new ValueOperation(context, 0);
            }
        };
        var context = new ExecutionContextBase();

        context.Register(instruction);

        Assert.Equal(3, context.Operations.Count);
        Assert.Equal(1, first.CreationCount);
        Assert.Equal(1, second.CreationCount);
        Assert.Equal(0, unknown.CreationCount);
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("null-entry")]
    [InlineData("wrong-context")]
    public void RegistrationRejectsInvalidDependenciesBeforeCreationHook(string kind)
    {
        var instruction = new EditableInstruction
        {
            DeclaredDependencies = kind switch
            {
                "null-list" => null!,
                "null-entry" => [null!],
                "wrong-context" => [new OtherContextInstruction()],
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            }
        };
        var context = new ExecutionContextBase();

        Assert.Throws<InvalidOperationException>(() => context.Register(instruction));

        Assert.Equal(0, instruction.CreationCount);
        Assert.Empty(context.Operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationRejectsDeclaredCyclesAndAllowsRetry(bool selfCycle)
    {
        var first = new EditableInstruction();
        var second = new EditableInstruction();
        first.DeclaredDependencies = [selfCycle ? first : second];
        second.DeclaredDependencies = [first];
        var context = new ExecutionContextBase();

        var error = Assert.Throws<InvalidOperationException>(() => context.Register(first));

        Assert.Contains("Cyclic", error.Message);
        Assert.Empty(context.Operations);
        Assert.Equal(0, first.CreationCount);
        Assert.Equal(0, second.CreationCount);
        first.DeclaredDependencies = [];
        context.Register(first);
        Assert.Single(context.Operations);
    }

    [Fact]
    public async Task ExistingRegistrationDoesNotReadEditedDependenciesOrConfiguration()
    {
        var original = new ProbeInstruction(40);
        var replacement = new ProbeInstruction(100);
        var instruction = new EditableInstruction { DeclaredDependencies = [original], Value = 2 };
        var first = new ExecutionContextBase();
        var operation = first.Register(instruction);
        instruction.DeclaredDependencies = null!;
        instruction.Value = 3;

        Assert.Same(operation, first.Register(instruction));
        await first.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(2, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        instruction.DeclaredDependencies = [replacement];
        var next = new ExecutionContextBase();
        var nextOperation = next.Register(instruction);
        await next.ExecuteAsync().WaitAsync(_timeout);
        Assert.Equal(3, (await nextOperation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.True(first.Operations.ContainsKey(original));
        Assert.False(first.Operations.ContainsKey(replacement));
        Assert.False(next.Operations.ContainsKey(original));
        Assert.True(next.Operations.ContainsKey(replacement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrNullCreationDoesNotPoisonRegistration(bool returnsNull)
    {
        var error = new InvalidOperationException("Creation failed.");
        var instruction = new ProbeInstruction(42)
        {
            Factory = (_, _) => returnsNull ? null! : throw error
        };
        var context = new ExecutionContextBase();

        var thrown = Assert.Throws<InvalidOperationException>(() => context.Register(instruction));
        if (returnsNull)
            Assert.Contains("returned null", thrown.Message);
        else
            Assert.Same(error, thrown);
        Assert.Empty(context.Operations);
        instruction.Factory = null;
        var operation = context.Register(instruction);
        Assert.Same(operation, context.Register(instruction));
        Assert.Equal(2, instruction.CreationCount);
    }

    [Fact]
    public void NullContextsAndInputLookupsAreRejected()
    {
        var instruction = new ProbeInstruction(42)
        {
            Factory = (context, inputs) =>
            {
                Assert.Throws<ArgumentNullException>("instruction", () =>
                    inputs.GetOperation((InstructionBase<ExecutionContextBase>)null!));
                Assert.Throws<ArgumentNullException>("instruction", () =>
                    inputs.GetOperation((InstructionBase<int, ExecutionContextBase>)null!));
                return new ValueOperation(context, 42);
            }
        };

        Assert.Throws<ArgumentNullException>("context", () => instruction.GetOperation(null!));
        Assert.Throws<ArgumentNullException>("context", () => instruction.GetTypedOperation(null!));
        new ExecutionContextBase().Register(instruction);
    }

    [Fact]
    public void PublicApiRemovesInstructionContractsAndKeepsRegistrationLibraryOwned()
    {
        var assembly = typeof(InstructionBase).Assembly;
        Assert.DoesNotContain(assembly.GetExportedTypes(),
            type => type.IsInterface && type.Name.Split('`')[0] == "IInstruction");
        Assert.False(typeof(InstructionBase<ExecutionContextBase>)
            .GetMethod(nameof(InstructionBase<ExecutionContextBase>.GetOperation))!.IsVirtual);
        Assert.False(typeof(InstructionBase<int, ExecutionContextBase>)
            .GetMethod(nameof(InstructionBase<int, ExecutionContextBase>.GetTypedOperation))!.IsVirtual);
        Assert.Null(typeof(ExecutionContextBase).GetMethod("GetOrAddOperation", BindingFlags.Public | BindingFlags.Instance));
    }

    private class ProbeInstruction(int value, params InstructionBase[] dependencies)
        : InstructionBase<int, ExecutionContextBase>(dependencies)
    {
        public int CreationCount { get; private set; }
        public Func<ExecutionContextBase, InstructionInputs<ExecutionContextBase>, OperationInstanceBase<int>>? Factory { get; set; }

        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
        {
            CreationCount++;
            return Factory is null ? new ValueOperation(context, value) : Factory(context, inputs);
        }
    }

    private sealed class EqualInstruction(int value) : ProbeInstruction(value)
    {
        public override bool Equals(object? obj) => obj is EqualInstruction;
        public override int GetHashCode() => 0;
    }

    private sealed class EditableInstruction : InstructionBase<int, ExecutionContextBase>
    {
        public IReadOnlyList<InstructionBase> DeclaredDependencies { get; set; } = [];
        public override IReadOnlyList<InstructionBase> Dependencies => DeclaredDependencies;
        public int Value { get; set; }
        public int CreationCount { get; private set; }

        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs)
        {
            CreationCount++;
            return new ValueOperation(context, Value);
        }
    }

    private sealed class CompletionInstruction : InstructionBase<ExecutionContextBase>
    {
        protected override OperationInstanceBase CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            new CompletionOperation(context);
    }

    private sealed class CompletionOperation(ExecutionContextBase context) : OperationInstanceBase(context)
    {
        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OtherContext : ExecutionContextBase;

    private sealed class OtherContextInstruction : InstructionBase<OtherContext>
    {
        protected override OperationInstanceBase CreateOperation(
            OtherContext context, InstructionInputs<OtherContext> inputs) =>
            throw new InvalidOperationException("Incompatible instruction must not register.");
    }

    private sealed class ValueOperation(ExecutionContextBase context, int value) : OperationInstanceBase<int>(context)
    {
        public int InvocationCount { get; private set; }

        protected override Task<IOperationResult<int>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(value));
        }
    }
}
