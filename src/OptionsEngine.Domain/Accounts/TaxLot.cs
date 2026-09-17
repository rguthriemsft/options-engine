namespace OptionsEngine.Domain.Accounts;

/// <summary>An acquisition lot retained for holding-period and cost-basis information.</summary>
public sealed class TaxLot
{
    public Guid TaxLotId { get; private set; } = Guid.NewGuid();
    public Guid HoldingId { get; private set; }
    public Holding Holding { get; private set; } = null!;
    public DateOnly AcquisitionDate { get; private set; }
    public decimal Shares { get; private set; }
    public decimal CostBasisPerShare { get; private set; }
    public decimal TotalCostBasis { get; private set; }
    public HoldingPeriodClassification HoldingPeriodClassification { get; private set; }

    private TaxLot()
    {
    }

    public TaxLot(
        Holding holding,
        DateOnly acquisitionDate,
        decimal shares,
        decimal costBasisPerShare,
        decimal totalCostBasis,
        HoldingPeriodClassification holdingPeriodClassification)
    {
        ArgumentNullException.ThrowIfNull(holding);
        Holding = holding;
        HoldingId = holding.HoldingId;
        AcquisitionDate = acquisitionDate;
        Shares = shares;
        CostBasisPerShare = costBasisPerShare;
        TotalCostBasis = totalCostBasis;
        HoldingPeriodClassification = holdingPeriodClassification;
    }
}
