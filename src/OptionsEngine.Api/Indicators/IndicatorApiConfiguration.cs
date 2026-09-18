using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Indicators;

internal static class IndicatorApiConfiguration
{
    public static (IndicatorConfiguration Configuration, IndicatorCalculationVersion CalculationVersion) Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Indicators");
        var calculationVersion = section["CalculationVersion"];
        if (string.IsNullOrWhiteSpace(calculationVersion))
            throw new InvalidOperationException("Indicators:CalculationVersion is required.");
        if (!int.TryParse(section["ConfigurationVersion"], out var configurationVersion) || configurationVersion < 1)
            throw new InvalidOperationException("Indicators:ConfigurationVersion must be a positive integer.");

        var settings = new IndicatorConfiguration
        {
            Version = new ConfigurationVersion(configurationVersion),
            SimpleMovingAverage = section.GetSection("SimpleMovingAverage").Get<SimpleMovingAverageConfiguration>() ?? new(),
            RelativeStrengthIndex = section.GetSection("RelativeStrengthIndex").Get<RelativeStrengthIndexConfiguration>() ?? new(),
            BollingerBands = section.GetSection("BollingerBands").Get<BollingerBandsConfiguration>() ?? new(),
            Macd = section.GetSection("Macd").Get<MovingAverageConvergenceDivergenceConfiguration>() ?? new(),
            AverageTrueRange = section.GetSection("AverageTrueRange").Get<AverageTrueRangeConfiguration>() ?? new(),
            RealizedVolatility = section.GetSection("RealizedVolatility").Get<RealizedVolatilityConfiguration>() ?? new(),
            ImpliedVolatility = section.GetSection("ImpliedVolatility").Get<ImpliedVolatilityConfiguration>() ?? new(),
            Resistance = section.GetSection("Resistance").Get<ResistanceConfiguration>() ?? new(),
            Regime = section.GetSection("Regime").Get<RegimeConfiguration>() ?? new()
        };
        settings.Validate();
        return (settings, new IndicatorCalculationVersion(calculationVersion));
    }
}
