using System.Collections.Immutable;
using configurable_number_pipeline_poc;
using operator_result_management;
using operator_result_management.Basic;

ImmutableList<InstructionFactory<NumberPipelineExecutionContext>> factories =
[
    DirectInputInstructionFactory.Create(),
    MinMaxDifferenceInstructionFactory.Create(),
    UserInputMultiplyInstructionFactory.Create(),
    FactorInstructionFactory.Create(),
    DistributeInstructionFactory.Create(),
];
var process = new ProcessBase<NumberPipelineExecutionContext>();
var inputInstruction = process.Add(new DirectInputInstruction());
var multiply = process.Add(new UserInputMultiplyInstruction(inputInstruction));
var factor = process.Add(new FactorInstruction(multiply));
process.Add(new MinMaxDifferenceInstruction(factor));

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
