namespace OptionsEngine.Domain.Accounts;

/// <summary>Configuration and share ownership for one security in one account.</summary>
public sealed class Holding
{
    public Guid HoldingId { get; private set; } = Guid.NewGuid();
    public Guid AccountId { get; private set; }
    public Account Account { get; private set; } = null!;
    public string Symbol { get; private set; } = null!;
    public AssetType AssetType { get; private set; }
    public decimal Shares { get; private set; }
    public AssignmentSensitivity AssignmentSensitivity { get; private set; }
    public TaxSensitivity TaxSensitivity { get; private set; }
    public decimal MaximumCoveragePercent { get; private set; }
    public double MaximumInitialDelta { get; private set; }
    public double PreferredDeltaMinimum { get; private set; }
    public double PreferredDeltaMaximum { get; private set; }
    public double MinimumCcos { get; private set; }
    public double MinimumContractScore { get; private set; }
    public decimal MinimumPremium { get; private set; }
    public double MinimumAnnualizedYield { get; private set; }
    public double MaximumDeltaExposureRatio { get; private set; }
    public bool IsEnabled { get; private set; }
    public ICollection<TaxLot> TaxLots { get; } = new List<TaxLot>();

    private Holding()
    {
    }

    public Holding(
        Account account,
        string symbol,
        AssetType assetType,
        decimal shares,
        AssignmentSensitivity assignmentSensitivity,
        TaxSensitivity taxSensitivity,
        decimal maximumCoveragePercent,
        double maximumInitialDelta,
        double preferredDeltaMinimum,
        double preferredDeltaMaximum,
        double minimumCcos,
        double minimumContractScore,
        decimal minimumPremium,
        double minimumAnnualizedYield,
        double maximumDeltaExposureRatio,
        bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        Account = account;
        AccountId = account.AccountId;
        Symbol = symbol.ToUpperInvariant();
        AssetType = assetType;
        Shares = shares;
        AssignmentSensitivity = assignmentSensitivity;
        TaxSensitivity = taxSensitivity;
        MaximumCoveragePercent = maximumCoveragePercent;
        MaximumInitialDelta = maximumInitialDelta;
        PreferredDeltaMinimum = preferredDeltaMinimum;
        PreferredDeltaMaximum = preferredDeltaMaximum;
        MinimumCcos = minimumCcos;
        MinimumContractScore = minimumContractScore;
        MinimumPremium = minimumPremium;
        MinimumAnnualizedYield = minimumAnnualizedYield;
        MaximumDeltaExposureRatio = maximumDeltaExposureRatio;
        IsEnabled = isEnabled;
    }
}
