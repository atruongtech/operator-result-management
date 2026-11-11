using operator_result_management;
using operator_result_management.Basic;

namespace drink_ordering_poc;

public sealed class OrangeJuiceProcess : ProcessBase<DrinkOrderExecutionContext>
{
    public InstructionBase<OrangeJuice, DrinkOrderExecutionContext> Result { get; }

    public OrangeJuiceProcess()
    {
        var order = Add(new TakeOrangeJuiceOrderInstruction());
        var needsRefill = Add(new CheckHopperInstruction(order));

        var refillBody = new ProcessBase<DrinkOrderExecutionContext>();
        refillBody.Add(new FillHopperInstruction());
        var refill = Add(new ConditionalInstruction<DrinkOrderExecutionContext>(
            needsRefill, refillBody, parent => parent.CreateChild()));

        var pulpChoice = Add(new ChoosePulpInstruction(refill));
        var produce = Add(new ProduceOrangeJuiceInstruction(order, pulpChoice));
        Result = Add(new ReturnOrangeJuiceInstruction(produce));
    }
}

public sealed class TakeOrangeJuiceOrderInstruction : InstructionBase<OrangeJuiceSize, DrinkOrderExecutionContext>
{
    protected override OperationInstanceBase<OrangeJuiceSize> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context);

    private sealed class Operation(DrinkOrderExecutionContext context)
        : OperationInstanceBase<OrangeJuiceSize>(context)
    {
        protected override Task<IOperationResult<OrangeJuiceSize>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            int choice = context.Console.ReadChoice("Orange juice size:", ["Small (1 orange)", "Medium (2 oranges)", "Large (3 oranges)"], cancellationToken);
            return Task.FromResult<IOperationResult<OrangeJuiceSize>>(
                new ImmediateResult<OrangeJuiceSize>((OrangeJuiceSize)choice));
        }
    }
}

public sealed class CheckHopperInstruction(InstructionBase<OrangeJuiceSize, DrinkOrderExecutionContext> order)
    : InstructionBase<bool, DrinkOrderExecutionContext>(order)
{
    protected override OperationInstanceBase<bool> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(order));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<OrangeJuiceSize> order)
        : OperationInstanceBase<bool>(context)
    {
        protected override async Task<IOperationResult<bool>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var size = (await order.Promise().WaitAsync(cancellationToken)).GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            bool needsRefill = context.OrangeJuiceMachine.NeedsRefill(size);
            context.Console.WriteLine($"Hopper: {context.OrangeJuiceMachine.OrangesRemaining} oranges. " +
                (needsRefill ? "Refill needed." : "Enough oranges; no refill."));
            return new ImmediateResult<bool>(needsRefill);
        }
    }
}

public sealed class FillHopperInstruction : InstructionBase<DrinkOrderExecutionContext>
{
    protected override OperationInstanceBase CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context);

    private sealed class Operation(DrinkOrderExecutionContext context) : OperationInstanceBase(context)
    {
        protected override Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.OrangeJuiceMachine.FillHopper();
            context.Console.WriteLine($"Hopper filled to {OrangeJuiceMachine.HopperCapacity} oranges.");
            return Task.CompletedTask;
        }
    }
}

public sealed class ChoosePulpInstruction(InstructionBase<DrinkOrderExecutionContext> refill)
    : InstructionBase<DrinkOrderExecutionContext>(refill)
{
    protected override OperationInstanceBase CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(refill));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase refill)
        : OperationInstanceBase(context)
    {
        protected override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            await refill.Completion.WaitAsync(cancellationToken);
            int choice = context.Console.ReadChoice(
                $"Orange juice pulp (machine currently {(context.OrangeJuiceMachine.IncludePulp ? "pulp" : "no pulp")}):",
                ["Pulp", "No pulp"], cancellationToken);
            context.OrangeJuiceMachine.IncludePulp = choice == 1;
            context.Console.WriteLine($"Machine set to {(context.OrangeJuiceMachine.IncludePulp ? "pulp" : "no pulp")}.");
        }
    }
}

public sealed class ProduceOrangeJuiceInstruction(
    InstructionBase<OrangeJuiceSize, DrinkOrderExecutionContext> order,
    InstructionBase<DrinkOrderExecutionContext> pulpChoice)
    : InstructionBase<OrangeJuice, DrinkOrderExecutionContext>(order, pulpChoice)
{
    protected override OperationInstanceBase<OrangeJuice> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(order), inputs.GetOperation(pulpChoice));

    private sealed class Operation(
        DrinkOrderExecutionContext context,
        OperationInstanceBase<OrangeJuiceSize> order,
        OperationInstanceBase pulpChoice) : OperationInstanceBase<OrangeJuice>(context)
    {
        protected override async Task<IOperationResult<OrangeJuice>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var size = (await order.Promise().WaitAsync(cancellationToken)).GetResult();
            await pulpChoice.Completion.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var juice = context.OrangeJuiceMachine.Produce(size);
            context.Console.WriteLine($"Produced {size.ToString().ToLowerInvariant()} orange juice {(juice.HasPulp ? "with pulp" : "without pulp")}. " +
                $"Hopper: {context.OrangeJuiceMachine.OrangesRemaining} oranges remaining.");
            return new ImmediateResult<OrangeJuice>(juice);
        }
    }
}

public sealed class ReturnOrangeJuiceInstruction(InstructionBase<OrangeJuice, DrinkOrderExecutionContext> juice)
    : InstructionBase<OrangeJuice, DrinkOrderExecutionContext>(juice)
{
    protected override OperationInstanceBase<OrangeJuice> CreateOperation(
        DrinkOrderExecutionContext context, InstructionInputs<DrinkOrderExecutionContext> inputs) =>
        new Operation(context, inputs.GetOperation(juice));

    private sealed class Operation(DrinkOrderExecutionContext context, OperationInstanceBase<OrangeJuice> juice)
        : OperationInstanceBase<OrangeJuice>(context)
    {
        protected override async Task<IOperationResult<OrangeJuice>> ExecuteResultAsync(CancellationToken cancellationToken)
        {
            var result = await juice.Promise().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var serving = result.GetResult();
            context.Console.WriteLine($"Order up: {serving.Size.ToString().ToLowerInvariant()} orange juice " +
                $"{(serving.HasPulp ? "with pulp" : "without pulp")}!");
            return result;
        }
    }
}
