using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class StorageAccessServiceTests
{
    [Fact]
    public async Task OfficerSeesOnlyActiveTemporaryLocations()
    {
        await using var test = await TestDatabase.CreateAsync();
        await using var database = test.CreateContext();

        var locations =
            await StorageAccessService.GetIntakeLocationsAsync(
                database,
                test.UserId);

        var location = Assert.Single(locations);

        Assert.Equal(test.TemporaryId, location.Id);
        Assert.Equal(
            StorageLocationType.Temporary,
            location.LocationType);
    }

    [Fact]
    public async Task OfficerCanSubmitToActiveTemporaryLocker()
    {
        await using var test = await TestDatabase.CreateAsync();
        await using var database = test.CreateContext();

        var location =
            await StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.TemporaryId);

        Assert.Equal(test.TemporaryId, location.Id);
    }

    [Fact]
    public async Task OfficerCannotSubmitToPermanentStorage()
    {
        await using var test = await TestDatabase.CreateAsync();
        await using var database = test.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.PermanentId));
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task AuthorizedStaffSeeBothActiveLocationTypes(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);
        await using var database = test.CreateContext();

        var locations =
            await StorageAccessService.GetIntakeLocationsAsync(
                database,
                test.UserId);

        Assert.Equal(2, locations.Count);

        Assert.Contains(locations, x => x.Id == test.TemporaryId);
        Assert.Contains(locations, x => x.Id == test.PermanentId);

        Assert.DoesNotContain(
            locations,
            x => x.Id == test.InactiveId);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task AuthorizedStaffCanSubmitToPermanentStorage(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);
        await using var database = test.CreateContext();

        var location =
            await StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.PermanentId);

        Assert.Equal(test.PermanentId, location.Id);
    }

    [Fact]
    public async Task InactiveAccountCannotListOrSelectLocations()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var setup = test.CreateContext())
        {
            var user = await setup.Users.SingleAsync(
                x => x.Id == test.UserId);

            user.IsActive = false;
            await setup.SaveChangesAsync();
        }

        await using var database = test.CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.GetIntakeLocationsAsync(
                database,
                test.UserId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.TemporaryId));
    }

    [Fact]
    public async Task UnknownAccountCannotListOrSelectLocations()
    {
        await using var test = await TestDatabase.CreateAsync();
        await using var database = test.CreateContext();

        Guid unknownUserId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.GetIntakeLocationsAsync(
                database,
                unknownUserId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                unknownUserId,
                test.TemporaryId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InactiveOrMissingLocationIsRejected(
        bool missing)
    {
        await using var test = await TestDatabase.CreateAsync(
            UserRole.Administrator);

        await using var database = test.CreateContext();

        Guid locationId = missing
            ? Guid.NewGuid()
            : test.InactiveId;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                locationId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviouslyListedLockerIsRecheckedBeforeUse(
        bool changeToPermanent)
    {
        await using var test = await TestDatabase.CreateAsync();
        await using var database = test.CreateContext();

        var locations =
            await StorageAccessService.GetIntakeLocationsAsync(
                database,
                test.UserId);

        Assert.Contains(
            locations,
            x => x.Id == test.TemporaryId);

        await using (var update = test.CreateContext())
        {
            var locker = await update.StorageLocations.SingleAsync(
                x => x.Id == test.TemporaryId);

            if (changeToPermanent)
            {
                locker.LocationType = StorageLocationType.Permanent;
            }
            else
            {
                locker.IsActive = false;
            }

            await update.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.TemporaryId));
    }

    [Fact]
    public async Task AdministratorChangedToOfficerLosesPermanentAccess()
    {
        await using var test = await TestDatabase.CreateAsync(
            UserRole.Administrator);

        await using var database = test.CreateContext();

        var locations =
            await StorageAccessService.GetIntakeLocationsAsync(
                database,
                test.UserId);

        Assert.Contains(
            locations,
            x => x.Id == test.PermanentId);

        await using (var update = test.CreateContext())
        {
            var user = await update.Users.SingleAsync(
                x => x.Id == test.UserId);

            user.Role = UserRole.Officer;
            await update.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StorageAccessService.RequireIntakeLocationAsync(
                database,
                test.UserId,
                test.PermanentId));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:");

        public Guid UserId { get; } = Guid.NewGuid();

        public Guid TemporaryId { get; } = Guid.NewGuid();

        public Guid PermanentId { get; } = Guid.NewGuid();

        public Guid InactiveId { get; } = Guid.NewGuid();

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public static async Task<TestDatabase> CreateAsync(
            UserRole role = UserRole.Officer)
        {
            var test = new TestDatabase();

            try
            {
                await test._connection.OpenAsync();

                await using var database = test.CreateContext();

                await database.Database.EnsureCreatedAsync();

                database.Users.Add(new AppUser
                {
                    Id = test.UserId,
                    Username = "test.storage.user",
                    DisplayName = "Test Storage User",
                    Role = role,
                    IsActive = true
                });

                database.StorageLocations.AddRange(
                    new StorageLocation
                    {
                        Id = test.TemporaryId,
                        Name = "Temporary Locker 01",
                        LocationType = StorageLocationType.Temporary,
                        IsActive = true
                    },
                    new StorageLocation
                    {
                        Id = test.PermanentId,
                        Name = "Permanent Cabinet A",
                        LocationType = StorageLocationType.Permanent,
                        IsActive = true
                    },
                    new StorageLocation
                    {
                        Id = test.InactiveId,
                        Name = "Inactive Temporary Locker",
                        LocationType = StorageLocationType.Temporary,
                        IsActive = false
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