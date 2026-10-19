namespace ManifestLE.Core.Models;

public class SafekeepingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ReportNumber { get; set; } = string.Empty;

    public string ItemNumber { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CollectedBy { get; set; } = string.Empty;

    public DateTimeOffset CollectedAt { get; set; }
        = DateTimeOffset.UtcNow;

    public string CollectionLocation { get; set; } = string.Empty;

    public string OwnerName { get; set; } = string.Empty;

    public string OwnerContact { get; set; } = string.Empty;

    public string HoldReason { get; set; } = string.Empty;

    public string HoldAuthority { get; set; } = string.Empty;

    public DateTimeOffset? ReviewDueAt { get; set; }

    public bool IsFirearm { get; set; }

    public string Manufacturer { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public string Caliber { get; set; } = string.Empty;

    public string Packaging { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public Guid? CurrentStorageLocationId { get; set; }

    public EvidenceStatus Status { get; set; }
        = EvidenceStatus.InCustody;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
}