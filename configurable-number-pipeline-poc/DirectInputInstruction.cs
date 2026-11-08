using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public sealed class DirectInputInstruction : IInstruction<int, NumberPipelineExecutionContext>
{
    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<IInstruction> Dependencies => Array.Empty<IInstruction>();

    public DirectInputInstruction()
    {
    }

    public IOperationInstance<int> GetTypedOperation(NumberPipelineExecutionContext context)
    {
        return context.GetOrAddOperation(this, () => new DirectInputOperation(context));
    }

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context)
    {
        return GetTypedOperation(context);
    }
}

public sealed class DirectInputOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;

    public DirectInputOperation(NumberPipelineExecutionContext context) : base(context)
    {
        _context = context;
    }

    protected override Task<IOperationResult<int>> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(_context.PromptProvider.PromptUserForIntInput("Input integer value:")));
    }
}
