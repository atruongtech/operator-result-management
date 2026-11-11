using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

/// <summary>
/// Adds a scalar to every value in an immutable integer list.
/// A sum outside int range overflows.
/// </summary>
public sealed class DistributeInstruction : InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext>
{
    private readonly InstructionBase<int, NumberPipelineExecutionContext> _value;
    private readonly InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> _values;

    public DistributeInstruction(
        InstructionBase<int, NumberPipelineExecutionContext> toDistribute,
        InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext> distributeAmong)
        : base(toDistribute, distributeAmong)
    {
        _value = toDistribute;
        _values = distributeAmong;
    }

    protected override OperationInstanceBase<ImmutableList<int>> CreateOperation(
        NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
        new DistributeOperation(context, inputs.GetOperation(_value), inputs.GetOperation(_values));
}

public sealed class DistributeOperation : OperationInstanceBase<ImmutableList<int>>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly OperationInstanceBase<int> _value;
    private readonly OperationInstanceBase<ImmutableList<int>> _values;

    public DistributeOperation(
        NumberPipelineExecutionContext context,
        OperationInstanceBase<int> value,
        OperationInstanceBase<ImmutableList<int>> values) : base(context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(values);
        _context = context;
        _value = value;
        _values = values;
    }

    protected override async Task<IOperationResult<ImmutableList<int>>> ExecuteResultAsync(
        CancellationToken cancellationToken)
    {
        var value = (await _value.Promise().WaitAsync(cancellationToken)).GetResult();
        var values = (await _values.Promise().WaitAsync(cancellationToken)).GetResult();
        ArgumentNullException.ThrowIfNull(values);

        var output = ImmutableList.CreateBuilder<int>();
        foreach (var item in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Add(checked(value + item));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = output.ToImmutable();
        _context.PromptProvider.InformUser(
            $"Adding {value} to [{string.Join(", ", values)}]: [{string.Join(", ", result)}]");
        return new ImmediateResult<ImmutableList<int>>(result);
    }
}
