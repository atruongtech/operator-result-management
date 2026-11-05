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
var multiply = new UserInputMultiplyInstruction(inputInstruction);
var factor = new FactorInstruction(multiply);

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
    factor.GetTypedOperation(context);
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
