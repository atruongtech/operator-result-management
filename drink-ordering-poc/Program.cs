using drink_ordering_poc;
using operator_result_management;

var console = new OrderConsole(Console.In, Console.Out);
var orangeJuiceMachine = new OrangeJuiceMachine();
var coffeeMachine = new CoffeeMachine();
var orangeJuiceProcess = new OrangeJuiceProcess();
var coffeeProcess = new CoffeeProcess();

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    while (true)
    {
        console.WriteLine($"\nMachines: {orangeJuiceMachine.OrangesRemaining}/{OrangeJuiceMachine.HopperCapacity} oranges; " +
            $"pulp setting: {(orangeJuiceMachine.IncludePulp ? "pulp" : "no pulp")}; " +
            $"{coffeeMachine.OuncesRemaining} oz coffee.");
        int choice = console.ReadChoice("Choose a workflow:", ["Order orange juice", "Order coffee", "Exit"], cancellation.Token);
        if (choice == 3)
            return 0;

        var context = new DrinkOrderExecutionContext(console, orangeJuiceMachine, coffeeMachine, cancellation.Token);
        if (choice == 1)
        {
            context.Register(orangeJuiceProcess);
            var result = context.Register(orangeJuiceProcess.Result);
            await context.ExecuteAsync();
            _ = (await result.Promise()).GetResult();
        }
        else
        {
            context.Register(coffeeProcess);
            var result = context.Register(coffeeProcess.Result);
            await context.ExecuteAsync();
            _ = (await result.Promise()).GetResult();
        }
    }
}
catch (EndOfStreamException)
{
    console.WriteLine("Input closed.");
    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.Error.WriteLine("Order canceled.");
    return 1;
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
