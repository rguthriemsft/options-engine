using System.Text.Json;
using OptionsEngine.Api.EntryStrategy;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Tests;

public sealed class EntryStrategyDtoSerializationTests
{
    [Fact]
    public void PhaseFourOptionDtoUsesStringTypeAndPreservesNulls()
    {
        var option = new OptionResponse("A", "MSFT", DateTimeOffset.Parse("2026-01-02T15:00:00Z"),
            new DateOnly(2026, 2, 20), 410m, "Call", null, 1.25m, null, null, 120L,
            null, null, null, 400m, "test");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(option, options));
        Assert.Equal("Call", json.RootElement.GetProperty("optionType").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("bid").ValueKind);
        Assert.Equal("2026-02-20", json.RootElement.GetProperty("expiration").GetString());
    }

    [Fact]
    public void IndicatorValueDtoPreservesUnavailableValueAsNull()
    {
        var value = new IndicatorValueResponse<double>(null, "InsufficientData");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value, options));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("value").ValueKind);
        Assert.Equal("InsufficientData", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void PhaseFourDispositionAndMissingInputCodesSerializeAsStrings()
    {
        var response = new EntryStrategyEvaluationResponse(Guid.NewGuid(), Guid.NewGuid(), "MSFT",
            new DateOnly(2026, 1, 2), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "v1", 1, "1.0.0",
            null!, null!, null!, ConfigurationResponse.From(new EntryStrategyConfiguration { Version = new ConfigurationVersion(1) }), null,
            [], [], [], [], true, "MSFT260220C00410000", 410m, new DateOnly(2026, 2, 20), 2.35m,
            "EntryCandidate", ["Rsi14"], []);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("dispositionReason").ValueKind);
        Assert.Equal("EntryCandidate", json.RootElement.GetProperty("dispositionReason").GetString());
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("missingInputs")[0].ValueKind);
        Assert.Equal("Rsi14", json.RootElement.GetProperty("missingInputs")[0].GetString());
    }

    [Fact]
    public void HistoryCodesSerializeAsStrings()
    {
        var history = new EntryStrategyEvaluationHistoryResponse(Guid.NewGuid(), Guid.NewGuid(), "MSFT",
            new DateOnly(2026, 1, 2), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Available", 80, "Strong",
            true, "OPT", 410m, new DateOnly(2026, 2, 20), "EntryCandidate", "v1", 1, "1.0.0");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(history, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("ccosStatus").ValueKind);
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("dispositionReason").ValueKind);
    }
}
