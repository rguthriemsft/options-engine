using Microsoft.EntityFrameworkCore;
using OptionsEngine.Domain.Accounts;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>EF Core persistence boundary for the Phase 1 account, holding, and tax-lot model.</summary>
public sealed class OptionsEngineDbContext(DbContextOptions<OptionsEngineDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Holding> Holdings => Set<Holding>();
    public DbSet<TaxLot> TaxLots => Set<TaxLot>();

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
    }
}
