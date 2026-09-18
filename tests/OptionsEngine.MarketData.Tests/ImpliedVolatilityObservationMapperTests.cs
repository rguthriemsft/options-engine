using OptionsEngine.Application.MarketData;
using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.MarketData.Tests;

public sealed class ImpliedVolatilityObservationMapperTests
{
    [Fact]
    public void PreservesNormalizedChainIvPriceTypeAndTimestampWithoutLiquidityFiltering()
    {
        var observedAt = new DateTimeOffset(2026, 1, 5, 20, 0, 0, TimeSpan.Zero);
        var expiration = new DateOnly(2026, 2, 4);
        var contracts = new[]
        {
            Contract("C", OptionType.Call, 0.20, observedAt, expiration),
            Contract("P", OptionType.Put, 0.40, observedAt, expiration)
        };
        var chain = new OptionChain("MSFT", expiration, observedAt, contracts, "ExampleProvider");

        var mapped = ImpliedVolatilityObservationMapper.Map(chain);
        var result = new ImpliedVolatilityContextCalculator().Calculate(new ImpliedVolatilityCalculationRequest(
            "MSFT", new DateOnly(2026, 1, 5), [mapped], [],
            new IndicatorConfiguration { Version = new ConfigurationVersion(1) },
            new IndicatorCalculationVersion("3.0.0-iv.1"), observedAt.AddMinutes(1)));

        Assert.Equal(observedAt, mapped.ObservedAt);
        Assert.Equal(2, mapped.Contracts.Count);
        Assert.Equal(IvOptionType.Call, mapped.Contracts[0].OptionType);
        Assert.Equal(IvOptionType.Put, mapped.Contracts[1].OptionType);
        Assert.All(mapped.Contracts, contract => Assert.Equal(100m, contract.UnderlyingPrice));
        Assert.InRange(result.Iv30.Value!.Value, 0.30 - 1e-10, 0.30 + 1e-10);
    }

    [Fact]
    public void RejectsContractsFromAnotherSnapshot()
    {
        var observedAt = new DateTimeOffset(2026, 1, 5, 20, 0, 0, TimeSpan.Zero);
        var expiration = new DateOnly(2026, 2, 4);
        var contract = Contract("C", OptionType.Call, 0.20, observedAt.AddMinutes(1), expiration);
        var chain = new OptionChain("MSFT", expiration, observedAt, [contract], "ExampleProvider");

        Assert.Throws<ArgumentException>(() => ImpliedVolatilityObservationMapper.Map(chain));
    }

    private static OptionContractSnapshot Contract(string suffix, OptionType type, double iv, DateTimeOffset observedAt, DateOnly expiration) =>
        new("MSFT-" + suffix, "MSFT", observedAt, expiration, 100m, type,
            null, null, null, 0, 0, iv, null, null, null, null, 100m, "ExampleProvider");
}
