namespace ManifestLE.Core.Models;

public class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    public string Details { get; set; } = string.Empty;
}