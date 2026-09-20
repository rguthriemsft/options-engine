using OptionsEngine.Application.PositionSizing;
using OptionsEngine.Strategy.Indicators;
using OptionsEngine.Strategy.PositionSizing;

namespace OptionsEngine.Api.PositionSizing;

internal static class PositionSizingApiConfiguration
{
    public static PositionSizingOrchestrationConfiguration Load(IConfiguration configuration,
        IndicatorConfiguration indicatorConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(indicatorConfiguration);
        var version = configuration.GetSection("PositionSizing")["StrategyVersion"];
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("PositionSizing:StrategyVersion is required.");

        var result = new PositionSizingOrchestrationConfiguration
        {
            PositionSizingConfiguration = new PositionSizingConfiguration { Version = indicatorConfiguration.Version },
            StrategyVersion = new PositionSizingStrategyVersion(version)
        };
        result.Validate();
        return result;
    }
}
