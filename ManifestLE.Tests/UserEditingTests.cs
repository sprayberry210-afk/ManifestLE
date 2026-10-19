using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class UserAccountEditingTests
{
    [Fact]
    public async Task EditingDetailsPreservesAccountAndCredentials()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await service.UpdateUserDetailsAsync(
            test.ActorId,
            test.TargetId,
            "  Updated Officer  ",
            "  07  ",
            "Original Officer",
            "47");

        await using var database = test.CreateContext();

        var user = await database.Users.SingleAsync(
            x => x.Id == test.TargetId);

        var credential = await database.UserCredentials.SingleAsync(
            x => x.UserId == test.TargetId);

        var audit = await database.AuditLogEntries.SingleAsync();

        Assert.Equal("Updated Officer", user.DisplayName);
        Assert.Equal("07", user.BadgeNumber);
        Assert.Equal("test.target", user.Username);
        Assert.Equal(UserRole.Officer, user.Role);
        Assert.True(user.IsActive);

        Assert.Equal(test.OriginalPasswordHash, credential.PasswordHash);
        Assert.Equal(test.OriginalPasswordChangedAt, credential.PasswordChangedAt);
        Assert.True(credential.IsMfaEnabled);
        Assert.Equal(
            new byte[] { 1, 2, 3, 4 },
            credential.ProtectedMfaSecret);
        Assert.Equal(123L, credential.LastAcceptedMfaTimeStep);
        Assert.Equal(2, credential.FailedLoginAttempts);

        Assert.Equal(test.ActorId, audit.UserId);
        Assert.Equal(test.TargetId, audit.EntityId);
        Assert.Equal("User.DetailsUpdated", audit.Action);
        Assert.Contains("Original Officer", audit.Details);
        Assert.Contains("Updated Officer", audit.Details);
        Assert.Contains("47", audit.Details);
        Assert.Contains("07", audit.Details);
        Assert.DoesNotContain(test.OriginalPasswordHash, audit.Details);
    }

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task NonAdministratorCannotEditAccount(UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                test.ActorId,
                test.TargetId,
                "Unauthorized Change",
                "90",
                "Original Officer",
                "47"));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task InactiveAdministratorCannotEditAccount()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var actor = await database.Users.SingleAsync(
                x => x.Id == test.ActorId);

            actor.IsActive = false;
            await database.SaveChangesAsync();
        }

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                test.ActorId,
                test.TargetId,
                "Unauthorized Change",
                "90",
                "Original Officer",
                "47"));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task UnknownActorCannotEditAccount()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                Guid.NewGuid(),
                test.TargetId,
                "Unauthorized Change",
                "90",
                "Original Officer",
                "47"));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task MissingTargetIsRejectedWithoutAuditEntry()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                test.ActorId,
                Guid.NewGuid(),
                "Missing Officer",
                "90",
                "Original Officer",
                "47"));

        await AssertUnchangedAsync(test);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleDetailsCannotOverwriteAnotherChange(
        bool changeBadge)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        string firstName = changeBadge
            ? "Original Officer"
            : "First Updated Name";

        string firstBadge = changeBadge ? "48" : "47";

        await service.UpdateUserDetailsAsync(
            test.ActorId,
            test.TargetId,
            firstName,
            firstBadge,
            "Original Officer",
            "47");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                test.ActorId,
                test.TargetId,
                "Stale Update",
                "90",
                "Original Officer",
                "47"));

        await using var database = test.CreateContext();

        var user = await database.Users.SingleAsync(
            x => x.Id == test.TargetId);

        Assert.Equal(firstName, user.DisplayName);
        Assert.Equal(firstBadge, user.BadgeNumber);
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task SavingUnchangedDetailsCreatesNoAuditEntry()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await service.UpdateUserDetailsAsync(
            test.ActorId,
            test.TargetId,
            "  Original Officer  ",
            "  47  ",
            "Original Officer",
            "47");

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task BlankDisplayNameIsRejectedWithoutChanges()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateUserDetailsAsync(
                test.ActorId,
                test.TargetId,
                "   ",
                "90",
                "Original Officer",
                "47"));

        await AssertUnchangedAsync(test);
    }

    private static async Task AssertUnchangedAsync(TestDatabase test)
    {
        await using var database = test.CreateContext();

        var user = await database.Users.SingleAsync(
            x => x.Id == test.TargetId);

        Assert.Equal("Original Officer", user.DisplayName);
        Assert.Equal("47", user.BadgeNumber);
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:");

        public Guid ActorId { get; } = Guid.NewGuid();

        public Guid TargetId { get; } = Guid.NewGuid();

        public string OriginalPasswordHash { get; private set; }
            = string.Empty;

        public DateTimeOffset OriginalPasswordChangedAt { get; }
            = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public static async Task<TestDatabase> CreateAsync(
            UserRole actorRole = UserRole.Administrator)
        {
            var test = new TestDatabase();

            try
            {
                await test._connection.OpenAsync();

                await using var database = test.CreateContext();

                await database.Database.EnsureCreatedAsync();

                var actor = new AppUser
                {
                    Id = test.ActorId,
                    Username = "test.admin",
                    DisplayName = "Test Administrator",
                    Role = actorRole,
                    IsActive = true
                };

                var target = new AppUser
                {
                    Id = test.TargetId,
                    Username = "test.target",
                    DisplayName = "Original Officer",
                    BadgeNumber = "47",
                    Role = UserRole.Officer,
                    IsActive = true
                };

                test.OriginalPasswordHash =
                    new PasswordService().HashPassword(
                        target,
                        "Fictional editing test password 123!");

                database.Users.AddRange(actor, target);

                database.UserCredentials.Add(new UserCredential
                {
                    UserId = target.Id,
                    User = target,
                    PasswordHash = test.OriginalPasswordHash,
                    PasswordChangedAt = test.OriginalPasswordChangedAt,
                    IsMfaEnabled = true,

                    // Test bytes for verifying preservation only.
                    // These are not a working MFA enrollment.
                    ProtectedMfaSecret = new byte[] { 1, 2, 3, 4 },

                    LastAcceptedMfaTimeStep = 123L,
                    FailedLoginAttempts = 2
                });

                await database.SaveChangesAsync();

                return test;
            }
            catch
            {
                await test.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
    }
}