using configurable_number_pipeline_poc;
using operator_result_management;

var inputInstruction = new DirectInputInstruction();
var process = new ProcessBase<NumberPipelineExecutionContext>();
process.Add(inputInstruction);
var multiply = process.Append(inputInstruction,
    source => new UserInputMultiplyInstruction(source));
var factor = process.Append(multiply, source => new FactorInstruction(source));
process.Append(factor, source => new MinMaxDifferenceInstruction(source));

InputScripts.OutputExistingProcess(process);
while (InputScripts.PromptUserForAction() == ConfigurePipelineUserAction.Edit)
{
    InputScripts.OutputExistingProcess(process);    
}

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

