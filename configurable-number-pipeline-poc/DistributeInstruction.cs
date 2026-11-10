using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

/// <summary>
/// Adds a scalar to every value in an immutable integer list.
/// A sum outside int range overflows.
/// </summary>
public sealed class DistributeInstruction : IInstruction<ImmutableList<int>, NumberPipelineExecutionContext>
{
    private readonly IInstruction<int, NumberPipelineExecutionContext> _value;
    private readonly IInstruction<ImmutableList<int>, NumberPipelineExecutionContext> _values;

    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<IInstruction> Dependencies { get; }

    public DistributeInstruction(
        IInstruction<int, NumberPipelineExecutionContext> toDistribute,
        IInstruction<ImmutableList<int>, NumberPipelineExecutionContext> distributeAmong)
    {
        ArgumentNullException.ThrowIfNull(toDistribute);
        ArgumentNullException.ThrowIfNull(distributeAmong);
        _value = toDistribute;
        _values = distributeAmong;
        Dependencies = Array.AsReadOnly<IInstruction>([toDistribute, distributeAmong]);
    }

    public IOperationInstance<ImmutableList<int>> GetTypedOperation(NumberPipelineExecutionContext context) =>
        context.GetOrAddOperation(this,
            () => new DistributeOperation(context,
                _value.GetTypedOperation(context), _values.GetTypedOperation(context)));

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context) =>
        GetTypedOperation(context);
}

public sealed class DistributeOperation : OperationInstanceBase<ImmutableList<int>>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly IOperationInstance<int> _value;
    private readonly IOperationInstance<ImmutableList<int>> _values;

    public DistributeOperation(
        NumberPipelineExecutionContext context,
        IOperationInstance<int> value,
        IOperationInstance<ImmutableList<int>> values) : base(context)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(values);
        _context = context;
        _value = value;
        _values = values;
    }

    protected override async Task<IOperationResult<ImmutableList<int>>> ExecuteCoreAsync(
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
