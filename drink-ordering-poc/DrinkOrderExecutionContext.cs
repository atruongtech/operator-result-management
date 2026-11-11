using operator_result_management;

namespace drink_ordering_poc;

public sealed class DrinkOrderExecutionContext(
    OrderConsole console,
    OrangeJuiceMachine orangeJuiceMachine,
    CoffeeMachine coffeeMachine,
    CancellationToken cancellationToken = default) : ExecutionContextBase(cancellationToken)
{
    public OrderConsole Console { get; } = console;
    public OrangeJuiceMachine OrangeJuiceMachine { get; } = orangeJuiceMachine;
    public CoffeeMachine CoffeeMachine { get; } = coffeeMachine;

    public DrinkOrderExecutionContext CreateChild() =>
        new(Console, OrangeJuiceMachine, CoffeeMachine, CancellationToken);
}
