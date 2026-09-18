namespace OptionsEngine.Application.EntryStrategy;

/// <summary>Validated, provider-independent current-evaluation earnings calendar configuration.</summary>
public sealed record EarningsCalendarConfiguration
{
    public Dictionary<string, string[]> Symbols { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> ParseAndValidate()
    {
        ArgumentNullException.ThrowIfNull(Symbols);
        var result = new Dictionary<string, IReadOnlyList<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (symbol, configuredDates) in Symbols)
        {
            var normalized = NormalizeSymbol(symbol);
            if (configuredDates is null)
                throw new ArgumentException($"Earnings dates for '{normalized}' cannot be null.", nameof(Symbols));

            var parsed = new SortedSet<DateOnly>();
            foreach (var configuredDate in configuredDates)
            {
                if (!DateOnly.TryParseExact(configuredDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                    throw new ArgumentException($"Earnings date '{configuredDate}' for '{normalized}' must use yyyy-MM-dd.", nameof(Symbols));
                parsed.Add(date);
            }

            if (!result.TryAdd(normalized, parsed.ToArray()))
                throw new ArgumentException($"Earnings calendar contains duplicate symbol '{normalized}'.", nameof(Symbols));
        }
        return result;
    }

    private static string NormalizeSymbol(string symbol) => string.IsNullOrWhiteSpace(symbol)
        ? throw new ArgumentException("An earnings-calendar symbol is required.", nameof(symbol))
        : symbol.Trim().ToUpperInvariant();
}

/// <summary>
/// Supplies the earliest configured current/future earnings date. This is a V1 current-evaluation source,
/// not a reconstruction of historically-known corporate-event data.
/// </summary>
public sealed class ConfiguredEarningsDateSource : IEarningsDateSource
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private readonly IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> datesBySymbol;

    public ConfiguredEarningsDateSource(EarningsCalendarConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        datesBySymbol = configuration.ParseAndValidate();
    }

    public Task<DateOnly?> GetNextEarningsDateAsync(string symbol, DateTimeOffset evaluationTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (evaluationTimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluationTimestampUtc));
        var normalized = NormalizeSymbol(symbol);
        var evaluationDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(evaluationTimestampUtc, NewYork).DateTime);
        if (!datesBySymbol.TryGetValue(normalized, out var dates)) return Task.FromResult<DateOnly?>(null);
        foreach (var date in dates)
        {
            if (date >= evaluationDate) return Task.FromResult<DateOnly?>(date);
        }
        return Task.FromResult<DateOnly?>(null);
    }

    private static string NormalizeSymbol(string symbol) => string.IsNullOrWhiteSpace(symbol)
        ? throw new ArgumentException("A symbol is required.", nameof(symbol)) : symbol.Trim().ToUpperInvariant();
}
