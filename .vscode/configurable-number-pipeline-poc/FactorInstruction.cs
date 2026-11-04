using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

namespace configurable_number_pipeline_poc;

public class FactorInstruction : IInstruction<ImmutableList<int>>
{
    private readonly IInstruction<int> _input;

    public Guid Id {get;}

    public FactorInstruction(IInstruction<int> input)
    {
        Id = Guid.NewGuid();
        _input = input;
    }

    public IOperationInstance<ImmutableList<int>> GetOperation(operator_result_management.ExecutionContext context)
    {
        if (context.Operations.TryGetValue(this, out var existing) && existing is IExecutionContextContainer<FactorOperation> factor)
            return factor.Value;
        
        var newop = new FactorOperation(_input.GetOperation(context));
        context.AddOperation(this, new ExecutionContextContainer<FactorOperation>(newop));
        return newop;
    }
}

public class FactorOperation : OperationInstanceBase<ImmutableList<int>>
{
    private readonly IOperationInstance<int> _input;

    public FactorOperation(IOperationInstance<int> input)
    {
        _input = input;
    }

    public async Task Execute()
    {
        var result = await _input.Promise();
        var input = result.GetResult();
        var factors = GetPrimeFactors(input);
        Fulfill(factors);
    }

    private IOperationResult<ImmutableList<int>> GetPrimeFactors(int number)
    {
        if (number < 1)
            throw new ArgumentOutOfRangeException(
                nameof(number), "Number must be positive.");

        var factors = new List<int>();

        while (number % 2 == 0)
        {
            factors.Add(2);
            number /= 2;
        }

        // Division avoids overflow from divisor * divisor.
        for (int divisor = 3; divisor <= number / divisor; divisor += 2)
        {
            while (number % divisor == 0)
            {
                factors.Add(divisor);
                number /= divisor;
            }
        }

        if (number > 1)
            factors.Add(number);

        return new ImmediateResult<ImmutableList<int>>([.. factors]);
    }
}

// public class GenericConversionInstruction<T, U> : IInstruction<U>
// {
//     private readonly Func<T, U> _converter;

//     public Guid Id => new();

//     public GenericConversionInstruction(Func<T, U> converter)
//     {
//         _converter = converter;
//     }

//     public Task<IOperationInstance<U>> Promise(operator_result_management.ExecutionContext context)
//     {
//         throw new NotImplementedException();
//     }
// }