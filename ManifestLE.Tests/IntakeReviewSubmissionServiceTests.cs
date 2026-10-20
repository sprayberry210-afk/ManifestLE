using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class IntakeReviewSubmissionServiceTests
{
    [Theory]
    [InlineData(PropertyRegisterKind.Evidence)]
    [InlineData(PropertyRegisterKind.FoundProperty)]
    [InlineData(PropertyRegisterKind.Safekeeping)]
    public async Task TemporaryIntakeSavesItemCustodyReviewAndAudit(
        PropertyRegisterKind register)
    {
        await using var test = await TestDatabase.CreateAsync();
        Guid itemId = Guid.NewGuid();
        Guid reviewId;

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            AddItem(database, register, itemId, test.TemporaryId);

            AddCustody(
                database,
                register,
                itemId,
                test.UserId,
                test.TemporaryId);

            var review = await IntakeReviewSubmissionService.AddAsync(
                database,
                test.UserId,
                register,
                itemId);

            reviewId = review.Id;

            await database.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = test.CreateContext();

        var savedReview = await verify.IntakeReviews.SingleAsync();
        var custody = await verify.CustodyEvents.SingleAsync();
        var audit = await verify.AuditLogEntries.SingleAsync();

        Assert.Equal(1, await CountItemsAsync(verify));
        Assert.Equal(reviewId, savedReview.Id);
        Assert.Equal(IntakeReviewStatus.PendingReview, savedReview.Status);
        Assert.Equal(test.UserId, savedReview.SubmittedByUserId);
        Assert.Equal(savedReview.SubmittedAt, savedReview.LastSubmittedAt);
        Assert.Equal(0, savedReview.Revision);
        Assert.Null(savedReview.LastReviewedByUserId);
        Assert.Null(savedReview.LastReviewedAt);
        Assert.Null(savedReview.AcceptedByUserId);
        Assert.Null(savedReview.AcceptedAt);

        Assert.Equal(
            register == PropertyRegisterKind.Evidence
                ? (Guid?)itemId : null,
            savedReview.EvidenceItemId);

        Assert.Equal(
            register == PropertyRegisterKind.FoundProperty
                ? (Guid?)itemId : null,
            savedReview.FoundPropertyItemId);

        Assert.Equal(
            register == PropertyRegisterKind.Safekeeping
                ? (Guid?)itemId : null,
            savedReview.SafekeepingItemId);

        Assert.Equal(CustodyEventType.Intake, custody.EventType);
        Assert.Equal(test.TemporaryId, custody.ToStorageLocationId);
        Assert.Equal(test.UserId, custody.RecordedByUserId);

        Assert.Equal("IntakeReview.Submitted", audit.Action);
        Assert.Equal(itemId, audit.EntityId);
        Assert.Equal(test.UserId, audit.UserId);
        Assert.Contains(reviewId.ToString(), audit.Details);
    }

    [Fact]
    public async Task FailedCustodySaveLeavesNoPartialSubmission()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await IntakeReviewSubmissionService.AddAsync(
                database,
                test.UserId,
                PropertyRegisterKind.Evidence,
                itemId);

            // An unknown recording user causes a foreign-key failure.
            AddCustody(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                Guid.NewGuid(),
                test.TemporaryId);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => database.SaveChangesAsync());

            await transaction.RollbackAsync();
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Fact]
    public async Task SubmissionRequiresAnExistingTransaction()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData(UserRole.Administrator)]
    public async Task PermanentStorageCannotEnterTemporaryReviewQueue(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.PermanentId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Fact]
    public async Task InactiveUserCannotSubmitReview()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var setup = test.CreateContext())
        {
            var user = await setup.Users.SingleAsync();
            user.IsActive = false;
            await setup.SaveChangesAsync();
        }

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Fact]
    public async Task InactiveLockerCannotReceiveSubmission()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var setup = test.CreateContext())
        {
            var locker = await setup.StorageLocations.SingleAsync(
                x => x.Id == test.TemporaryId);

            locker.IsActive = false;
            await setup.SaveChangesAsync();
        }

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Fact]
    public async Task ExistingItemCannotBeSilentlyAddedToQueue()
    {
        await using var test = await TestDatabase.CreateAsync();
        Guid itemId = Guid.NewGuid();

        await using (var setup = test.CreateContext())
        {
            AddItem(
                setup,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await setup.SaveChangesAsync();
        }

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));
        }

        await AssertCountsAsync(test, expectedItems: 1);
    }

    [Fact]
    public async Task SameNewItemCannotReceiveTwoPendingReviews()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync();

            Guid itemId = Guid.NewGuid();

            AddItem(
                database,
                PropertyRegisterKind.Evidence,
                itemId,
                test.TemporaryId);

            await IntakeReviewSubmissionService.AddAsync(
                database,
                test.UserId,
                PropertyRegisterKind.Evidence,
                itemId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                IntakeReviewSubmissionService.AddAsync(
                    database,
                    test.UserId,
                    PropertyRegisterKind.Evidence,
                    itemId));

            Assert.Single(database.IntakeReviews.Local);
            Assert.Single(database.AuditLogEntries.Local);

            await transaction.RollbackAsync();
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    [Fact]
    public async Task DatabaseRejectsReviewWithoutPropertyReference()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            database.IntakeReviews.Add(new IntakeReview
            {
                SubmittedByUserId = test.UserId,
                Status = IntakeReviewStatus.PendingReview
            });

            await Assert.ThrowsAsync<DbUpdateException>(
                () => database.SaveChangesAsync());
        }

        await AssertCountsAsync(test, expectedItems: 0);
    }

    private static void AddItem(
        ManifestDbContext database,
        PropertyRegisterKind register,
        Guid itemId,
        Guid locationId)
    {
        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                database.EvidenceItems.Add(new EvidenceItem
                {
                    Id = itemId,
                    CaseNumber = "TEST-REVIEW-E",
                    ItemNumber = "1",
                    Description = "Fictional review test evidence",
                    CollectedBy = "Test Officer",
                    Status = EvidenceStatus.InCustody,
                    CurrentStorageLocationId = locationId
                });
                break;

            case PropertyRegisterKind.FoundProperty:
                database.FoundPropertyItems.Add(new FoundPropertyItem
                {
                    Id = itemId,
                    ReportNumber = "TEST-REVIEW-F",
                    ItemNumber = "1",
                    Description = "Fictional review test found property",
                    CollectedBy = "Test Officer",
                    Status = EvidenceStatus.InCustody,
                    CurrentStorageLocationId = locationId
                });
                break;

            case PropertyRegisterKind.Safekeeping:
                database.SafekeepingItems.Add(new SafekeepingItem
                {
                    Id = itemId,
                    ReportNumber = "TEST-REVIEW-S",
                    ItemNumber = "1",
                    Description = "Fictional review test safekeeping",
                    CollectedBy = "Test Officer",
                    HoldReason = "Development test",
                    HoldAuthority = "Fictional test reference",
                    Status = EvidenceStatus.InCustody,
                    CurrentStorageLocationId = locationId
                });
                break;

            default:
                throw new InvalidOperationException("Unknown register.");
        }
    }

    private static void AddCustody(
        ManifestDbContext database,
        PropertyRegisterKind register,
        Guid itemId,
        Guid recordedByUserId,
        Guid locationId)
    {
        var custody = new CustodyEvent
        {
            EventType = CustodyEventType.Intake,
            RecordedByUserId = recordedByUserId,
            FromCustodian = "Test Collector",
            ToCustodian = "Test Officer",
            ToStorageLocationId = locationId,
            Reason = "Fictional submission test"
        };

        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                custody.EvidenceItemId = itemId;
                break;

            case PropertyRegisterKind.FoundProperty:
                custody.FoundPropertyItemId = itemId;
                break;

            case PropertyRegisterKind.Safekeeping:
                custody.SafekeepingItemId = itemId;
                break;
        }

        database.CustodyEvents.Add(custody);
    }

    private static async Task<int> CountItemsAsync(
        ManifestDbContext database)
    {
        return await database.EvidenceItems.CountAsync()
            + await database.FoundPropertyItems.CountAsync()
            + await database.SafekeepingItems.CountAsync();
    }

    private static async Task AssertCountsAsync(
        TestDatabase test,
        int expectedItems)
    {
        await using var database = test.CreateContext();

        Assert.Equal(expectedItems, await CountItemsAsync(database));
        Assert.Equal(0, await database.IntakeReviews.CountAsync());
        Assert.Equal(0, await database.CustodyEvents.CountAsync());
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:;Foreign Keys=True");

        public Guid UserId { get; } = Guid.NewGuid();

        public Guid TemporaryId { get; } = Guid.NewGuid();

        public Guid PermanentId { get; } = Guid.NewGuid();

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
                    Username = "test.review.officer",
                    DisplayName = "Test Review Officer",
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