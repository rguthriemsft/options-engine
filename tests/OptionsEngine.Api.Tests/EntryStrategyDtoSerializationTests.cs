using System.Text.Json;
using OptionsEngine.Api.EntryStrategy;

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
}
