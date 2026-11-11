using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public sealed class DirectInputInstruction : InstructionBase<int, NumberPipelineExecutionContext>
{
    protected override OperationInstanceBase<int> CreateOperation(
        NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
        new DirectInputOperation(context);
}

public sealed class DirectInputOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;

    public DirectInputOperation(NumberPipelineExecutionContext context) : base(context)
    {
        _context = context;
    }

    protected override Task<IOperationResult<int>> ExecuteResultAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IOperationResult<int>>(new ImmediateResult<int>(_context.PromptProvider.PromptUserForIntInput("Input integer value:")));
    }
}
