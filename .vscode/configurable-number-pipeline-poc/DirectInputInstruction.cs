using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public sealed class DirectInputInstruction : IInstruction<int, NumberPipelineExecutionContext>
{
    private readonly int _input;

    public Guid Id { get; } = Guid.NewGuid();

    public DirectInputInstruction(int input)
    {
        _input = input;
    }

    public int Input => _input;

    public IOperationInstance<int> GetTypedOperation(NumberPipelineExecutionContext context)
    {
        if (context.Operations.TryGetValue(this, out var existing) &&
            existing is IExecutionContextContainer<DirectInputOperation> input)
            return input.Value;

        var operation = new DirectInputOperation(_input);
        context.AddOperation(this, new ExecutionContextContainer<DirectInputOperation>(operation));
        return operation;
    }

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context)
    {
        return GetTypedOperation(context);
    }
}

public sealed class DirectInputOperation : OperationInstanceBase<int>
{
    public DirectInputOperation(int input)
    {
        Fulfill(new ImmediateResult<int>(input));
    }

    public override Task Execute()
    {
        return Task.CompletedTask;
    }
}
