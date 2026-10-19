using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Security;
using ManifestLE.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class UserAdministrationServiceTests
{
    private const string TestPassword =
        "Fictional test password 123!";

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData(UserRole.EvidenceCustodian)]
    [InlineData(UserRole.Administrator)]
    public async Task AdministratorCanCreateAccountWithHashedPasswordAndAudit(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        Guid createdId = await service.CreateUserAsync(
            test.ActorId,
            "  TEST.OFFICER  ",
            "  Test Officer  ",
            TestPassword,
            role);

        await using var database = test.CreateContext();

        var user = await database.Users.SingleAsync(
            x => x.Id == createdId);

        var credential = await database.UserCredentials.SingleAsync(
            x => x.UserId == createdId);

        var audit = await database.AuditLogEntries.SingleAsync();

        Assert.Equal("test.officer", user.Username);
        Assert.Equal("Test Officer", user.DisplayName);
        Assert.Equal(role, user.Role);
        Assert.True(user.IsActive);

        Assert.NotEqual(TestPassword, credential.PasswordHash);

        var verification = new PasswordService().VerifyPassword(
            user,
            credential.PasswordHash,
            TestPassword);

        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            verification);

        Assert.False(credential.IsMfaEnabled);
        Assert.Null(credential.ProtectedMfaSecret);

        Assert.Equal(test.ActorId, audit.UserId);
        Assert.Equal(createdId, audit.EntityId);
        Assert.Equal("User.Created", audit.Action);
        Assert.DoesNotContain(TestPassword, audit.Details);
        Assert.DoesNotContain(credential.PasswordHash, audit.Details);

        var users = await service.GetUsersAsync(test.ActorId);

        Assert.Equal(2, users.Count);
        Assert.Contains(users, x => x.Id == createdId);
    }

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task NonAdministratorCannotListOrCreateAccounts(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetUsersAsync(test.ActorId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                "new.officer",
                "New Officer",
                TestPassword,
                UserRole.Officer));

        await AssertNoAccountCreatedAsync(test);
    }

    [Fact]
    public async Task InactiveAdministratorCannotManageAccounts()
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
            service.GetUsersAsync(test.ActorId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                "new.officer",
                "New Officer",
                TestPassword,
                UserRole.Officer));

        await AssertNoAccountCreatedAsync(test);
    }

    [Fact]
    public async Task UnknownAccountCannotManageUsers()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);
        Guid unknownId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetUsersAsync(unknownId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                unknownId,
                "new.officer",
                "New Officer",
                TestPassword,
                UserRole.Officer));

        await AssertNoAccountCreatedAsync(test);
    }

    [Fact]
    public async Task DuplicateUsernameIsRejectedWithoutAdditionalWrites()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        Guid originalId = await service.CreateUserAsync(
            test.ActorId,
            "test.officer",
            "Original Officer",
            TestPassword,
            UserRole.Officer);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                "  TEST.OFFICER  ",
                "Duplicate Officer",
                TestPassword,
                UserRole.Administrator));

        await using var database = test.CreateContext();

        Assert.Equal(2, await database.Users.CountAsync());
        Assert.Equal(1, await database.UserCredentials.CountAsync());
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());

        var original = await database.Users.SingleAsync(
            x => x.Id == originalId);

        Assert.Equal("Original Officer", original.DisplayName);
        Assert.Equal(UserRole.Officer, original.Role);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("invalid username")]
    public async Task InvalidUsernameIsRejected(string username)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                username,
                "Test Officer",
                TestPassword,
                UserRole.Officer));

        await AssertNoAccountCreatedAsync(test);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(129)]
    public async Task PasswordOutsideAllowedLengthIsRejected(int length)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                "test.officer",
                "Test Officer",
                new string('x', length),
                UserRole.Officer));

        await AssertNoAccountCreatedAsync(test);
    }

    [Fact]
    public async Task UnknownRoleIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new UserAdministrationService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateUserAsync(
                test.ActorId,
                "test.officer",
                "Test Officer",
                TestPassword,
                (UserRole)999));

        await AssertNoAccountCreatedAsync(test);
    }

    private static async Task AssertNoAccountCreatedAsync(
        TestDatabase test)
    {
        await using var database = test.CreateContext();

        Assert.Equal(1, await database.Users.CountAsync());
        Assert.Equal(0, await database.UserCredentials.CountAsync());
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:");

        public Guid ActorId { get; } = Guid.NewGuid();

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public static async Task<TestDatabase> CreateAsync(
            UserRole role = UserRole.Administrator)
        {
            var test = new TestDatabase();

            try
            {
                await test._connection.OpenAsync();

                await using var database = test.CreateContext();

                await database.Database.EnsureCreatedAsync();

                database.Users.Add(new AppUser
                {
                    Id = test.ActorId,
                    Username = "test.actor",
                    DisplayName = "Test Acting User",
                    Role = role,
                    IsActive = true
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