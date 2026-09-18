using System.Globalization;
using OptionsEngine.Application.Indicators;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Indicators;

public static class IndicatorEndpoints
{
    public static IEndpointRouteBuilder MapIndicatorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/indicators/{symbol}", GetAsync);
        return endpoints;
    }

    private static async Task<IResult> GetAsync(string symbol, HttpContext context,
        IndicatorOrchestrationService service, IndicatorConfiguration configuration,
        IndicatorCalculationVersion calculationVersion, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Trim().Any(char.IsWhiteSpace))
            return Validation("symbol", "A non-empty market symbol without whitespace is required.");

        var query = context.Request.Query;
        if (query.Keys.Any(key => key.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                                  key.Equals("calculationVersion", StringComparison.OrdinalIgnoreCase) ||
                                  key.Equals("indicatorCalculationVersion", StringComparison.OrdinalIgnoreCase) ||
                                  key.Equals("configurationVersion", StringComparison.OrdinalIgnoreCase)))
            return Validation("version", "Indicator versions are selected by the server.");

        DateOnly? asOfDate = null;
        if (query.TryGetValue("asOf", out var values))
        {
            if (values.Count != 1 || !DateOnly.TryParseExact(values[0], "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedDate))
                return Validation("asOf", "Use one asOf date in YYYY-MM-DD format.");
            asOfDate = requestedDate;
        }

        var now = clock.GetUtcNow().ToUniversalTime();
        if (asOfDate is null)
        {
            var boundary = DateOnly.FromDateTime(now.UtcDateTime);
            asOfDate = await service.ResolveLatestAsOfDateAsync(symbol, boundary, cancellationToken);
            if (asOfDate is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "HistoricalDataUnavailable", detail: "No applicable persisted trading observation exists for the symbol.");
        }

        var snapshot = await service.CalculateAndPersistAsync(symbol, asOfDate.Value, configuration,
            calculationVersion, now, cancellationToken);
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("OptionsEngine.Api.Indicators")
            .LogInformation("Calculated indicator snapshot for {Symbol} as of {AsOfDate} using {CalculationVersion} and configuration {ConfigurationVersion}",
                snapshot.Symbol, snapshot.AsOfDate, snapshot.IndicatorCalculationVersion.Value, snapshot.ConfigurationVersion.Value);
        return Results.Ok(IndicatorSnapshotResponse.From(snapshot));
    }

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
