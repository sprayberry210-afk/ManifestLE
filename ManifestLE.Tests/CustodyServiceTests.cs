using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class CustodyServiceTests
{
    [Theory]
    [InlineData(PropertyRegisterKind.Evidence)]
    [InlineData(PropertyRegisterKind.FoundProperty)]
    [InlineData(PropertyRegisterKind.Safekeeping)]
    public async Task CheckoutAndCheckInPreserveHistory(
        PropertyRegisterKind register)
    {
        await using var test = await TestDatabase.CreateAsync(register);

        var service = new CustodyService(test.CreateContext);

        await service.RecordMovementAsync(
            test.UserId,
            register,
            test.ItemId,
            CustodyEventType.CheckOut,
            EvidenceStatus.InCustody,
            test.SourceId,
            null,
            "Test Custodian",
            "Test Officer",
            "Temporary examination",
            "Checkout test");

        await using (var database = test.CreateContext())
        {
            var state = await ReadStateAsync(database, register, test.ItemId);

            Assert.Equal(EvidenceStatus.CheckedOut, state.Status);
            Assert.Null(state.LocationId);

            var custody = Assert.Single(
                await database.CustodyEvents.ToListAsync());

            Assert.Equal(CustodyEventType.CheckOut, custody.EventType);
            Assert.Equal(test.SourceId, custody.FromStorageLocationId);
            Assert.Null(custody.ToStorageLocationId);
            Assert.Equal("Test Officer", custody.ToCustodian);

            AssertLinksToItem(custody, register, test.ItemId);

            var audit = Assert.Single(
                await database.AuditLogEntries.ToListAsync());

            Assert.Equal("Custody.CheckOut", audit.Action);
            Assert.Equal(test.ItemId, audit.EntityId);
            Assert.Equal(test.UserId, audit.UserId);
        }

        await service.RecordMovementAsync(
            test.UserId,
            register,
            test.ItemId,
            CustodyEventType.CheckIn,
            EvidenceStatus.CheckedOut,
            null,
            test.DestinationId,
            "Test Officer",
            "Test Custodian",
            "Returned after examination",
            "Check-in test");

        await using (var database = test.CreateContext())
        {
            var state = await ReadStateAsync(database, register, test.ItemId);

            Assert.Equal(EvidenceStatus.InCustody, state.Status);
            Assert.Equal(test.DestinationId, state.LocationId);

            var events = await database.CustodyEvents.ToListAsync();

            Assert.Equal(2, events.Count);

            Assert.Contains(
                events,
                x => x.EventType == CustodyEventType.CheckOut &&
                     x.ToCustodian == "Test Officer");

            var checkIn = Assert.Single(
                events,
                x => x.EventType == CustodyEventType.CheckIn);

            Assert.Null(checkIn.FromStorageLocationId);
            Assert.Equal(
                test.DestinationId,
                checkIn.ToStorageLocationId);

            AssertLinksToItem(checkIn, register, test.ItemId);

            Assert.Equal(
                2,
                await database.AuditLogEntries.CountAsync());
        }
    }

    [Theory]
    [InlineData(PropertyRegisterKind.Evidence)]
    [InlineData(PropertyRegisterKind.FoundProperty)]
    [InlineData(PropertyRegisterKind.Safekeeping)]
    public async Task TransferUpdatesLocationAndRecordsMovement(
        PropertyRegisterKind register)
    {
        await using var test = await TestDatabase.CreateAsync(register);

        var service = new CustodyService(test.CreateContext);

        await service.RecordMovementAsync(
            test.UserId,
            register,
            test.ItemId,
            CustodyEventType.Transfer,
            EvidenceStatus.InCustody,
            test.SourceId,
            test.DestinationId,
            "Test Custodian",
            "Test Custodian",
            "Storage reorganization",
            "");

        await using var database = test.CreateContext();

        var state = await ReadStateAsync(database, register, test.ItemId);

        Assert.Equal(EvidenceStatus.InCustody, state.Status);
        Assert.Equal(test.DestinationId, state.LocationId);

        var custody = Assert.Single(
            await database.CustodyEvents.ToListAsync());

        Assert.Equal(CustodyEventType.Transfer, custody.EventType);
        Assert.Equal(test.SourceId, custody.FromStorageLocationId);
        Assert.Equal(test.DestinationId, custody.ToStorageLocationId);

        AssertLinksToItem(custody, register, test.ItemId);

        var audit = Assert.Single(
            await database.AuditLogEntries.ToListAsync());

        Assert.Equal("Custody.Transfer", audit.Action);
    }

    [Fact]
    public async Task AlreadyCheckedOutItemCannotBeCheckedOutAgain()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence,
            EvidenceStatus.CheckedOut);

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.CheckOut,
                EvidenceStatus.CheckedOut,
                null,
                null,
                "Test Custodian",
                "Test Officer",
                "Invalid second checkout",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.CheckedOut,
            null);
    }

    [Fact]
    public async Task InCustodyItemCannotBeCheckedIn()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.CheckIn,
                EvidenceStatus.InCustody,
                test.SourceId,
                test.DestinationId,
                "Test Officer",
                "Test Custodian",
                "Invalid check-in",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    [Fact]
    public async Task TransferToSameLocationIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.Transfer,
                EvidenceStatus.InCustody,
                test.SourceId,
                test.SourceId,
                "Test Custodian",
                "Test Custodian",
                "Invalid same-location transfer",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    [Fact]
    public async Task InactiveDestinationIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        await using (var database = test.CreateContext())
        {
            var location = await database.StorageLocations
                .SingleAsync(x => x.Id == test.DestinationId);

            location.IsActive = false;
            await database.SaveChangesAsync();
        }

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.Transfer,
                EvidenceStatus.InCustody,
                test.SourceId,
                test.DestinationId,
                "Test Custodian",
                "Test Custodian",
                "Transfer to inactive location",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    [Theory]
    [InlineData(UserRole.Officer, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task UnauthorizedOrInactiveUserCannotMoveItem(
        UserRole role,
        bool isActive)
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        await using (var database = test.CreateContext())
        {
            var user = await database.Users
                .SingleAsync(x => x.Id == test.UserId);

            user.Role = role;
            user.IsActive = isActive;

            await database.SaveChangesAsync();
        }

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.CheckOut,
                EvidenceStatus.InCustody,
                test.SourceId,
                null,
                "Test Custodian",
                "Test Officer",
                "Unauthorized checkout",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    [Fact]
    public async Task MissingReasonIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.CheckOut,
                EvidenceStatus.InCustody,
                test.SourceId,
                null,
                "Test Custodian",
                "Test Officer",
                "   ",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    [Fact]
    public async Task StaleDisplayedLocationIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync(
            PropertyRegisterKind.Evidence);

        var service = new CustodyService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordMovementAsync(
                test.UserId,
                PropertyRegisterKind.Evidence,
                test.ItemId,
                CustodyEventType.CheckOut,
                EvidenceStatus.InCustody,
                test.DestinationId,
                null,
                "Test Custodian",
                "Test Officer",
                "Checkout with outdated location",
                ""));

        await AssertUnchangedAsync(
            test,
            EvidenceStatus.InCustody,
            test.SourceId);
    }

    private static async Task AssertUnchangedAsync(
        TestDatabase test,
        EvidenceStatus expectedStatus,
        Guid? expectedLocation)
    {
        await using var database = test.CreateContext();

        var state = await ReadStateAsync(
            database,
            PropertyRegisterKind.Evidence,
            test.ItemId);

        Assert.Equal(expectedStatus, state.Status);
        Assert.Equal(expectedLocation, state.LocationId);

        Assert.Empty(await database.CustodyEvents.ToListAsync());
        Assert.Empty(await database.AuditLogEntries.ToListAsync());
    }

    private static void AssertLinksToItem(
        CustodyEvent custody,
        PropertyRegisterKind register,
        Guid itemId)
    {
        Assert.Equal(
            register == PropertyRegisterKind.Evidence
                ? itemId : (Guid?)null,
            custody.EvidenceItemId);

        Assert.Equal(
            register == PropertyRegisterKind.FoundProperty
                ? itemId : (Guid?)null,
            custody.FoundPropertyItemId);

        Assert.Equal(
            register == PropertyRegisterKind.Safekeeping
                ? itemId : (Guid?)null,
            custody.SafekeepingItemId);
    }

    private static async Task<ItemState> ReadStateAsync(
        ManifestDbContext database,
        PropertyRegisterKind register,
        Guid itemId)
    {
        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                {
                    var item = await database.EvidenceItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            case PropertyRegisterKind.FoundProperty:
                {
                    var item = await database.FoundPropertyItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            case PropertyRegisterKind.Safekeeping:
                {
                    var item = await database.SafekeepingItems
                        .AsNoTracking()
                        .SingleAsync(x => x.Id == itemId);

                    return new ItemState(
                        item.Status,
                        item.CurrentStorageLocationId);
                }

            default:
                throw new InvalidOperationException("Unknown register.");
        }
    }

    private sealed record ItemState(
        EvidenceStatus Status,
        Guid? LocationId);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private readonly DbContextOptions<ManifestDbContext> _options;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid SourceId { get; } = Guid.NewGuid();
        public Guid DestinationId { get; } = Guid.NewGuid();

        private TestDatabase()
        {
            _connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");

            _options = new DbContextOptionsBuilder<ManifestDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public ManifestDbContext CreateContext()
        {
            return new ManifestDbContext(_options);
        }

        public static async Task<TestDatabase> CreateAsync(
            PropertyRegisterKind register,
            EvidenceStatus status = EvidenceStatus.InCustody)
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
                    Username = "test.custodian",
                    DisplayName = "Test Custodian",
                    Role = UserRole.EvidenceCustodian,
                    IsActive = true
                });

                database.StorageLocations.AddRange(
                    new StorageLocation
                    {
                        Id = test.SourceId,
                        Name = "Test Source",
                        IsActive = true
                    },
                    new StorageLocation
                    {
                        Id = test.DestinationId,
                        Name = "Test Destination",
                        IsActive = true
                    });

                Guid? locationId =
                    status == EvidenceStatus.InCustody
                        ? test.SourceId
                        : null;

                switch (register)
                {
                    case PropertyRegisterKind.Evidence:
                        database.EvidenceItems.Add(new EvidenceItem
                        {
                            Id = test.ItemId,
                            CaseNumber = "TEST-E001",
                            ItemNumber = "1",
                            Description = "Fictional evidence item",
                            CollectedBy = "Test Officer",
                            Status = status,
                            CurrentStorageLocationId = locationId
                        });
                        break;

                    case PropertyRegisterKind.FoundProperty:
                        database.FoundPropertyItems.Add(new FoundPropertyItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-F001",
                            ItemNumber = "1",
                            Description = "Fictional found-property item",
                            CollectedBy = "Test Officer",
                            Status = status,
                            CurrentStorageLocationId = locationId
                        });
                        break;

                    case PropertyRegisterKind.Safekeeping:
                        database.SafekeepingItems.Add(new SafekeepingItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-S001",
                            ItemNumber = "1",
                            Description = "Fictional safekeeping item",
                            CollectedBy = "Test Officer",
                            HoldReason = "Development test",
                            HoldAuthority = "Fictional test reference",
                            Status = status,
                            CurrentStorageLocationId = locationId
                        });
                        break;

                    default:
                        throw new InvalidOperationException(
                            "Unknown register.");
                }

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