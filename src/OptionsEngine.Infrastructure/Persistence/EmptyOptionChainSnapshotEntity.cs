namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Preserves observed empty chains; there are no contract rows to represent these in Phase 2.</summary>
public sealed class EmptyOptionChainSnapshotEntity
{
    public long EmptyOptionChainSnapshotId { get; set; }
    public required string UnderlyingSymbol { get; set; }
    public required string Provider { get; set; }
    public DateOnly Expiration { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long TimestampUtcTicks { get; set; }
}
