namespace ManifestLE.Core.Models;

public class UserCredential
{
    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public string PasswordHash { get; set; } = string.Empty;

    public byte[]? ProtectedMfaSecret { get; set; }

    public bool IsMfaEnabled { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTimeOffset? LockoutUntil { get; set; }

    public long? LastAcceptedMfaTimeStep { get; set; }

    public DateTimeOffset PasswordChangedAt { get; set; }
        = DateTimeOffset.UtcNow;
}