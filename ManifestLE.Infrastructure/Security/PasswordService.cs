using System;
using ManifestLE.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace ManifestLE.Infrastructure.Security;

public class PasswordService
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public string HashPassword(AppUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        if (password.Length < 15 || password.Length > 128)
        {
            throw new ArgumentException(
                "Use a password between 15 and 128 characters.",
                nameof(password));
        }

        return _hasher.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyPassword(
        AppUser user,
        string storedHash,
        string suppliedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(storedHash) ||
            suppliedPassword is null ||
            suppliedPassword.Length > 128)
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(
                user,
                storedHash,
                suppliedPassword);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}