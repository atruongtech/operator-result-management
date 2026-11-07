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
var process = new ProcessBase<NumberPipelineExecutionContext>();
process.Add(inputInstruction);
var multiply = process.Append(inputInstruction,
    source => new UserInputMultiplyInstruction(source));
var factor = process.Append(multiply, source => new FactorInstruction(source));
process.Append(factor, source => new MinMaxDifferenceInstruction(source));

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    var context = new NumberPipelineExecutionContext(new(), cancellation.Token);
    context.Register(process);
    await context.ExecuteAsync();

    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Execution canceled.");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
