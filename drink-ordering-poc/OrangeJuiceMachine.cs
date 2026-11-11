namespace drink_ordering_poc;

public enum OrangeJuiceSize
{
    Small = 1,
    Medium = 2,
    Large = 3
}

public sealed record OrangeJuice(OrangeJuiceSize Size, bool HasPulp = true);

public sealed class OrangeJuiceMachine
{
    public const int HopperCapacity = 6;

    public int OrangesRemaining { get; private set; } = HopperCapacity;

    public bool IncludePulp { get; set; } = true;

    public bool NeedsRefill(OrangeJuiceSize size) => OrangesRemaining < OrangesRequired(size);

    public void FillHopper() => OrangesRemaining = HopperCapacity;

    public OrangeJuice Produce(OrangeJuiceSize size)
    {
        int required = OrangesRequired(size);
        if (OrangesRemaining < required)
            throw new InvalidOperationException("The hopper does not contain enough oranges.");

        OrangesRemaining -= required;
        return new OrangeJuice(size, IncludePulp);
    }

    private static int OrangesRequired(OrangeJuiceSize size) => size switch
    {
        OrangeJuiceSize.Small => 1,
        OrangeJuiceSize.Medium => 2,
        OrangeJuiceSize.Large => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown orange juice size.")
    };
}
