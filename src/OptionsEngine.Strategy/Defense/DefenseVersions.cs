namespace OptionsEngine.Strategy.Defense;

public sealed record DefenseStrategyVersion
{
    public DefenseStrategyVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A Defense strategy version is required.", nameof(value));
        Value = value;
    }
    public string Value { get; }
}

public sealed record RollStrategyVersion
{
    public RollStrategyVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A Roll strategy version is required.", nameof(value));
        Value = value;
    }
    public string Value { get; }
}
