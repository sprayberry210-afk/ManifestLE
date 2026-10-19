namespace ManifestLE.Core.Models;

public enum EvidenceStatus
{
    InCustody,
    CheckedOut,
    Released,
    Disposed
}

public class EvidenceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CaseNumber { get; set; } = string.Empty;

    public string ItemNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CollectedBy { get; set; } = string.Empty;

    public DateTimeOffset CollectedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public string CollectionLocation { get; set; } = string.Empty;

    public string Packaging { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    public string OwnerContact { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public Guid? CurrentStorageLocationId { get; set; }

    public EvidenceStatus Status { get; set; }
        = EvidenceStatus.InCustody;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
}