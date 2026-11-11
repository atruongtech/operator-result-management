using operator_result_management;
using operator_result_management.Basic;

namespace drink_ordering_poc;

public sealed class CoffeeProcess : ProcessBase<DrinkOrderExecutionContext>
{
    public InstructionBase<Coffee, DrinkOrderExecutionContext> Result { get; }

    public CoffeeProcess()
    {
        var order = Add(new TakeCoffeeOrderInstruction());
        var needsBrew = Add(new CheckCarafeInstruction(order));

        var brewBody = new ProcessBase<DrinkOrderExecutionContext>();
        brewBody.Add(new BrewCoffeeInstruction());
        var brew = Add(new ConditionalInstruction<DrinkOrderExecutionContext>(
            needsBrew, brewBody, parent => parent.CreateChild()));

        var pour = Add(new PourCoffeeInstruction(order, brew));
        var cream = Add(new AddCreamInstruction(pour));
        var sugar = Add(new AddSugarInstruction(cream));
        Result = Add(new ReturnCoffeeInstruction(sugar));
    }
}

public sealed class TakeCoffeeOrderInstruction : InstructionBase<CoffeeSize, DrinkOrderExecutionContext>
{
    protected override OperationInstanceBase<CoffeeSize> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context);

    private sealed class Operation(DrinkOrderExecutionContext context) : OperationInstanceBase<CoffeeSize>(context)
    {
        protected override Task<IOperationResult<CoffeeSize>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            int choice = context.Console.ReadChoice("Coffee size:", ["Small (4 oz)", "Large (8 oz)"], cancellationToken);
            var size = choice == 1 ? CoffeeSize.Small : CoffeeSize.Large;
            return Task.FromResult<IOperationResult<CoffeeSize>>(new ImmediateResult<CoffeeSize>(size));
        }
    }
}

public sealed class CheckCarafeInstruction(InstructionBase<CoffeeSize, DrinkOrderExecutionContext> order)
    : InstructionBase<bool, DrinkOrderExecutionContext>(order)
{
    protected override OperationInstanceBase<bool> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(order));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<CoffeeSize> order)
        : OperationInstanceBase<bool>(context)
    {
        protected override async Task<IOperationResult<bool>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var size = (await order.Promise().WaitAsync(cancellationToken)).GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            bool needsBrew = context.CoffeeMachine.NeedsBrew(size);
            context.Console.WriteLine($"Carafe: {context.CoffeeMachine.OuncesRemaining} oz. " +
                (needsBrew ? "Brew needed." : "Enough coffee; no brew."));
            return new ImmediateResult<bool>(needsBrew);
        }
    }
}

public sealed class BrewCoffeeInstruction : InstructionBase<DrinkOrderExecutionContext>
{
    protected override OperationInstanceBase CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context);

    private sealed class Operation(DrinkOrderExecutionContext context) : OperationInstanceBase(context)
    {
        protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            context.Console.WriteLine($"Brewing {CoffeeMachine.BatchOunces} oz of coffee (3 seconds)...");
            await context.CoffeeMachine.BrewAsync(cancellationToken);
            context.Console.WriteLine($"Brew complete. Carafe: {context.CoffeeMachine.OuncesRemaining} oz.");
        }
    }
}

public sealed class PourCoffeeInstruction(
    InstructionBase<CoffeeSize, DrinkOrderExecutionContext> order,
    InstructionBase<DrinkOrderExecutionContext> brew)
    : InstructionBase<Coffee, DrinkOrderExecutionContext>(order, brew)
{
    protected override OperationInstanceBase<Coffee> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(order), inputs.GetOperation(brew));

    private sealed class Operation(
        DrinkOrderExecutionContext context,
        OperationInstanceBase<CoffeeSize> order,
        OperationInstanceBase brew) : OperationInstanceBase<Coffee>(context)
    {
        protected override async Task<IOperationResult<Coffee>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var size = (await order.Promise().WaitAsync(cancellationToken)).GetResult();
            await brew.Completion.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var coffee = context.CoffeeMachine.Pour(size);
            context.Console.WriteLine($"Poured {coffee.Ounces} oz. Carafe: {context.CoffeeMachine.OuncesRemaining} oz remaining.");
            return new ImmediateResult<Coffee>(coffee);
        }
    }
}

public sealed class AddCreamInstruction(InstructionBase<Coffee, DrinkOrderExecutionContext> coffee)
    : InstructionBase<Coffee, DrinkOrderExecutionContext>(coffee)
{
    protected override OperationInstanceBase<Coffee> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(coffee));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<Coffee> coffee)
        : OperationInstanceBase<Coffee>(context)
    {
        protected override async Task<IOperationResult<Coffee>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var serving = (await coffee.Promise().WaitAsync(cancellationToken)).GetResult();
            int count = context.Console.ReadNonNegativeInt("How many cream would you like? (0 for none)", cancellationToken);
            context.Console.WriteLine($"Added {count} cream.");
            return new ImmediateResult<Coffee>(serving with { CreamCount = count });
        }
    }
}

public sealed class AddSugarInstruction(InstructionBase<Coffee, DrinkOrderExecutionContext> coffee)
    : InstructionBase<Coffee, DrinkOrderExecutionContext>(coffee)
{
    protected override OperationInstanceBase<Coffee> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(coffee));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<Coffee> coffee)
        : OperationInstanceBase<Coffee>(context)
    {
        protected override async Task<IOperationResult<Coffee>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var serving = (await coffee.Promise().WaitAsync(cancellationToken)).GetResult();
            int count = context.Console.ReadNonNegativeInt("How many sugar would you like? (0 for none)", cancellationToken);
            context.Console.WriteLine($"Added {count} sugar.");
            return new ImmediateResult<Coffee>(serving with { SugarCount = count });
        }
    }
}

public sealed class ReturnCoffeeInstruction(InstructionBase<Coffee, DrinkOrderExecutionContext> coffee)
    : InstructionBase<Coffee, DrinkOrderExecutionContext>(coffee)
{
    protected override OperationInstanceBase<Coffee> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(coffee));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<Coffee> coffee)
        : OperationInstanceBase<Coffee>(context)
    {
        protected override async Task<IOperationResult<Coffee>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var result = await coffee.Promise().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var serving = result.GetResult();
            context.Console.WriteLine($"Order up: {serving.Size.ToString().ToLowerInvariant()} coffee " +
                $"({serving.Ounces} oz, {serving.CreamCount} cream, {serving.SugarCount} sugar)!");
            return result;
        }
    }
}
