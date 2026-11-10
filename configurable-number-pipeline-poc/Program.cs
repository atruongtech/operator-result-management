using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management;
using operator_result_management.Basic;

ImmutableList<IInstructionFactory<NumberPipelineExecutionContext>> factories =
[
    new DirectInputInstructionFactory(),
    new MinMaxDifferenceInstructionFactory(),
    new UserInputMultiplyInstructionFactory(),
    new FactorInstructionFactory(),
    new DistributeInstructionFactory(),
];
var inputInstruction = new DirectInputInstruction();
var process = new ProcessBase<NumberPipelineExecutionContext>();
process.Add(inputInstruction);
var multiply = process.Append(inputInstruction,
    source => new UserInputMultiplyInstruction(source));
var factor = process.Append(multiply, source => new FactorInstruction(source));
process.Append(factor, source => new MinMaxDifferenceInstruction(source));

while(true)
{    
    InputScripts.OutputExistingProcess(process);
    while (InputScripts.PromptUserForAction() == ConfigurePipelineUserAction.Edit)
    {
        var inputs = InputScripts.GetInstructionSelection(process);
        var bindings = factories.Select(f => f.BindInputs(inputs)).ToImmutableList();
        var userCreated = InputScripts.SelectBinding(bindings, process).ToList();
        if (userCreated.Count == 0)
            continue;
        process.Add(userCreated.Single());
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
}
