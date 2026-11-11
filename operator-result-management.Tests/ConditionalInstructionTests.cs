using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class ConditionalInstructionTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcreteInstructionUsesFactoryWithoutRequiringSubclass(bool takeBranch)
    {
        using var cancellation = new CancellationTokenSource();
        var context = new TestContext(cancellation.Token);
        var input = new ValueInstruction<int>(42);
        var local = new SumInstruction(input);
        var parents = new List<TestContext>();
        var children = new List<TestContext>();
        var conditional = new ConditionalInstruction<TestContext>(
            new ValueInstruction<bool>(takeBranch), Body(local), parent =>
            {
                parents.Add(parent);
                var child = new TestContext(parent.CancellationToken, parent.Service);
                children.Add(child);
                return child;
            }, [input]);

        Assert.Empty(children);
        InstructionBase<TestContext> instruction = conditional;
        var operation = context.Register(instruction);
        Assert.Same(operation, conditional.GetOperation(context));
        Assert.Same(context, Assert.Single(parents));
        var child = Assert.Single(children);
        Assert.Equal(context.CancellationToken, child.CancellationToken);
        Assert.Same(context.Service, child.Service);
        Assert.Same(input.GetTypedOperation(context), input.GetTypedOperation(child));
        Assert.False(context.Operations.ContainsKey(local));
        Assert.False(operation.Completion.IsCompleted);
        Assert.Equal(0, Assert.IsType<ValueOperation<int>>(local.GetTypedOperation(child)).InvocationCount);

        await context.ExecuteAsync().WaitAsync(_timeout);
        await operation.Completion.WaitAsync(_timeout);

        Assert.Same(operation.Completion, operation.Execute());
        var localOperation = local.GetTypedOperation(child);
        Assert.Equal(takeBranch ? 1 : 0, Assert.IsType<ValueOperation<int>>(localOperation).InvocationCount);
        if (takeBranch)
            Assert.Equal(42, (await localOperation.Promise().WaitAsync(_timeout)).GetResult());
        else
            Assert.False(localOperation.Completion.IsCompleted);
        Assert.Single(children);

        var next = new TestContext(cancellation.Token);
        Assert.NotSame(operation, conditional.GetOperation(next));
        Assert.Equal(2, children.Count);
        Assert.NotSame(child, children[1]);
        Assert.Same(next, parents[1]);
        await next.ExecuteAsync().WaitAsync(_timeout);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistersBodySeparatelyAndExecutesOnlyWhenTrue(bool takeBranch)
    {
        var condition = new ValueInstruction<bool>(takeBranch);
        var local = new ValueInstruction<int>(42);
        var body = Body(local);
        var conditional = new Conditional(condition, body);
        var process = Body(conditional);
        var context = new TestContext();
        context.Register(process);
        var operation = conditional.GetOperation(context);
        var child = Assert.Single(conditional.Contexts);
        var localOperation = local.GetTypedOperation(child);

        Assert.Equal(new InstructionBase[] { condition, conditional }, process.Instructions);
        Assert.Equal(2, context.Operations.Count);
        Assert.False(context.Operations.ContainsKey(local));
        Assert.Single(child.Operations);
        Assert.Same(context.Service, child.Service);
        Assert.False(operation.Completion.IsCompleted);
        Assert.False(localOperation.Promise().IsCompleted);
        Assert.Equal(0, Assert.IsType<ValueOperation<int>>(localOperation).InvocationCount);
        Assert.Throws<InvalidOperationException>(() => child.Register(new ValueInstruction<int>(9)));

        var execution = context.ExecuteAsync();
        await execution.WaitAsync(_timeout);
        await operation.Completion.WaitAsync(_timeout);
        Assert.Same(execution, context.ExecuteAsync());
        Assert.Same(operation, conditional.GetOperation(context));
        Assert.Same(operation.Completion, operation.Execute());
        Assert.Single(conditional.Contexts);
        Assert.Equal(takeBranch ? 1 : 0, Assert.IsType<ValueOperation<int>>(localOperation).InvocationCount);
        if (takeBranch)
            Assert.Equal(42, (await localOperation.Promise().WaitAsync(_timeout)).GetResult());
        else
            Assert.False(localOperation.Promise().IsCompleted);
    }

    [Fact]
    public async Task ConditionalAndDownstreamWaitForEntireBody()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var local = new ValueInstruction<int>(0)
        {
            Work = token =>
            {
                started.SetResult();
                return release.Task.WaitAsync(token);
            }
        };
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(local));
        var downstream = new AfterInstruction(conditional);
        var context = new TestContext();
        var downstreamOperation = context.Register(downstream);
        var operation = conditional.GetOperation(context);
        var run = context.ExecuteAsync();
        await started.Task.WaitAsync(_timeout);

        Assert.False(run.IsCompleted);
        Assert.False(operation.Completion.IsCompleted);
        Assert.False(downstreamOperation.Promise().IsCompleted);

        release.SetResult(42);
        await run.WaitAsync(_timeout);
        Assert.Equal(1, (await downstreamOperation.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public async Task FalseConditionalAlsoReleasesDownstream()
    {
        var local = new ValueInstruction<int>(0) { Work = _ => throw new InvalidOperationException() };
        var conditional = new Conditional(new ValueInstruction<bool>(false), Body(local));
        var context = new TestContext();
        var downstream = context.Register(new AfterInstruction(conditional));

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(1, (await downstream.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Fact]
    public async Task FalseBodyDoesNotRunLocalUpstreamWorkButDeclaredOuterInputsStillRun()
    {
        var localInput = new ValueInstruction<int>(42)
        {
            Work = _ => throw new InvalidOperationException("Body input must not run.")
        };
        var outerInput = new ValueInstruction<int>(7);
        var local = new SumInstruction(localInput, outerInput);
        var conditional = new Conditional(new ValueInstruction<bool>(false), Body(local), [outerInput]);
        var context = new TestContext();
        context.Register(conditional);

        await context.ExecuteAsync().WaitAsync(_timeout);

        var child = Assert.Single(conditional.Contexts);
        Assert.All(child.Operations.Values,
            operation => Assert.Equal(0, Assert.IsType<ValueOperation<int>>(operation).InvocationCount));
        Assert.Equal(1, Assert.IsType<ValueOperation<int>>(outerInput.GetTypedOperation(context)).InvocationCount);
    }

    [Fact]
    public void FalseConditionDoesNotSuppressInvalidBodyRegistration()
    {
        var local = new ValueInstruction<int>(42) { FailRegistration = true };
        var conditional = new Conditional(new ValueInstruction<bool>(false), Body(local));

        Assert.Throws<InvalidOperationException>(() => new TestContext().Register(conditional));
    }

    [Fact]
    public async Task SelectedConditionalDoesNotActivateUnrelatedParentOrSiblingWork()
    {
        var input = new ValueInstruction<int>(21);
        var first = new Conditional(new ValueInstruction<bool>(true), Body(new SumInstruction(input)), [input]);
        var second = new Conditional(new ValueInstruction<bool>(true), Body(new SumInstruction(input)), [input]);
        var unrelated = new ValueInstruction<int>(0)
        {
            Work = _ => throw new InvalidOperationException("Unselected work.")
        };
        var process = Body(first, second, unrelated);
        var context = new TestContext();
        context.Register(new ProcessRunRequest<TestContext>(process, [first]));

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Single(first.Contexts);
        Assert.Empty(second.Contexts);
        Assert.False(context.Operations.ContainsKey(unrelated));
        Assert.Equal(1, Assert.IsType<ValueOperation<int>>(input.GetTypedOperation(context)).InvocationCount);
    }

    [Fact]
    public async Task ImportsShareParentOperationsAndDoNotBelongToBody()
    {
        var input = new ValueInstruction<int>(20);
        var outer = new SumInstruction(input, input);
        var local = new SumInstruction(outer, input);
        var condition = new ValueInstruction<bool>(true);
        var conditional = new Conditional(condition, Body(local), [outer]);
        var context = new TestContext();
        context.Register(conditional);
        var child = Assert.Single(conditional.Contexts);

        Assert.Equal(4, context.Operations.Count);
        Assert.Single(child.Operations);
        Assert.Same(input.GetTypedOperation(context), input.GetTypedOperation(child));
        Assert.Same(outer.GetTypedOperation(context), outer.GetTypedOperation(child));
        Assert.Same(condition.GetTypedOperation(context), condition.GetTypedOperation(child));

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(60, (await local.GetTypedOperation(child).Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(1, Assert.IsType<ValueOperation<int>>(input.GetTypedOperation(context)).InvocationCount);
        Assert.Equal(1, Assert.IsType<ValueOperation<int>>(outer.GetTypedOperation(context)).InvocationCount);
    }

    [Fact]
    public async Task CapturesBodyMembershipValuesAndDependenciesAtRegistration()
    {
        var condition = new ValueInstruction<bool>(true);
        var input = new ValueInstruction<int>(21);
        var replacement = new ValueInstruction<int>(100);
        var local = new SumInstruction(input, input);
        var body = Body(local);
        var conditional = new Conditional(condition, body);
        var context = new TestContext();
        context.Register(conditional);
        var child = Assert.Single(conditional.Contexts);

        condition.Value = false;
        input.Value = 50;
        local.Inputs = [replacement];
        var later = new ValueInstruction<int>(999);
        body.Add(later);
        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(42, (await local.GetTypedOperation(child).Promise().WaitAsync(_timeout)).GetResult());
        Assert.False(child.Operations.ContainsKey(later));
        Assert.False(child.Operations.ContainsKey(replacement));

        var next = new TestContext();
        var nextOperation = conditional.GetOperation(next);
        var nextBody = conditional.Contexts[1];
        await next.ExecuteAsync().WaitAsync(_timeout);
        await nextOperation.Completion.WaitAsync(_timeout);
        Assert.True(nextBody.Operations.ContainsKey(later));
        Assert.True(nextBody.Operations.ContainsKey(replacement));
        Assert.Equal(0, Assert.IsType<ValueOperation<int>>(local.GetTypedOperation(nextBody)).InvocationCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NestedBodiesRequireEveryEnclosingCondition(bool outerValue, bool innerValue)
    {
        var input = new ValueInstruction<int>(21);
        var local = new SumInstruction(input, input);
        var inner = new Conditional(new ValueInstruction<bool>(innerValue), Body(local), [input]);
        var outer = new Conditional(new ValueInstruction<bool>(outerValue), Body(inner), [input]);
        var context = new TestContext();
        context.Register(outer);
        var outerBody = Assert.Single(outer.Contexts);
        var innerBody = Assert.Single(inner.Contexts);
        Assert.Same(input.GetTypedOperation(context), input.GetTypedOperation(innerBody));
        Assert.False(outerBody.Operations.ContainsKey(local));
        Assert.False(context.Operations.ContainsKey(inner));

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(outerValue && innerValue ? 1 : 0,
            Assert.IsType<ValueOperation<int>>(local.GetTypedOperation(innerBody)).InvocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyBodyCompletes(bool condition)
    {
        var conditional = new Conditional(new ValueInstruction<bool>(condition), Body());
        var context = new TestContext();
        var operation = conditional.GetOperation(context);

        await context.ExecuteAsync().WaitAsync(_timeout);
        await operation.Completion.WaitAsync(_timeout);
        Assert.Empty(Assert.Single(conditional.Contexts).Operations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailurePropagatesWithoutRetry(bool conditionFails)
    {
        var error = new InvalidOperationException("Failed.");
        var condition = new ValueInstruction<bool>(true);
        var local = new ValueInstruction<int>(42);
        if (conditionFails)
            condition.Work = _ => throw error;
        else
            local.Work = _ => throw error;
        var conditional = new Conditional(condition, Body(local));
        var context = new TestContext();
        var operation = conditional.GetOperation(context);
        var child = Assert.Single(conditional.Contexts);

        var run = context.ExecuteAsync();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => run.WaitAsync(_timeout)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(
            () => operation.Completion.WaitAsync(_timeout)));
        var execution = operation.Execute();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => execution.WaitAsync(_timeout)));
        Assert.Same(execution, operation.Execute());
        Assert.Equal(conditionFails ? 0 : 1,
            Assert.IsType<ValueOperation<int>>(local.GetTypedOperation(child)).InvocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationWhileWaitingForConditionOrBodyPropagates(bool waitForCondition)
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var condition = new ValueInstruction<bool>(true);
        var local = new ValueInstruction<int>(42);
        if (waitForCondition)
            condition.Work = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return true;
            };
        else
            local.Work = async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 42;
            };
        var conditional = new Conditional(condition, Body(local));
        var context = new TestContext(cancellation.Token);
        var operation = conditional.GetOperation(context);
        var run = context.ExecuteAsync();
        await started.Task.WaitAsync(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(_timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion.WaitAsync(_timeout));
        Assert.True(operation.Completion.IsCanceled);
    }

    [Fact]
    public async Task PreCanceledRunDoesNotStartBody()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(new ValueInstruction<int>(1)));
        var context = new TestContext(cancellation.Token);
        var operation = conditional.GetOperation(context);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.ExecuteAsync().WaitAsync(_timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.Completion.WaitAsync(_timeout));
        Assert.All(Assert.Single(conditional.Contexts).Operations.Values,
            value => Assert.Equal(0, Assert.IsType<ValueOperation<int>>(value).InvocationCount));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParentCannotReferenceBodyLocalInstructionsRegardlessOfRegistrationOrder(bool parentFirst)
    {
        var local = new ValueInstruction<int>(42);
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(local));
        var context = new TestContext();
        if (parentFirst)
        {
            context.Register(local);
            Assert.Throws<InvalidOperationException>(() => context.Register(conditional));
        }
        else
        {
            context.Register(conditional);
            Assert.Throws<InvalidOperationException>(() => context.Register(new SumInstruction(local)));
        }
    }

    [Fact]
    public void UndeclaredOuterInputsAndSharedSiblingLocalsAreRejected()
    {
        var input = new ValueInstruction<int>(42);
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(new SumInstruction(input)));
        var context = new TestContext();
        context.Register(input);
        Assert.Throws<InvalidOperationException>(() => context.Register(conditional));

        var local = new ValueInstruction<int>(1);
        var first = new Conditional(new ValueInstruction<bool>(true), Body(local));
        var second = new Conditional(new ValueInstruction<bool>(true), Body(local));
        var next = new TestContext();
        next.Register(first);
        Assert.Throws<InvalidOperationException>(() => next.Register(second));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecursiveContainmentIsRejected(bool direct)
    {
        var firstBody = Body();
        var first = new Conditional(new ValueInstruction<bool>(true), firstBody);
        if (direct)
            firstBody.Add(first);
        else
        {
            var second = new Conditional(new ValueInstruction<bool>(true), Body(first));
            firstBody.Add(second);
        }

        Assert.Throws<InvalidOperationException>(() => new TestContext().Register(first));
    }

    [Fact]
    public void FailedBodyRegistrationReleasesNestedScopeOwnershipForRetry()
    {
        var nestedLocal = new ValueInstruction<int>(7);
        var nested = new Conditional(new ValueInstruction<bool>(true), Body(nestedLocal));
        var failing = new ValueInstruction<int>(42) { FailRegistration = true };
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(nested, failing));
        var context = new TestContext();

        Assert.Throws<InvalidOperationException>(() => context.Register(conditional));
        failing.FailRegistration = false;
        context.Register(conditional);

        Assert.Equal(2, conditional.Contexts.Count);
        Assert.Equal(2, nested.Contexts.Count);
        Assert.False(context.Operations.ContainsKey(nestedLocal));
    }

    [Fact]
    public async Task ConcurrentRegistrationAndExecutionShareOneBody()
    {
        var local = new ValueInstruction<int>(42);
        var conditional = new Conditional(new ValueInstruction<bool>(true), Body(local));
        var context = new TestContext();
        var operations = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => conditional.GetOperation(context)))).WaitAsync(_timeout);
        Assert.All(operations, operation => Assert.Same(operations[0], operation));
        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(context.ExecuteAsync))).WaitAsync(_timeout);

        Assert.Equal(1, Assert.IsType<ValueOperation<int>>(
            local.GetTypedOperation(Assert.Single(conditional.Contexts))).InvocationCount);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("token")]
    [InlineData("registered")]
    [InlineData("executed")]
    [InlineData("null")]
    public void RejectsInvalidBodyContext(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        var context = new TestContext(cancellation.Token);
        var conditional = new ConditionalInstruction<TestContext>(
            new ValueInstruction<bool>(true), Body(), parent =>
            {
                if (kind == "parent") return parent;
                if (kind == "null") return null!;
                var child = new TestContext(kind == "token" ? default : parent.CancellationToken);
                if (kind == "registered") child.Register(new ValueInstruction<int>(1));
                if (kind == "executed") _ = child.ExecuteAsync();
                return child;
            });

        Assert.Throws<InvalidOperationException>(() => context.Register(conditional));
    }

    [Fact]
    public void FactoryFailurePropagatesAndAllowsRegistrationRetry()
    {
        var error = new InvalidOperationException("Body context creation failed.");
        var fail = true;
        var conditional = new ConditionalInstruction<TestContext>(
            new ValueInstruction<bool>(false), Body(), parent =>
                fail ? throw error : new TestContext(parent.CancellationToken, parent.Service));
        var context = new TestContext();

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => context.Register(conditional)));
        Assert.False(context.Operations.ContainsKey(conditional));

        fail = false;
        var operation = context.Register(conditional);
        Assert.Same(operation, conditional.GetOperation(context));
        Assert.False(operation.Completion.IsCompleted);
    }

    [Fact]
    public void RejectsNullArgumentsAndSnapshotsOuterInputList()
    {
        var condition = new ValueInstruction<bool>(true);
        Func<TestContext, TestContext> factory =
            parent => new TestContext(parent.CancellationToken, parent.Service);
        Assert.Throws<ArgumentNullException>(() =>
            new ConditionalInstruction<TestContext>(null!, Body(), factory));
        Assert.Throws<ArgumentNullException>(() =>
            new ConditionalInstruction<TestContext>(condition, null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new ConditionalInstruction<TestContext>(condition, Body(), null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ConditionalInstruction<TestContext>(condition, Body(), factory, [null!]));
        var input = new ValueInstruction<int>(42);
        InstructionBase<TestContext>[] inputs = [input, input, condition];
        var conditional = new ConditionalInstruction<TestContext>(condition, Body(), factory, inputs);
        inputs[0] = new ValueInstruction<int>(9);

        Assert.Equal(new InstructionBase[] { condition, input }, conditional.Dependencies);
        Assert.Throws<ArgumentNullException>(() => conditional.GetOperation(null!));
    }

    private static ProcessBase<TestContext> Body(params InstructionBase<TestContext>[] instructions)
    {
        var body = new ProcessBase<TestContext>();
        foreach (var instruction in instructions)
            body.Add(instruction);
        return body;
    }

    private sealed class TestContext(CancellationToken cancellationToken = default, object? service = null)
        : ExecutionContextBase(cancellationToken)
    {
        public object Service { get; } = service ?? new object();
    }

    private sealed class Conditional(
        InstructionBase<bool, TestContext> condition,
        ProcessBase<TestContext> body,
        IEnumerable<InstructionBase<TestContext>>? outerInputs = null)
        : ConditionalInstruction<TestContext>(
            condition, body, parent => new TestContext(parent.CancellationToken, parent.Service), outerInputs)
    {
        public List<TestContext> Contexts { get; } = [];

        protected override TestContext CreateBodyContext(TestContext parent)
        {
            var context = base.CreateBodyContext(parent);
            Contexts.Add(context);
            return context;
        }
    }

    private sealed class ValueInstruction<T>(T value) : InstructionBase<T, TestContext>
    {
        public T Value { get; set; } = value;
        public Func<CancellationToken, Task<T>>? Work { get; set; }
        public bool FailRegistration { get; set; }

        protected override OperationInstanceBase<T> CreateOperation(
            TestContext context, InstructionInputs<TestContext> inputs)
        {
            if (FailRegistration)
                throw new InvalidOperationException("Registration failed.");
            var captured = Value;
            return new ValueOperation<T>(context, Work ?? (_ => Task.FromResult(captured)));
        }
    }

    private sealed class SumInstruction(params InstructionBase<int, TestContext>[] inputs)
        : InstructionBase<int, TestContext>
    {
        public InstructionBase<int, TestContext>[] Inputs { get; set; } = inputs;
        public override IReadOnlyList<InstructionBase> Dependencies => Inputs;

        protected override OperationInstanceBase<int> CreateOperation(
            TestContext context, InstructionInputs<TestContext> inputs)
        {
            var operations = Inputs.Select(input => inputs.GetOperation(input)).ToArray();
            return new ValueOperation<int>(context, async token =>
            {
                var sum = 0;
                foreach (var operation in operations)
                    sum += (await operation.Promise().WaitAsync(token)).GetResult();
                return sum;
            });
        }
    }

    private sealed class AfterInstruction(Conditional conditional) : InstructionBase<int, TestContext>(conditional)
    {
        protected override OperationInstanceBase<int> CreateOperation(
            TestContext context, InstructionInputs<TestContext> inputs)
        {
            var operation = inputs.GetOperation(conditional);
            return new ValueOperation<int>(context, async token =>
            {
                await operation.Completion.WaitAsync(token);
                return 1;
            });
        }
    }

    private sealed class ValueOperation<T>(TestContext context, Func<CancellationToken, Task<T>> work)
        : OperationInstanceBase<T>(context)
    {
        public int InvocationCount { get; private set; }

        protected override async Task<IOperationResult<T>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return new ImmediateResult<T>(await work(cancellationToken));
        }
    }
}
