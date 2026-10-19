using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class UserAdministrationService
{
    private readonly Func<ManifestDbContext> _createContext;
    private readonly PasswordService _passwordService = new();

    public UserAdministrationService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public UserAdministrationService(
        Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task<List<AppUser>> GetUsersAsync(
        Guid administratorId)
    {
        await using var database = _createContext();

        await RequireAdministratorAsync(
            database,
            administratorId);

        return await database.Users
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Username)
            .ToListAsync();
    }

    public async Task<Guid> CreateUserAsync(
        Guid administratorId,
        string username,
        string displayName,
        string password,
        UserRole role,
        string badgeNumber = "")
    {
        username = (username ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        displayName = (displayName ?? string.Empty).Trim();
        badgeNumber = (badgeNumber ?? string.Empty).Trim();

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync();

        await RequireAdministratorAsync(
            database,
            administratorId);

        if (!Regex.IsMatch(
            username,
            @"\A[a-z0-9._-]{3,50}\z"))
        {
            throw new InvalidOperationException(
                "Use a username of 3–50 characters containing " +
                "letters, numbers, periods, underscores, or hyphens.");
        }

        ValidateDetails(displayName, badgeNumber);

        if (password is null ||
            password.Length < 15 ||
            password.Length > 128)
        {
            throw new InvalidOperationException(
                "Use a password between 15 and 128 characters.");
        }

        if (!Enum.IsDefined(typeof(UserRole), role))
        {
            throw new InvalidOperationException(
                "Choose a valid user role.");
        }

        bool usernameExists = await database.Users.AnyAsync(
            x => x.Username.ToLower() == username);

        if (usernameExists)
        {
            throw new InvalidOperationException(
                "That username is already assigned to an account.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        var user = new AppUser
        {
            Username = username,
            DisplayName = displayName,
            BadgeNumber = badgeNumber,
            Role = role,
            IsActive = true,
            CreatedAt = now
        };

        var credential = new UserCredential
        {
            UserId = user.Id,
            User = user,
            PasswordHash = _passwordService.HashPassword(
                user,
                password),
            PasswordChangedAt = now,
            IsMfaEnabled = false
        };

        database.Users.Add(user);
        database.UserCredentials.Add(credential);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = administratorId,
            RecordedAt = now,
            Action = "User.Created",
            EntityType = nameof(AppUser),
            EntityId = user.Id,
            Details =
                $"Account created: {username}. " +
                $"Display name: {displayName}. " +
                $"Badge number: {DescribeBadge(badgeNumber)}. " +
                $"Role: {role}."
        });

        try
        {
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqliteException sqlite &&
                  sqlite.SqliteExtendedErrorCode == 2067)
        {
            throw new InvalidOperationException(
                "The account could not be created because " +
                "a unique value is already in use. Refresh and try again.",
                ex);
        }

        return user.Id;
    }

    public async Task UpdateUserDetailsAsync(
        Guid administratorId,
        Guid userId,
        string displayName,
        string badgeNumber,
        string expectedDisplayName,
        string expectedBadgeNumber)
    {
        displayName = (displayName ?? string.Empty).Trim();
        badgeNumber = (badgeNumber ?? string.Empty).Trim();

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync();

        await RequireAdministratorAsync(
            database,
            administratorId);

        ValidateDetails(displayName, badgeNumber);

        var user = await database.Users
            .SingleOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new InvalidOperationException(
                "The selected account could not be found.");
        }

        if (!string.Equals(
                user.DisplayName,
                expectedDisplayName,
                StringComparison.Ordinal) ||
            !string.Equals(
                user.BadgeNumber,
                expectedBadgeNumber,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This account's details changed after you loaded it. " +
                "Refresh the account list and select it again.");
        }

        bool unchanged =
            string.Equals(
                user.DisplayName,
                displayName,
                StringComparison.Ordinal) &&
            string.Equals(
                user.BadgeNumber,
                badgeNumber,
                StringComparison.Ordinal);

        if (unchanged)
        {
            await transaction.CommitAsync();
            return;
        }

        string previousDisplayName = user.DisplayName;
        string previousBadgeNumber = user.BadgeNumber;

        user.DisplayName = displayName;
        user.BadgeNumber = badgeNumber;

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = administratorId,
            RecordedAt = DateTimeOffset.UtcNow,
            Action = "User.DetailsUpdated",
            EntityType = nameof(AppUser),
            EntityId = user.Id,
            Details =
                $"Account: {user.Username}. " +
                $"Display name changed from " +
                $"'{previousDisplayName}' to '{displayName}'. " +
                $"Badge number changed from " +
                $"'{DescribeBadge(previousBadgeNumber)}' to " +
                $"'{DescribeBadge(badgeNumber)}'."
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateDetails(
        string displayName,
        string badgeNumber)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Length > 200)
        {
            throw new InvalidOperationException(
                "Enter a display name of 1–200 characters.");
        }

        if (displayName.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The display name cannot contain control characters.");
        }

        if (badgeNumber.Length > 30)
        {
            throw new InvalidOperationException(
                "Use a badge number of no more than 30 characters.");
        }

        if (badgeNumber.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "The badge number cannot contain control characters.");
        }
    }

    private static string DescribeBadge(string badgeNumber)
    {
        return string.IsNullOrWhiteSpace(badgeNumber)
            ? "Not provided"
            : badgeNumber;
    }

    private static async Task RequireAdministratorAsync(
        ManifestDbContext database,
        Guid administratorId)
    {
        bool authorized = await database.Users
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == administratorId &&
                x.IsActive &&
                x.Role == UserRole.Administrator);

        if (!authorized)
        {
            throw new InvalidOperationException(
                "An active administrator account is required " +
                "to manage users.");
        }
    }
}