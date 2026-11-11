using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class FactorInstruction : InstructionBase<ImmutableList<int>, NumberPipelineExecutionContext>
{
    private readonly InstructionBase<int, NumberPipelineExecutionContext> _input;

    public FactorInstruction(InstructionBase<int, NumberPipelineExecutionContext> input) : base(input)
    {
        _input = input;
    }

    protected override OperationInstanceBase<ImmutableList<int>> CreateOperation(
        NumberPipelineExecutionContext context, InstructionInputs<NumberPipelineExecutionContext> inputs) =>
        new FactorOperation(context, inputs.GetOperation(_input));
}

public class FactorOperation : OperationInstanceBase<ImmutableList<int>>
{
    private readonly NumberPipelineExecutionContext _context;
    private readonly OperationInstanceBase<int> _input;

    public FactorOperation(NumberPipelineExecutionContext context, OperationInstanceBase<int> input) : base(context)
    {
        _context = context;
        _input = input;
    }

    protected override async Task<IOperationResult<ImmutableList<int>>> ExecuteResultAsync(
        CancellationToken cancellationToken)
    {
        var result = await _input.Promise().WaitAsync(cancellationToken);
        var input = result.GetResult();
        var factorResult = GetPrimeFactors(input, cancellationToken);
        var factors = factorResult.GetResult();
        cancellationToken.ThrowIfCancellationRequested();
        _context.PromptProvider.InformUser(factors.Count == 0
            ? $"{input} has no prime factors."
            : $"Prime factors of {input}: {string.Join(", ", factors)}");
        return factorResult;
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