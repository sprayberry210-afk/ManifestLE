namespace ManifestLE.Core.Models;

public enum UserRole
{
    Officer,
    EvidenceCustodian,
    Administrator
}

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string BadgeNumber { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Officer;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
        = DateTimeOffset.UtcNow;
}