using System.Globalization;
using OptionsEngine.Application.Defense;
using OptionsEngine.Strategy.Defense;
using OptionsEngine.Strategy.EntryStrategy;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Api.Defense;

internal static class DefenseApiConfiguration
{
    public static DefenseRollConfiguration Load(IConfiguration configuration, ConfigurationVersion sharedVersion,
        EntryStrategyConfiguration entryStrategyConfiguration)
    {
        ArgumentNullException.ThrowIfNull(entryStrategyConfiguration);
        var section = configuration.GetSection("Defense");
        var defenseVersion = section["StrategyVersion"];
        var rollVersion = section["RollStrategyVersion"];
        if (string.IsNullOrWhiteSpace(defenseVersion))
            throw new InvalidOperationException("Defense:StrategyVersion is required.");
        if (string.IsNullOrWhiteSpace(rollVersion))
            throw new InvalidOperationException("Defense:RollStrategyVersion is required.");
        if (!decimal.TryParse(section["MaximumRollDebitPerShare"], NumberStyles.Number,
                CultureInfo.InvariantCulture, out var maximumDebit) || maximumDebit < 0)
            throw new InvalidOperationException("Defense:MaximumRollDebitPerShare must be a non-negative decimal.");
        if (section.GetSection("Roll:LiquidityEligibility").Exists() ||
            section.GetSection("Roll:LiquidityScoring").Exists())
            throw new InvalidOperationException(
                "Phase 6 Roll liquidity is shared with EntryStrategy and cannot be configured independently.");

        var loadedDefense = section.GetSection("Configuration").Get<DefenseConfiguration>();
        var loadedRoll = section.GetSection("Roll").Get<RollConfiguration>();
        var result = new DefenseRollConfiguration
        {
            Defense = (loadedDefense ?? new DefenseConfiguration { Version = sharedVersion }) with
            {
                Version = sharedVersion,
                ProfitTaking = section.GetSection("ProfitTaking").Get<ProfitTakingConfiguration>()
                    ?? loadedDefense?.ProfitTaking ?? new ProfitTakingConfiguration(),
                HardTriggers = section.GetSection("HardTriggers").Get<HardTriggerConfiguration>()
                    ?? loadedDefense?.HardTriggers ?? new HardTriggerConfiguration()
            },
            Roll = ((loadedRoll ?? new RollConfiguration
            {
                Version = sharedVersion, MaximumRollDebitPerShare = maximumDebit
            }) with
            {
                Version = sharedVersion,
                MaximumRollDebitPerShare = maximumDebit
            }).WithLiquidityFrom(entryStrategyConfiguration),
            DefenseStrategyVersion = new DefenseStrategyVersion(defenseVersion),
            RollStrategyVersion = new RollStrategyVersion(rollVersion)
        };
        result.Validate();
        return result;
    }
}
