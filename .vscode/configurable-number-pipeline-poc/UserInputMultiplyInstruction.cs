using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class UserInputMultiplyInstruction : IInstruction<int, NumberPipelineExecutionContext>
{
    private readonly IInstruction<int, NumberPipelineExecutionContext> _input;

    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<IInstruction> Dependencies { get; }

    public UserInputMultiplyInstruction(IInstruction<int, NumberPipelineExecutionContext> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _input = input;
        Dependencies = Array.AsReadOnly<IInstruction>([input]);
    }

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context)
    {
        return GetTypedOperation(context);
    }

    public IOperationInstance<int> GetTypedOperation(NumberPipelineExecutionContext context)
    {
        return context.GetOrAddOperation(this,
            () => new UserInputMultiplyOperation(context, _input.GetTypedOperation(context)));
    }
}

public class UserInputMultiplyOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly IOperationInstance<int> _input;

    public UserInputMultiplyOperation(NumberPipelineExecutionContext context, IOperationInstance<int> input)
        : base(context)
    {
        _context = context;
        _input = input;
    }

    protected override async Task<IOperationResult<int>> ExecuteCoreAsync(CancellationToken cancellationToken)
    {
        var toMultiply = await _input.Promise().WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Console input cannot be interrupted; check cancellation again after the read.
        var multiplyBy = _context.PromptProvider.PromptUserForIntInput("Enter an integer to multiply by:");
        cancellationToken.ThrowIfCancellationRequested();
        var product = toMultiply.GetResult() * multiplyBy;

        _context.PromptProvider.InformUser($"Input was: {toMultiply.GetResult()} and result is {product}.");
        return new ImmediateResult<int>(product);
    }
}