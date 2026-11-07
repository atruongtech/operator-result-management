using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public sealed class DirectInputInstruction : IInstruction<int, NumberPipelineExecutionContext>
{
    private readonly int _input;

    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<IInstruction> Dependencies => Array.Empty<IInstruction>();

    public DirectInputInstruction(int input)
    {
        _input = input;
    }

    public int Input => _input;

    public IOperationInstance<int> GetTypedOperation(NumberPipelineExecutionContext context)
    {
        return context.GetOrAddOperation(this, () => new DirectInputOperation(context, _input));
    }

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context)
    {
        return GetTypedOperation(context);
    }
}

public sealed class DirectInputOperation : OperationInstanceBase<int>
{
    private readonly int _input;

    public DirectInputOperation(IExecutionContext context, int input) : base(context)
    {
        _input = input;
    }

    protected override Task<IOperationResult<int>> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(_input));
    }
}
