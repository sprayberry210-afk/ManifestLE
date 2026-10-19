using System;

namespace ManifestLE.Core.Models;

public enum InventoryAuditResult
{
    Pending,
    Verified,
    Missing,
    Discrepancy
}

public sealed class InventoryAuditItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InventoryAuditId { get; set; }

    public InventoryAudit InventoryAudit { get; set; } = null!;

    // Exactly one property reference will be populated.
    public Guid? EvidenceItemId { get; set; }

    public Guid? FoundPropertyItemId { get; set; }

    public Guid? SafekeepingItemId { get; set; }

    // These values preserve the expected state at audit creation.
    public string ReportNumberSnapshot { get; set; } = string.Empty;

    public string ItemNumberSnapshot { get; set; } = string.Empty;

    public string DescriptionSnapshot { get; set; } = string.Empty;

    public Guid ExpectedStorageLocationId { get; set; }

    public string ExpectedStoragePathSnapshot { get; set; } =
        string.Empty;

    public EvidenceStatus ExpectedStatus { get; set; } =
        EvidenceStatus.InCustody;

    // These values record the physical inspection.
    public InventoryAuditResult Result { get; set; } =
        InventoryAuditResult.Pending;

    public Guid? CheckedByUserId { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    public string Notes { get; set; } = string.Empty;
}