using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class FactorInstruction : IInstruction<ImmutableList<int>, NumberPipelineExecutionContext>
{
    private readonly IInstruction<int, NumberPipelineExecutionContext> _input;

    public Guid Id {get;}

    public FactorInstruction(IInstruction<int, NumberPipelineExecutionContext> input)
    {
        Id = Guid.NewGuid();
        _input = input;
    }

    public IOperationInstance<ImmutableList<int>> GetTypedOperation(NumberPipelineExecutionContext context)
    {
        return context.GetOrAddOperation(this,
            () => new FactorOperation(context, _input.GetTypedOperation(context)));
    }

    public IOperationInstance GetOperation(NumberPipelineExecutionContext context) => GetTypedOperation(context);
}

public class FactorOperation : OperationInstanceBase<ImmutableList<int>>
{
    private readonly IOperationInstance<int> _input;

    public FactorOperation(IExecutionContext context, IOperationInstance<int> input) : base(context)
    {
        _input = input;
    }

    protected override async Task<IOperationResult<ImmutableList<int>>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var result = await _input.Promise().WaitAsync(cancellationToken);
        var input = result.GetResult();
        return GetPrimeFactors(input, cancellationToken);
    }

    private IOperationResult<ImmutableList<int>> GetPrimeFactors(int number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (number < 1)
            throw new ArgumentOutOfRangeException(
                nameof(number), "Number must be positive.");

        var factors = new List<int>();

        while (number % 2 == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            factors.Add(2);
            number /= 2;
        }

        // Division avoids overflow from divisor * divisor.
        for (int divisor = 3; divisor <= number / divisor; divisor += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (number % divisor == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                factors.Add(divisor);
                number /= divisor;
            }
        }

        if (number > 1)
            factors.Add(number);

        return new ImmediateResult<ImmutableList<int>>([.. factors]);
    }
}