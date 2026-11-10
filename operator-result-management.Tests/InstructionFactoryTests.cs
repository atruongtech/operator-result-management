using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management.Basic;
using Xunit;

namespace operator_result_management.Tests;

public class InstructionFactoryTests
{
    [Theory]
    [InlineData(0, typeof(DirectInputInstruction))]
    [InlineData(1, typeof(FactorInstruction))]
    [InlineData(2, typeof(MinMaxDifferenceInstruction))]
    [InlineData(3, typeof(UserInputMultiplyInstruction))]
    [InlineData(4, typeof(DistributeInstruction))]
    public void ReadyResponsesCreateContextTypedInstructionsWithBoundDependencies(
        int factoryIndex, Type instructionType)
    {
        var input = new DirectInputInstruction();
        IReadOnlyList<IInstruction> inputs = factoryIndex switch
        {
            0 => [],
            2 => [new FactorInstruction(input)],
            4 => [input, new FactorInstruction(input)],
            _ => [input]
        };
        IInstructionFactory<NumberPipelineExecutionContext> factory = GetFactory(factoryIndex);

        var ready = Assert.IsType<ReadyResponse<NumberPipelineExecutionContext>>(
            factory.BindInputs(inputs));
        IInstruction<NumberPipelineExecutionContext> instruction = ready.Create();
        var process = new ProcessBase<NumberPipelineExecutionContext>();
        process.Add(instruction);

        Assert.Equal(instructionType, ready.BoundInstructionType);
        Assert.Equal(instructionType, instruction.GetType());
        Assert.Same(instruction, process.Instructions[^1]);
        Assert.Equal(inputs, instruction.Dependencies);
        Assert.Equal(inputs.Count, ready.Bindings.Count);
        for (var i = 0; i < inputs.Count; i++)
        {
            Assert.Equal(factoryIndex == 4 ? (i == 0 ? "toDistribute" : "distributeAmong") : "input",
                ready.Bindings[i].ParameterName);
            Assert.Same(inputs[i], ready.Bindings[i].BoundInstruction);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FactoriesRejectIncorrectInputCounts(int factoryIndex)
    {
        IReadOnlyList<IInstruction> inputs = factoryIndex == 0
            ? [new DirectInputInstruction()]
            : [];

        var failed = Assert.IsType<FailedResponse<NumberPipelineExecutionContext>>(
            GetFactory(factoryIndex).BindInputs(inputs));

        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FactoriesRejectIncorrectInputResultTypes(int factoryIndex)
    {
        var input = new DirectInputInstruction();
        IInstruction wrongResult = factoryIndex == 2 ? input : new FactorInstruction(input);

        var failed = Assert.IsType<FailedResponse<NumberPipelineExecutionContext>>(
            GetFactory(factoryIndex).BindInputs([wrongResult]));

        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FactoriesRejectInputsForAnotherContext(int factoryIndex)
    {
        IInstruction wrongContext = factoryIndex == 2
            ? new OtherContextInstruction<ImmutableList<int>>()
            : new OtherContextInstruction<int>();

        var failed = Assert.IsType<FailedResponse<NumberPipelineExecutionContext>>(
            GetFactory(factoryIndex).BindInputs([wrongContext]));

        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }

    private static IInstructionFactory<NumberPipelineExecutionContext> GetFactory(int index) =>
        index switch
        {
            0 => new DirectInputInstructionFactory(),
            1 => new FactorInstructionFactory(),
            2 => new MinMaxDifferenceInstructionFactory(),
            3 => new UserInputMultiplyInstructionFactory(),
            4 => new DistributeInstructionFactory(),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

    private sealed class OtherContext : ExecutionContextBase;

    private sealed class OtherContextInstruction<TResult> : IInstruction<TResult, OtherContext>
    {
        public Guid Id { get; } = Guid.NewGuid();
        public IReadOnlyList<IInstruction> Dependencies => [];

        public IOperationInstance GetOperation(OtherContext context) =>
            throw new NotSupportedException("This instruction is only used for binding.");

        public IOperationInstance<TResult> GetTypedOperation(OtherContext context) =>
            throw new NotSupportedException("This instruction is only used for binding.");
    }
}
