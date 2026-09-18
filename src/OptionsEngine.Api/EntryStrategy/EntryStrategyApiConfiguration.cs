using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.EntryStrategy;

internal static class EntryStrategyApiConfiguration
{
    public static EntryStrategyOrchestrationConfiguration Load(IConfiguration configuration,
        IndicatorConfiguration indicatorConfiguration, IndicatorCalculationVersion indicatorCalculationVersion)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(indicatorConfiguration);
        ArgumentNullException.ThrowIfNull(indicatorCalculationVersion);
        var section = configuration.GetSection("EntryStrategy");
        var strategyVersion = section["StrategyVersion"];
        if (string.IsNullOrWhiteSpace(strategyVersion))
            throw new InvalidOperationException("EntryStrategy:StrategyVersion is required.");

        var strategyConfiguration = new EntryStrategyConfiguration
        {
            Version = indicatorConfiguration.Version,
            Ccos = section.GetSection("Ccos").Get<CcosConfiguration>() ?? new(),
            ContractScore = section.GetSection("ContractScore").Get<ContractScoreConfiguration>() ?? new(),
            ContractEligibility = section.GetSection("ContractEligibility").Get<ContractEligibilityConfiguration>() ?? new()
        };
        var result = new EntryStrategyOrchestrationConfiguration
        {
            StrategyConfiguration = strategyConfiguration,
            IndicatorConfiguration = indicatorConfiguration,
            IndicatorCalculationVersion = indicatorCalculationVersion,
            StrategyVersion = new StrategyVersion(strategyVersion)
        };
        result.Validate();
        return result;
    }

    public static EarningsCalendarConfiguration LoadEarningsCalendar(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var dates = configuration.GetSection("EarningsCalendar").Get<Dictionary<string, string[]>>() ?? new();
        var result = new EarningsCalendarConfiguration { Symbols = dates };
        result.ParseAndValidate();
        return result;
    }
}
