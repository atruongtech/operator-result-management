using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

/// <summary>
/// Produces the largest value minus the smallest value in an immutable integer list.
/// Empty input fails with ArgumentException; a difference outside int range overflows.
/// </summary>
public sealed class MinMaxDifferenceInstruction : IInstruction<int, NumberPipelineExecutionContext>
{
    private readonly IInstruction<ImmutableList<int>, NumberPipelineExecutionContext> _input;

    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<IInstruction> Dependencies { get; }

    public MinMaxDifferenceInstruction(
        IInstruction<ImmutableList<int>, NumberPipelineExecutionContext> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
        Dependencies = Array.AsReadOnly<IInstruction>([input]);
    }

    public IOperationInstance<int> GetTypedOperation(NumberPipelineExecutionContext context) =>
        context.GetOrAddOperation(this,
            () => new MinMaxDifferenceOperation(context, _input.GetTypedOperation(context)));

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context) =>
        GetTypedOperation(context);
}

public sealed class MinMaxDifferenceOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly IOperationInstance<ImmutableList<int>> _input;

    public MinMaxDifferenceOperation(
        NumberPipelineExecutionContext context, IOperationInstance<ImmutableList<int>> input) : base(context)
    {
        ArgumentNullException.ThrowIfNull(input);
        _context = context;
        _input = input;
    }

    protected override async Task<IOperationResult<int>> ExecuteCoreAsync(
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
