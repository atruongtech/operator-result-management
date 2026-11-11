using System;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class UserInputMultiplyInstruction : InstructionBase<int, NumberPipelineExecutionContext>
{
    private readonly InstructionBase<int, NumberPipelineExecutionContext> _input;

    public UserInputMultiplyInstruction(InstructionBase<int, NumberPipelineExecutionContext> input) : base(input)
    {
        _input = input;
    }

    protected override OperationInstanceBase<int> CreateOperation(
        NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
        new UserInputMultiplyOperation(context, inputs.GetOperation(_input));
}

public class UserInputMultiplyOperation : OperationInstanceBase<int>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly OperationInstanceBase<int> _input;

    public UserInputMultiplyOperation(NumberPipelineExecutionContext context, OperationInstanceBase<int> input)
        : base(context)
    {
        _context = context;
        _input = input;
    }

    protected override async Task<IOperationResult<int>> ExecuteResultAsync(CancellationToken cancellationToken)
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