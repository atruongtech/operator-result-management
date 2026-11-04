using configurable_number_pipeline_poc;

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
var procedure = new FactorInstruction(inputInstruction);
var context = new operator_result_management.ExecutionContext();
var operation = (FactorOperation)procedure.GetOperation(context);

await operation.Execute();
var factors = (await operation.Promise()).GetResult();

Console.WriteLine(factors.Count == 0
    ? $"{input} has no prime factors."
    : $"Prime factors of {input}: {string.Join(", ", factors)}");

return 0;
