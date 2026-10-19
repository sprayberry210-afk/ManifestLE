namespace ManifestLE.Core.Models;

public enum CustodyEventType
{
    Intake,
    Transfer,
    CheckOut,
    CheckIn,
    Release,
    Disposal
}

public class CustodyEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? EvidenceItemId { get; set; }

    public Guid? FoundPropertyItemId { get; set; }

    public Guid? SafekeepingItemId { get; set; }

    public CustodyEventType EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
        = DateTimeOffset.UtcNow;

    public DateTimeOffset RecordedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public Guid RecordedByUserId { get; set; }

    public string FromCustodian { get; set; } = string.Empty;

    public string ToCustodian { get; set; } = string.Empty;

    public Guid? FromStorageLocationId { get; set; }

    public Guid? ToStorageLocationId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;
}