using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

/// <summary>
/// Produces the largest value minus the smallest value in an immutable integer list.
/// Empty input fails with ArgumentException; a difference outside int range overflows.
/// </summary>
public sealed class MinMaxDifferenceInstruction : InstructionBase<int, NumberPipelineExecutionContext>
{
    private readonly InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> _input;

    public MinMaxDifferenceInstruction(
        InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> input) : base(input)
    {
        _input = input;
    }

    protected override OperationInstanceBase<int> CreateOperation(
        NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
        new MinMaxDifferenceOperation(context, inputs.GetOperation(_input));
}

public sealed class MinMaxDifferenceOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly OperationInstanceBase<ImmutableList<int>> _input;

    public MinMaxDifferenceOperation(
        NumberPipelineExecutionContext context, OperationInstanceBase<ImmutableList<int>> input) : base(context)
    {
        ArgumentNullException.ThrowIfNull(input);
        _context = context;
        _input = input;
    }

    protected override async Task<IOperationResult<int>> ExecuteResultAsync(
        CancellationToken cancellationToken)
    {
        var result = await _input.Promise().WaitAsync(cancellationToken);
        var input = result.GetResult();
        ArgumentNullException.ThrowIfNull(input);
        if (input.Count == 0)
            throw new ArgumentException("Input must contain at least one integer.", nameof(input));

        var minimum = input[0];
        var maximum = input[0];
        foreach (var value in input)
        {
            cancellationToken.ThrowIfCancellationRequested();
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        _context.PromptProvider.InformUser($"Difference between mininum and maximum of [{string.Join(',', input)}] is {maximum - minimum}.");
        return new ImmediateResult<int>(checked(maximum - minimum));
    }
}
