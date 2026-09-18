using Microsoft.EntityFrameworkCore;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>EF Core persistence boundary for the Phase 1 account, holding, and tax-lot model.</summary>
public sealed class OptionsEngineDbContext(DbContextOptions<OptionsEngineDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<TaxLot> TaxLots => Set<TaxLot>();
    public DbSet<MarketQuoteSnapshotEntity> MarketQuoteSnapshots => Set<MarketQuoteSnapshotEntity>();
    public DbSet<HistoricalPriceBarEntity> HistoricalPriceBars => Set<HistoricalPriceBarEntity>();
    public DbSet<OptionContractSnapshotEntity> OptionContractSnapshots => Set<OptionContractSnapshotEntity>();
    public DbSet<OptionExpirationCacheEntity> OptionExpirationCaches => Set<OptionExpirationCacheEntity>();
    public DbSet<HistoricalPriceCoverageEntity> HistoricalPriceCoverages => Set<HistoricalPriceCoverageEntity>();
    public DbSet<IndicatorSnapshotEntity> IndicatorSnapshots => Set<IndicatorSnapshotEntity>();
    public DbSet<EmptyOptionChainSnapshotEntity> EmptyOptionChainSnapshots => Set<EmptyOptionChainSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(account => account.AccountId);
            entity.Property(account => account.Name).HasMaxLength(200).IsRequired();
            entity.Property(account => account.Broker).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(account => account.AccountType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasMany(account => account.Holdings)
                .WithOne(holding => holding.Account)
                .HasForeignKey(holding => holding.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Holding>(entity =>
        {
            entity.ToTable("Holdings");
            entity.HasKey(holding => holding.HoldingId);
            entity.Property(holding => holding.Symbol).HasMaxLength(16).IsRequired();
            entity.Property(holding => holding.AssetType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(holding => holding.AssignmentSensitivity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(holding => holding.TaxSensitivity).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(holding => holding.Shares).HasPrecision(18, 4);
            entity.Property(holding => holding.MaximumCoveragePercent).HasPrecision(9, 6);
            entity.Property(holding => holding.MinimumPremium).HasPrecision(18, 4);
            entity.HasIndex(holding => new { holding.AccountId, holding.Symbol });
            entity.HasMany(holding => holding.TaxLots)
                .WithOne(taxLot => taxLot.Holding)
                .HasForeignKey(taxLot => taxLot.HoldingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaxLot>(entity =>
        {
            entity.ToTable("TaxLots");
            entity.HasKey(taxLot => taxLot.TaxLotId);
            entity.Property(taxLot => taxLot.Shares).HasPrecision(18, 4);
            entity.Property(taxLot => taxLot.CostBasisPerShare).HasPrecision(18, 4);
            entity.Property(taxLot => taxLot.TotalCostBasis).HasPrecision(18, 4);
            entity.Property(taxLot => taxLot.HoldingPeriodClassification).HasConversion<string>().HasMaxLength(32).IsRequired();
        });
        modelBuilder.Entity<MarketQuoteSnapshotEntity>(entity => { entity.ToTable("MarketQuoteSnapshots"); entity.HasKey(x => x.MarketQuoteSnapshotId); entity.Property(x => x.Symbol).HasMaxLength(32).IsRequired(); entity.Property(x => x.Provider).HasMaxLength(64).IsRequired(); entity.Property(x => x.Last).HasPrecision(18, 6); entity.Property(x => x.Bid).HasPrecision(18, 6); entity.Property(x => x.Ask).HasPrecision(18, 6); entity.Property(x => x.Open).HasPrecision(18, 6); entity.Property(x => x.High).HasPrecision(18, 6); entity.Property(x => x.Low).HasPrecision(18, 6); entity.Property(x => x.PreviousClose).HasPrecision(18, 6); entity.HasIndex(x => new { x.Symbol, x.Provider, x.Timestamp }); });
        modelBuilder.Entity<HistoricalPriceBarEntity>(entity => { entity.ToTable("HistoricalPriceBars"); entity.HasKey(x => x.HistoricalPriceBarId); entity.Property(x => x.Symbol).HasMaxLength(32).IsRequired(); entity.Property(x => x.Provider).HasMaxLength(64).IsRequired(); entity.Property(x => x.Open).HasPrecision(18, 6); entity.Property(x => x.High).HasPrecision(18, 6); entity.Property(x => x.Low).HasPrecision(18, 6); entity.Property(x => x.Close).HasPrecision(18, 6); entity.HasIndex(x => new { x.Symbol, x.Date, x.Provider }).IsUnique(); entity.HasIndex(x => new { x.Symbol, x.Provider, x.Date }); });
        modelBuilder.Entity<OptionContractSnapshotEntity>(entity => { entity.ToTable("OptionContractSnapshots"); entity.HasKey(x => x.OptionContractSnapshotId); entity.Property(x => x.OptionSymbol).HasMaxLength(64).IsRequired(); entity.Property(x => x.UnderlyingSymbol).HasMaxLength(32).IsRequired(); entity.Property(x => x.Provider).HasMaxLength(64).IsRequired(); entity.Property(x => x.Strike).HasPrecision(18, 6); entity.Property(x => x.Bid).HasPrecision(18, 6); entity.Property(x => x.Ask).HasPrecision(18, 6); entity.Property(x => x.Last).HasPrecision(18, 6); entity.Property(x => x.UnderlyingPrice).HasPrecision(18, 6); entity.Property(x => x.OptionType).HasConversion<string>().HasMaxLength(8); entity.HasIndex(x => new { x.UnderlyingSymbol, x.Provider, x.Timestamp }); entity.HasIndex(x => new { x.OptionSymbol, x.Provider, x.Timestamp }); });
        modelBuilder.Entity<OptionExpirationCacheEntity>(entity => { entity.ToTable("OptionExpirationCaches"); entity.HasKey(x => x.OptionExpirationCacheId); entity.Property(x => x.Symbol).HasMaxLength(32).IsRequired(); entity.Property(x => x.Provider).HasMaxLength(64).IsRequired(); entity.Property(x => x.ExpirationsJson).IsRequired(); entity.HasIndex(x => new { x.Symbol, x.Provider, x.RetrievedAt }); });
        modelBuilder.Entity<HistoricalPriceCoverageEntity>(entity => { entity.ToTable("HistoricalPriceCoverages"); entity.HasKey(x => x.HistoricalPriceCoverageId); entity.Property(x => x.Symbol).HasMaxLength(32).IsRequired(); entity.Property(x => x.Provider).HasMaxLength(64).IsRequired(); entity.HasIndex(x => new { x.Symbol, x.Provider, x.StartDate, x.EndDate }); });
        modelBuilder.Entity<IndicatorSnapshotEntity>(entity =>
        {
            entity.ToTable("IndicatorSnapshots");
            entity.HasKey(x => x.IndicatorSnapshotId);
            entity.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
            entity.Property(x => x.IndicatorCalculationVersion).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SnapshotJson).IsRequired();
            entity.HasIndex(x => new { x.Symbol, x.AsOfDate, x.IndicatorCalculationVersion, x.ConfigurationVersion }).IsUnique();
        });
        modelBuilder.Entity<EmptyOptionChainSnapshotEntity>(entity =>
        {
            entity.ToTable("EmptyOptionChainSnapshots");
            entity.HasKey(x => x.EmptyOptionChainSnapshotId);
            entity.Property(x => x.UnderlyingSymbol).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.UnderlyingSymbol, x.Provider, x.Expiration, x.TimestampUtcTicks });
        });
    }
}
