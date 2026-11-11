using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class InstructionFactoryTests
{
    [Fact]
    public void FactoryIsSealedAndHasNoPublicConstructor()
    {
        var factory = InstructionFactory.For<ExecutionContextBase>()
            .Creates(_ => new DependencyInstruction());

        Assert.IsType<InstructionFactory<ExecutionContextBase>>(factory);
        Assert.True(typeof(InstructionFactory<ExecutionContextBase>).IsSealed);
        Assert.Empty(typeof(InstructionFactory<ExecutionContextBase>).GetConstructors());
    }

    [Fact]
    public void ConcreteFactoryRegistrySupportsDifferentInstructionAndResultTypes()
    {
        InstructionFactory<ExecutionContextBase>[] factories =
        [
            InstructionFactory.For<ExecutionContextBase>().Creates(_ => new DependencyInstruction()),
            InstructionFactory.For<ExecutionContextBase>().Creates(_ => new ValueInstruction<int>(42)),
            InstructionFactory.For<ExecutionContextBase>().Creates(_ => new ValueInstruction<string>("answer"))
        ];
        Type[] expectedTypes = [typeof(DependencyInstruction), typeof(ValueInstruction<int>), typeof(ValueInstruction<string>)];

        for (var index = 0; index < factories.Length; index++)
        {
            var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factories[index].BindInputs([]));
            Assert.Equal(expectedTypes[index], ready.BoundInstructionType);
            Assert.Equal(expectedTypes[index], ready.Create().GetType());
        }
    }

    [Fact]
    public void ZeroInputFactoryDefersConstructionAndInfersInstructionType()
    {
        var creationCount = 0;
        var factory = InstructionFactory.For<ExecutionContextBase>().Creates(_ =>
        {
            creationCount++;
            return new DependencyInstruction();
        });

        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs([]));

        Assert.Equal(0, creationCount);
        Assert.Equal(typeof(DependencyInstruction), ready.BoundInstructionType);
        Assert.Empty(ready.Bindings);
        var first = ready.Create();
        Assert.Empty(first.Dependencies);
        Assert.NotSame(first, ready.Create());
        Assert.Equal(2, creationCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(32)]
    [InlineData(128)]
    public void FixedInputCountIsNotLimitedByDelegateArity(int count)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var handles = Enumerable.Range(0, count).Select(i => schema.Input<int>($"input{i}")).ToArray();
        var factory = schema.Creates(bound =>
            new DependencyInstruction(handles.Select(handle => bound.Get(handle)).ToArray()));
        InstructionBase[] candidates = Enumerable.Range(0, count)
            .Select(i => new ValueInstruction<int>(i)).ToArray();

        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs(candidates));
        var instruction = ready.Create();

        Assert.Equal(count, ready.Bindings.Count);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal($"input{index}", ready.Bindings[index].ParameterName);
            Assert.Same(candidates[index], ready.Bindings[index].BoundInstruction);
            Assert.Same(candidates[index], instruction.Dependencies[index]);
        }
    }

    [Fact]
    public void FixedInputsSupportDifferentResultTypesAndPreserveDuplicateReferences()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var number = schema.Input<int>("number");
        var text = schema.Input<string>("text");
        var repeatedNumber = schema.Input<int>("repeatedNumber");
        var source = new ValueInstruction<int>(42);
        var label = new ValueInstruction<string>("answer");
        var factory = schema.Creates(bound =>
        {
            InstructionBase<int, ExecutionContextBase> typedNumber = bound.Get(number);
            InstructionBase<string, ExecutionContextBase> typedText = bound.Get(text);
            return new DependencyInstruction(typedNumber, typedText, bound.Get(repeatedNumber));
        });

        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(
            factory.BindInputs([source, label, source]));
        var instruction = ready.Create();

        Assert.Same(source, instruction.Dependencies[0]);
        Assert.Same(label, instruction.Dependencies[1]);
        Assert.Same(source, instruction.Dependencies[2]);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 128)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 128)]
    public void VariadicInputsSupportEmptyAndUnboundedGroups(bool withPrefix, int count)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var prefix = withPrefix ? schema.Input<string>("prefix") : null;
        var terms = schema.Many<int>("terms");
        var factory = schema.Creates(bound =>
        {
            var group = bound.Get(terms);
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<InstructionBase<int, ExecutionContextBase>>>(group).Clear());
            return new DependencyInstruction(prefix is null
                ? group.ToArray()
                : new InstructionBase[] { bound.Get(prefix) }.Concat(group).ToArray());
        });
        var candidates = new List<InstructionBase>();
        if (withPrefix)
            candidates.Add(new ValueInstruction<string>("sum"));
        candidates.AddRange(Enumerable.Range(0, count).Select(i => new ValueInstruction<int>(i)));

        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs(candidates));
        var instruction = ready.Create();

        Assert.Equal(candidates.Count, instruction.Dependencies.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            Assert.Same(candidates[index], instruction.Dependencies[index]);
            Assert.Same(candidates[index], ready.Bindings[index].BoundInstruction);
            Assert.Equal(withPrefix && index == 0 ? "prefix" : "terms", ready.Bindings[index].ParameterName);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void IncorrectInputCountsReturnFailuresWithoutConstruction(bool variadic, int count)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var input = schema.Input<int>("input");
        if (variadic)
            schema.Many<int>("terms", minimumCount: 2);
        var creationCount = 0;
        var factory = schema.Creates(bound =>
        {
            creationCount++;
            return new DependencyInstruction(bound.Get(input));
        });
        InstructionBase[] candidates = Enumerable.Range(0, count)
            .Select(i => new ValueInstruction<int>(i)).ToArray();

        var failure = Assert.IsType<FailedResponse<ExecutionContextBase>>(factory.BindInputs(candidates));

        Assert.Equal(typeof(DependencyInstruction), failure.BoundInstructionType);
        Assert.Contains(variadic ? "at least 3" : "exactly 1", failure.Reason);
        Assert.Equal(0, creationCount);
    }

    [Fact]
    public void VariadicMinimumCountDoesNotOverflow()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        schema.Input<int>("prefix");
        schema.Many<int>("terms", minimumCount: int.MaxValue);
        var factory = schema.Creates(_ => new DependencyInstruction());

        var failure = Assert.IsType<FailedResponse<ExecutionContextBase>>(factory.BindInputs([]));

        Assert.Contains("at least 2147483648", failure.Reason);
    }

    [Theory]
    [InlineData(false, "result")]
    [InlineData(false, "context")]
    [InlineData(false, "null")]
    [InlineData(true, "result")]
    [InlineData(true, "context")]
    [InlineData(true, "null")]
    public void InvalidInputTypesAndNullEntriesReturnNamedFailures(bool variadic, string kind)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        schema.Input<int>("prefix");
        if (variadic)
            schema.Many<int>("terms");
        else
            schema.Input<int>("value");
        var factory = schema.Creates<DependencyInstruction>(
            _ => throw new InvalidOperationException("Must not construct."));
        InstructionBase candidate = kind switch
        {
            "result" => new ValueInstruction<string>("wrong"),
            "context" => new OtherContextInstruction(),
            "null" => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var failure = Assert.IsType<FailedResponse<ExecutionContextBase>>(
            factory.BindInputs([new ValueInstruction<int>(0), candidate]));

        Assert.Contains(variadic ? "'terms'" : "'value'", failure.Reason);
        Assert.Contains("index 1", failure.Reason);
        Assert.Contains(typeof(InstructionBase<int, ExecutionContextBase>).ToString(), failure.Reason);
    }

    [Fact]
    public void ZeroInputFactoryRejectsExtraCandidatesAndNullCollectionIsAnArgumentError()
    {
        var factory = InstructionFactory.For<ExecutionContextBase>()
            .Creates(_ => new DependencyInstruction());

        Assert.IsType<FailedResponse<ExecutionContextBase>>(
            factory.BindInputs([new ValueInstruction<int>(0)]));
        Assert.Throws<ArgumentNullException>(() => factory.BindInputs(null!));
    }

    [Fact]
    public void BindingSnapshotsCallerCollectionAndKeepsSeparateBindingsIndependent()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var input = schema.Input<int>("input");
        var terms = schema.Many<int>("terms");
        var factory = schema.Creates(bound =>
            new DependencyInstruction(new InstructionBase[] { bound.Get(input) }.Concat(bound.Get(terms)).ToArray()));
        var first = new ValueInstruction<int>(1);
        var second = new ValueInstruction<int>(2);
        var candidates = new List<InstructionBase> { first, first };
        var firstBinding = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs(candidates));
        candidates.Clear();
        candidates.Add(second);
        var secondBinding = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs(candidates));
        candidates.Clear();

        Assert.All(firstBinding.Create().Dependencies, dependency => Assert.Same(first, dependency));
        Assert.Equal(2, firstBinding.Bindings.Count);
        Assert.All(firstBinding.Bindings, binding => Assert.Same(first, binding.BoundInstruction));
        Assert.Same(second, Assert.Single(secondBinding.Create().Dependencies));
    }

    [Fact]
    public void CreatesFreezesSchemaAndNullConstructorDoesNotFreezeIt()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        Assert.Throws<ArgumentNullException>(() => schema.Creates<DependencyInstruction>(null!));
        var handle = schema.Input<int>("input");
        var factory = schema.Creates(bound => new DependencyInstruction(bound.Get(handle)));

        Assert.Throws<InvalidOperationException>(() => schema.Input<int>("later"));
        Assert.Throws<InvalidOperationException>(() => schema.Many<int>("later"));
        Assert.Throws<InvalidOperationException>(() => schema.Creates(_ => new DependencyInstruction()));
        Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs([new ValueInstruction<int>(0)]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ParameterNamesMustNotBeBlank(string? name)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();

        Assert.ThrowsAny<ArgumentException>(() => schema.Input<int>(name!));
        Assert.ThrowsAny<ArgumentException>(() => schema.Many<int>(name!));
    }

    [Fact]
    public void SchemaRejectsDuplicateNamesNegativeMinimumAndDeclarationsAfterVariadicGroup()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        schema.Input<int>("value");

        Assert.Throws<ArgumentException>(() => schema.Input<string>("value"));
        Assert.Throws<ArgumentException>(() => schema.Many<int>("value"));
        Assert.Throws<ArgumentOutOfRangeException>(() => schema.Many<int>("terms", -1));
        schema.Many<int>("terms");
        Assert.Throws<InvalidOperationException>(() => schema.Input<int>("later"));
        Assert.Throws<InvalidOperationException>(() => schema.Many<int>("later"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignParameterKeysAreRejectedEvenWithMatchingNamesAndPositions(bool variadic)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var other = InstructionFactory.For<ExecutionContextBase>();
        InstructionFactory<ExecutionContextBase> factory;
        if (variadic)
        {
            schema.Many<int>("input");
            var foreign = other.Many<int>("input");
            factory = schema.Creates(bound => new DependencyInstruction(bound.Get(foreign).ToArray()));
        }
        else
        {
            schema.Input<int>("input");
            var foreign = other.Input<int>("input");
            factory = schema.Creates(bound => new DependencyInstruction(bound.Get(foreign)));
        }
        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(
            factory.BindInputs([new ValueInstruction<int>(0)]));

        var error = Assert.Throws<InvalidOperationException>(() => ready.Create());

        Assert.Contains("different factory schema", error.Message);
    }

    [Fact]
    public void NullParameterKeysAreArgumentErrors()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var factory = schema.Creates(bound =>
        {
            Assert.Throws<ArgumentNullException>(() => bound.Get((InstructionCreationParameterKey<int, ExecutionContextBase>)null!));
            Assert.Throws<ArgumentNullException>(() => bound.Get((InstructionCreationParameterGroupKey<int, ExecutionContextBase>)null!));
            return new DependencyInstruction();
        });

        Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs([])).Create();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("order")]
    [InlineData("replacement")]
    [InlineData("null-list")]
    [InlineData("null-entry")]
    public void ConstructionRejectsDependenciesThatDoNotMatchBoundReferencesAndOrder(string kind)
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var firstHandle = schema.Input<int>("first");
        var secondHandle = schema.Input<int>("second");
        var first = new EqualInstruction();
        var second = new EqualInstruction();
        var factory = schema.Creates(bound =>
        {
            var dependencies = kind switch
            {
                "missing" => new InstructionBase[] { bound.Get(firstHandle) },
                "extra" => [first, second, first],
                "order" => [second, first],
                "replacement" => [new EqualInstruction(), second],
                "null-list" => null!,
                "null-entry" => [first, null!],
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return new EditableInstruction { DeclaredDependencies = dependencies };
        });
        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs([first, second]));

        var error = Assert.Throws<InvalidOperationException>(() => ready.Create());

        Assert.Contains("by reference and order", error.Message);
    }

    [Fact]
    public void NullAndUnexpectedInstructionTypesAreFactoryAuthorErrors()
    {
        var nullFactory = InstructionFactory.For<ExecutionContextBase>()
            .Creates<DependencyInstruction>(_ => null!);
        var wrongTypeFactory = InstructionFactory.For<ExecutionContextBase>()
            .Creates<InstructionBase<ExecutionContextBase>>(_ => new DependencyInstruction());

        Assert.Throws<InvalidOperationException>(() =>
            Assert.IsType<ReadyResponse<ExecutionContextBase>>(nullFactory.BindInputs([])).Create());
        Assert.Throws<InvalidOperationException>(() =>
            Assert.IsType<ReadyResponse<ExecutionContextBase>>(wrongTypeFactory.BindInputs([])).Create());
    }

    [Fact]
    public void ConstructorExceptionsPropagateWithoutBecomingBindingFailures()
    {
        var expected = new NotSupportedException("Domain construction failed.");
        var factory = InstructionFactory.For<ExecutionContextBase>()
            .Creates<DependencyInstruction>(_ => throw expected);
        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(factory.BindInputs([]));

        Assert.Same(expected, Assert.Throws<NotSupportedException>(() => ready.Create()));
    }

    [Fact]
    public async Task VariadicFactoryComposesWithLibraryRegistrationAndExecution()
    {
        var schema = InstructionFactory.For<ExecutionContextBase>();
        var terms = schema.Many<int>("terms", minimumCount: 1);
        var factory = schema.Creates(bound => new SumInstruction(bound.Get(terms)));
        var context = new ExecutionContextBase();
        var first = new ValueInstruction<int>(2);
        var ready = Assert.IsType<ReadyResponse<ExecutionContextBase>>(
            factory.BindInputs([first, new ValueInstruction<int>(3), first]));

        Assert.Empty(context.Operations);
        var instruction = Assert.IsType<SumInstruction>(ready.Create());
        Assert.Empty(context.Operations);
        var process = new ProcessBase<ExecutionContextBase>();
        process.Add(instruction);
        context.Register(process);
        var operation = instruction.GetTypedOperation(context);
        Assert.Equal(3, context.Operations.Count);
        Assert.False(operation.Promise().IsCompleted);

        await context.ExecuteAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(7, (await operation.Promise()).GetResult());
    }

    private sealed class DependencyInstruction(params InstructionBase[] dependencies)
        : InstructionBase<ExecutionContextBase>(dependencies)
    {
        protected override OperationInstanceBase CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            throw new NotSupportedException("Used only for factory binding.");
    }

    private class ValueInstruction<TResult>(TResult value) : InstructionBase<TResult, ExecutionContextBase>
    {
        protected override OperationInstanceBase<TResult> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            new ValueOperation<TResult>(context, value);
    }

    private sealed class ValueOperation<TResult>(ExecutionContextBase context, TResult value)
        : OperationInstanceBase<TResult>(context)
    {
        protected override Task<IOperationResult<TResult>> ExecuteResultAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IOperationResult<TResult>>(new ImmediateResult<TResult>(value));
    }

    private sealed class EqualInstruction() : ValueInstruction<int>(0)
    {
        public override bool Equals(object? obj) => obj is EqualInstruction;
        public override int GetHashCode() => 0;
    }

    private sealed class EditableInstruction : InstructionBase<ExecutionContextBase>
    {
        public IReadOnlyList<InstructionBase> DeclaredDependencies { get; init; } = [];
        public override IReadOnlyList<InstructionBase> Dependencies => DeclaredDependencies;

        protected override OperationInstanceBase CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            throw new NotSupportedException("Used only for factory binding.");
    }

    private sealed class OtherContext : ExecutionContextBase;

    private sealed class OtherContextInstruction : InstructionBase<int, OtherContext>
    {
        protected override OperationInstanceBase<int> CreateOperation(
            OtherContext context, InstructionInputs<OtherContext> inputs) =>
            throw new NotSupportedException("Used only for factory binding.");
    }

    private sealed class SumInstruction(IReadOnlyList<InstructionBase<int, ExecutionContextBase>> terms)
        : InstructionBase<int, ExecutionContextBase>(terms.ToArray())
    {
        protected override OperationInstanceBase<int> CreateOperation(
            ExecutionContextBase context, InstructionInputs<ExecutionContextBase> inputs) =>
            new SumOperation(context, terms.Select(term => inputs.GetOperation(term)).ToArray());
    }

    private sealed class SumOperation(
        ExecutionContextBase context, IReadOnlyList<OperationInstanceBase<int>> terms)
        : OperationInstanceBase<int>(context)
    {
        protected override async Task<IOperationResult<int>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var sum = 0;
            foreach (var term in terms)
                sum += (await term.Promise().WaitAsync(cancellationToken)).GetResult();
            return new ImmediateResult<int>(sum);
        }
    }
}
