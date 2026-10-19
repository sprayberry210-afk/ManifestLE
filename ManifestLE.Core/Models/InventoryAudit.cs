using System;
using System.Collections.Generic;

namespace ManifestLE.Core.Models;

public enum InventoryAuditStatus
{
    InProgress,
    Completed
}

public sealed class InventoryAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StorageLocationId { get; set; }

    public string StoragePathSnapshot { get; set; } = string.Empty;

    public bool IncludeChildLocations { get; set; } = true;

    public InventoryAuditStatus Status { get; set; } =
        InventoryAuditStatus.InProgress;

    public Guid StartedByUserId { get; set; }

    public DateTimeOffset StartedAt { get; set; } =
        DateTimeOffset.UtcNow;

    public Guid? CompletedByUserId { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string Notes { get; set; } = string.Empty;

    public ICollection<InventoryAuditItem> Items { get; set; } =
        new List<InventoryAuditItem>();
}