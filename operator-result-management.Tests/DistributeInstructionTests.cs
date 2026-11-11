using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class DistributeInstructionTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(3, new int[] { 2, 2, 5 }, new int[] { 5, 5, 8 })]
    [InlineData(-2, new int[] { -3, 0, 4 }, new int[] { -5, -2, 2 })]
    [InlineData(0, new int[] { 4, 1, 4 }, new int[] { 4, 1, 4 })]
    [InlineData(7, new int[] { }, new int[] { })]
    public async Task AddsScalarToEachValueWithoutChangingInput(int value, int[] values, int[] expected)
    {
        var original = values.ToImmutableList();
        var scalar = new ConstantInstruction<int>(value);
        var list = new ConstantInstruction<ImmutableList<int>>(original);
        var instruction = new DistributeInstruction(scalar, list);
        var process = new ProcessBase<NumberPipelineExecutionContext>();
        process.Add(instruction);
        var context = new NumberPipelineExecutionContext(new());
        context.Register(process);
        var operation = instruction.GetTypedOperation(context);

        Assert.Equal(new InstructionBase[] { scalar, list }, instruction.Dependencies);
        Assert.Equal(3, context.Operations.Count);
        Assert.Same(operation, instruction.GetOperation(context));
        Assert.False(operation.Promise().IsCompleted);

        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(expected, (await operation.Promise().WaitAsync(_timeout)).GetResult());
        Assert.Equal(values, original);
    }

    [Fact]
    public async Task ComposesWithSharedScalarAndFactorInstructions()
    {
        var scalar = new ConstantInstruction<int>(12);
        var instruction = new DistributeInstruction(scalar, new FactorInstruction(scalar));
        var context = new NumberPipelineExecutionContext(new());
        var operation = instruction.GetTypedOperation(context);

        Assert.Equal(3, context.Operations.Count);
        await context.ExecuteAsync().WaitAsync(_timeout);

        Assert.Equal(new[] { 14, 14, 15 }, (await operation.Promise().WaitAsync(_timeout)).GetResult());
    }

    [Theory]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MinValue, -1)]
    public async Task OverflowFaultsExecutionAndResult(int value, int item)
    {
        var instruction = new DistributeInstruction(
            new ConstantInstruction<int>(value),
            new ConstantInstruction<ImmutableList<int>>([item]));
        var context = new NumberPipelineExecutionContext(new());
        var operation = instruction.GetTypedOperation(context);

        await Assert.ThrowsAsync<OverflowException>(() => context.ExecuteAsync().WaitAsync(_timeout));
        await Assert.ThrowsAsync<OverflowException>(() => operation.Promise().WaitAsync(_timeout));
    }

    [Fact]
    public async Task CancellationInterruptsWaitingForInputs()
    {
        using var cancellation = new CancellationTokenSource();
        var context = new NumberPipelineExecutionContext(new(), cancellation.Token);
        var instruction = new DistributeInstruction(
            new ConstantInstruction<int>(3),
            new ConstantInstruction<ImmutableList<int>>([1, 2]));
        var operation = instruction.GetTypedOperation(context);
        var execution = operation.Execute();

        Assert.False(execution.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(_timeout));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operation.Promise().WaitAsync(_timeout));
    }

    [Fact]
    public void RejectsNullInstructionsAndOperations()
    {
        var scalar = new ConstantInstruction<int>(1);
        var list = new ConstantInstruction<ImmutableList<int>>([]);
        var context = new NumberPipelineExecutionContext(new());

        Assert.Throws<ArgumentNullException>(() => new DistributeInstruction(null!, list));
        Assert.Throws<ArgumentNullException>(() => new DistributeInstruction(scalar, null!));
        Assert.Throws<ArgumentNullException>(
            () => new DistributeOperation(context, null!, list.GetTypedOperation(context)));
        Assert.Throws<ArgumentNullException>(
            () => new DistributeOperation(context, scalar.GetTypedOperation(context), null!));
    }

    [Fact]
    public void FactoryRejectsWrongCountsTypesOrderAndContexts()
    {
        var scalar = new ConstantInstruction<int>(1);
        var list = new ConstantInstruction<ImmutableList<int>>([]);
        IReadOnlyList<InstructionBase>[] invalidInputs =
        [
            [],
            [scalar],
            [scalar, list, scalar],
            [list, scalar],
            [scalar, scalar],
            [list, list],
            [new OtherContextInstruction<int>(), list],
            [scalar, new OtherContextInstruction<ImmutableList<int>>()]
        ];
        var factory = new DistributeInstructionFactory();

        foreach (var inputs in invalidInputs)
        {
            var failure = Assert.IsType<FailedResponse<NumberPipelineExecutionContext>>(
                factory.BindInputs(inputs));
            Assert.Equal(typeof(DistributeInstruction), failure.BoundInstructionType);
            Assert.False(string.IsNullOrWhiteSpace(failure.Reason));
        }
    }

    private sealed class ConstantInstruction<TResult>(TResult value)
        : InstructionBase<TResult, NumberPipelineExecutionContext>
    {
        protected override OperationInstanceBase<TResult> CreateOperation(
            NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
            new ConstantOperation<TResult>(context, value);
    }

    private sealed class ConstantOperation<TResult>(NumberPipelineExecutionContext context, TResult value)
        : OperationInstanceBase<TResult>(context)
    {
        protected override Task<IOperationResult<TResult>> ExecuteResultAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IOperationResult<TResult>>(new ImmediateResult<TResult>(value));
    }

    private sealed class OtherContext : ExecutionContextBase;

    private sealed class OtherContextInstruction<TResult> : InstructionBase<TResult, OtherContext>
    {
        protected override OperationInstanceBase<TResult> CreateOperation(
            OtherContext context, InstructionInputs<OtherContext> inputs) =>
            throw new NotSupportedException("This instruction is only used for binding.");
    }
}
