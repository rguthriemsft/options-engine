using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Domain.Tests;

public sealed class AccountModelTests
{
    [Fact]
    public void HoldingIdentityIsIndependentOfTickerAndTaxLotLinksToHolding()
    {
        var firstAccount = new Account("Taxable", Broker.Fidelity, AccountType.Taxable, false);
        var secondAccount = new Account("Roth", Broker.Schwab, AccountType.RothIra, true);
        var firstHolding = CreateHolding(firstAccount, "MSFT");
        var secondHolding = CreateHolding(secondAccount, "MSFT");
        var lot = new TaxLot(firstHolding, new DateOnly(2020, 1, 2), 100m, 10m, 1000m, HoldingPeriodClassification.LongTerm);

        Assert.NotEqual(firstHolding.HoldingId, secondHolding.HoldingId);
        Assert.Equal(firstAccount.AccountId, firstHolding.AccountId);
        Assert.Equal(firstHolding.HoldingId, lot.HoldingId);
        Assert.Equal(1000m, lot.TotalCostBasis);
    }

    internal static Holding CreateHolding(Account account, string symbol) => new(
        account, symbol, AssetType.Stock, 250m, AssignmentSensitivity.Level4, TaxSensitivity.High,
        0.7m, 0.2, 0.12, 0.18, 70, 80, 0.5m, 0.1, 0.15);
}
