using OptionsEngine.MarketData.Models;
using OptionsEngine.Strategy.Indicators;

namespace OptionsEngine.Application.MarketData;

/// <summary>Transfers normalized Phase 2 option observations into the pure indicator boundary.</summary>
public static class ImpliedVolatilityObservationMapper
{
    public static IvOptionChainObservation Map(OptionChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(chain.Contracts);
        if (chain.Contracts.Any(contract =>
                !string.Equals(contract.UnderlyingSymbol, chain.UnderlyingSymbol, StringComparison.OrdinalIgnoreCase) ||
                contract.Expiration != chain.Expiration || contract.Timestamp != chain.Timestamp ||
                !string.Equals(contract.Provider, chain.Provider, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Option contracts must belong to the supplied chain snapshot.", nameof(chain));
        return new IvOptionChainObservation(chain.UnderlyingSymbol, chain.Expiration, chain.Timestamp,
            chain.Contracts.Select(contract => new IvOptionContract(contract.Strike,
                contract.OptionType switch
                {
                    OptionType.Call => IvOptionType.Call,
                    OptionType.Put => IvOptionType.Put,
                    _ => throw new ArgumentOutOfRangeException(nameof(chain), "Unknown option type.")
                },
                contract.ImpliedVolatility, contract.UnderlyingPrice)).ToArray());
    }
}
