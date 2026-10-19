using System;

namespace ManifestLE.Core.Models;

public enum InventoryAuditFollowUpType
{
    InvestigationNote,
    Resolved
}

public sealed class InventoryAuditFollowUp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InventoryAuditItemId { get; set; }

    public InventoryAuditItem InventoryAuditItem { get; set; } = null!;

    public InventoryAuditFollowUpType Type { get; set; } =
        InventoryAuditFollowUpType.InvestigationNote;

    public Guid RecordedByUserId { get; set; }

    public DateTimeOffset RecordedAt { get; set; } =
        DateTimeOffset.UtcNow;

    public string Notes { get; set; } = string.Empty;
}