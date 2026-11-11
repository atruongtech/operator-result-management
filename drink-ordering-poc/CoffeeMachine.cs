namespace drink_ordering_poc;

public enum CoffeeSize
{
    Small = 4,
    Large = 8
}

public sealed record Coffee(CoffeeSize Size, int CreamCount = 0, int SugarCount = 0)
{
    public int Ounces => CoffeeMachine.OuncesRequired(Size);
}

public sealed class CoffeeMachine
{
    public const int BatchOunces = 12;
    public static readonly TimeSpan BrewDuration = TimeSpan.FromSeconds(3);

    public int OuncesRemaining { get; private set; }

    public bool NeedsBrew(CoffeeSize size) => OuncesRemaining < OuncesRequired(size);

    public async Task BrewAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(BrewDuration, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        OuncesRemaining += BatchOunces;
    }

    public Coffee Pour(CoffeeSize size)
    {
        int required = OuncesRequired(size);
        if (OuncesRemaining < required)
            throw new InvalidOperationException("The carafe does not contain enough coffee.");

        OuncesRemaining -= required;
        return new Coffee(size);
    }

    internal static int OuncesRequired(CoffeeSize size) => size switch
    {
        CoffeeSize.Small => 4,
        CoffeeSize.Large => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown coffee size.")
    };
}
