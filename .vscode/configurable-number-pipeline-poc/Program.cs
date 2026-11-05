using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management;

int input;

while (true)
{
    Console.Write("Enter a positive integer: ");
    var line = Console.ReadLine();

    if (line is null)
    {
        Console.Error.WriteLine("No input received.");
        return 1;
    }

    if (int.TryParse(line, out input) && input > 0)
        break;

    Console.Error.WriteLine("Input must be a positive 32-bit integer.");
}

var inputInstruction = new DirectInputInstruction(input);
var multiply = new UserInputMultiplyInstruction(inputInstruction);
var factor = new FactorInstruction(multiply);

List<IInstruction<NumberPipelineExecutionContext>> process = [inputInstruction, multiply, factor];
var context = new NumberPipelineExecutionContext(new());
var operations = process.Select(i => i.GetOperation(context)).ToList();

await Task.WhenAll(operations.Select(o => o.Execute()));

var factors = (await factor.GetTypedOperation(context).Promise()).GetResult();
Console.WriteLine(factors.Count == 0
    ? $"{input} has no prime factors."
    : $"Prime factors: {string.Join(", ", factors)}");

return 0;
