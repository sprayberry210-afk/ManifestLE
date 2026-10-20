using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class IntakeCorrectionServiceTests
{
    [Theory]
    [InlineData(PropertyRegisterKind.Evidence)]
    [InlineData(PropertyRegisterKind.FoundProperty)]
    [InlineData(PropertyRegisterKind.Safekeeping)]
    public async Task OwnerCanCorrectAndResubmitWithoutMovingProperty(
        PropertyRegisterKind register)
    {
        await using var test = await TestDatabase.CreateAsync(register);

        var service = new IntakeCorrectionService(test.CreateContext);

        var details = await service.GetAsync(
            test.OwnerId,
            test.ReviewId);

        Assert.Equal("Original wallet description", details.Description);
        Assert.Equal("Evidence envelope", details.Packaging);
        Assert.Equal("Clarify the wallet color.", details.CorrectionInstructions);

        await service.ResubmitAsync(
            test.OwnerId,
            test.ReviewId,
            details.Revision,
            "  Fictional brown training wallet  ",
            "Paper bag",
            "  Corrected training documentation  ");

        await using var database = test.CreateContext();

        var item = await test.GetItemAsync(database);
        var review = await database.IntakeReviews.SingleAsync();
        var audit = await database.AuditLogEntries.SingleAsync();

        Assert.Equal("Fictional brown training wallet", item.Description);
        Assert.Equal("Paper bag", item.Packaging);
        Assert.Equal("Corrected training documentation", item.Notes);
        Assert.Equal("TEST-CORRECTION", item.ReportNumber);
        Assert.Equal("1", item.ItemNumber);
        Assert.Equal("Test Owner", item.CollectedBy);
        Assert.Equal(EvidenceStatus.InCustody, item.Status);
        Assert.Equal(test.TemporaryId, item.LocationId);

        Assert.Equal(IntakeReviewStatus.Resubmitted, review.Status);
        Assert.Equal(2, review.Revision);
        Assert.Equal(test.OwnerId, review.SubmittedByUserId);
        Assert.Equal(test.OriginalSubmittedAt, review.SubmittedAt);
        Assert.True(review.LastSubmittedAt > test.OriginalSubmittedAt);
        Assert.Equal(test.ReviewerId, review.LastReviewedByUserId);
        Assert.Equal(test.OriginalReviewedAt, review.LastReviewedAt);
        Assert.Equal(
            "Clarify the wallet color.",
            review.CorrectionInstructions);

        Assert.Null(review.AcceptedByUserId);
        Assert.Null(review.AcceptedAt);

        Assert.Equal(1, await database.CustodyEvents.CountAsync());
        Assert.Equal("IntakeReview.Resubmitted", audit.Action);
        Assert.Equal(test.OwnerId, audit.UserId);
        Assert.Equal(test.ItemId, audit.EntityId);
        Assert.Contains("Original wallet description", audit.Details);
        Assert.Contains("Fictional brown training wallet", audit.Details);
        Assert.Contains("Evidence envelope", audit.Details);
        Assert.Contains("Paper bag", audit.Details);
        Assert.Contains("Original training notes", audit.Details);
        Assert.Contains("Corrected training documentation", audit.Details);
    }

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData(UserRole.Administrator)]
    public async Task AnotherUserCannotReadOrResubmitCorrection(
        UserRole otherRole)
    {
        await using var test = await TestDatabase.CreateAsync(
            otherRole: otherRole);

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAsync(test.OtherId, test.ReviewId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OtherId,
                test.ReviewId,
                1,
                "Unauthorized change",
                "Paper bag",
                ""));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task InactiveOwnerCannotReadOrResubmitCorrection()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var owner = await database.Users.SingleAsync(
                x => x.Id == test.OwnerId);

            owner.IsActive = false;
            await database.SaveChangesAsync();
        }

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAsync(test.OwnerId, test.ReviewId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "Changed description",
                "Paper bag",
                ""));

        await AssertUnchangedAsync(test);
    }

    [Theory]
    [InlineData(IntakeReviewStatus.PendingReview)]
    [InlineData(IntakeReviewStatus.Resubmitted)]
    [InlineData(IntakeReviewStatus.Accepted)]
    public async Task OnlyNeedsCorrectionCanBeEdited(
        IntakeReviewStatus status)
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var review = await database.IntakeReviews.SingleAsync();
            review.Status = status;

            if (status == IntakeReviewStatus.PendingReview)
            {
                review.LastReviewedByUserId = null;
                review.LastReviewedAt = null;
            }

            if (status == IntakeReviewStatus.Accepted)
            {
                review.AcceptedByUserId = test.ReviewerId;
                review.AcceptedAt = test.OriginalReviewedAt;
            }

            await database.SaveChangesAsync();
        }

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetAsync(test.OwnerId, test.ReviewId));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "Changed description",
                "Paper bag",
                ""));

        await using var verify = test.CreateContext();

        var savedReview = await verify.IntakeReviews.SingleAsync();
        var item = await test.GetItemAsync(verify);

        Assert.Equal(status, savedReview.Status);
        Assert.Equal(1, savedReview.Revision);
        Assert.Equal("Original wallet description", item.Description);
        Assert.Equal(0, await verify.AuditLogEntries.CountAsync());
        Assert.Equal(1, await verify.CustodyEvents.CountAsync());
    }

    [Fact]
    public async Task StaleRevisionCannotOverwriteSubmission()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                0,
                "Changed description",
                "Paper bag",
                ""));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task BlankDescriptionIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "   ",
                "Paper bag",
                ""));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task UnknownPackagingIsRejected()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "Corrected description",
                "Unknown packaging",
                ""));

        await AssertUnchangedAsync(test);
    }

    [Theory]
    [InlineData("Other / see notes")]
    [InlineData("Unpackaged / see notes")]
    public async Task SpecialPackagingRequiresNotes(string packaging)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "Corrected description",
                packaging,
                "   "));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task AuditFailureRollsBackCorrectionAndResubmission()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            await database.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER RejectCorrectionAudit
                BEFORE INSERT ON "AuditLogEntries"
                BEGIN
                    SELECT RAISE(ABORT, 'Test audit failure');
                END;
                """);
        }

        var service = new IntakeCorrectionService(test.CreateContext);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                1,
                "Changed description",
                "Paper bag",
                "Changed notes"));

        await AssertUnchangedAsync(test);
    }

    [Fact]
    public async Task ResubmissionCannotBeRepeatedWithoutAnotherReview()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeCorrectionService(test.CreateContext);

        await service.ResubmitAsync(
            test.OwnerId,
            test.ReviewId,
            1,
            "First corrected description",
            "Paper bag",
            "First corrected notes");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResubmitAsync(
                test.OwnerId,
                test.ReviewId,
                2,
                "Second unreviewed change",
                "Evidence box",
                ""));

        await using var database = test.CreateContext();

        var item = await test.GetItemAsync(database);
        var review = await database.IntakeReviews.SingleAsync();

        Assert.Equal("First corrected description", item.Description);
        Assert.Equal("Paper bag", item.Packaging);
        Assert.Equal(IntakeReviewStatus.Resubmitted, review.Status);
        Assert.Equal(2, review.Revision);
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
        Assert.Equal(1, await database.CustodyEvents.CountAsync());
    }

    private static async Task AssertUnchangedAsync(TestDatabase test)
    {
        await using var database = test.CreateContext();

        var item = await test.GetItemAsync(database);
        var review = await database.IntakeReviews.SingleAsync();

        Assert.Equal("Original wallet description", item.Description);
        Assert.Equal("Evidence envelope", item.Packaging);
        Assert.Equal("Original training notes", item.Notes);
        Assert.Equal(test.TemporaryId, item.LocationId);
        Assert.Equal(EvidenceStatus.InCustody, item.Status);

        Assert.Equal(IntakeReviewStatus.NeedsCorrection, review.Status);
        Assert.Equal(1, review.Revision);
        Assert.Equal(test.OriginalSubmittedAt, review.LastSubmittedAt);
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());
        Assert.Equal(1, await database.CustodyEvents.CountAsync());
    }

    private sealed record ItemSnapshot(
        string ReportNumber,
        string ItemNumber,
        string Description,
        string Packaging,
        string Notes,
        string CollectedBy,
        EvidenceStatus Status,
        Guid? LocationId);

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:;Foreign Keys=True");

        private PropertyRegisterKind _register;

        public Guid OwnerId { get; } = Guid.NewGuid();
        public Guid OtherId { get; } = Guid.NewGuid();
        public Guid ReviewerId { get; } = Guid.NewGuid();
        public Guid ReviewId { get; } = Guid.NewGuid();
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid TemporaryId { get; } = Guid.NewGuid();

        public DateTimeOffset OriginalSubmittedAt { get; } =
            new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset OriginalReviewedAt { get; } =
            new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public async Task<ItemSnapshot> GetItemAsync(
            ManifestDbContext database)
        {
            switch (_register)
            {
                case PropertyRegisterKind.Evidence:
                    {
                        var item = await database.EvidenceItems.SingleAsync();

                        return new ItemSnapshot(
                            item.CaseNumber, item.ItemNumber,
                            item.Description, item.Packaging, item.Notes,
                            item.CollectedBy, item.Status,
                            item.CurrentStorageLocationId);
                    }

                case PropertyRegisterKind.FoundProperty:
                    {
                        var item = await database.FoundPropertyItems.SingleAsync();

                        return new ItemSnapshot(
                            item.ReportNumber, item.ItemNumber,
                            item.Description, item.Packaging, item.Notes,
                            item.CollectedBy, item.Status,
                            item.CurrentStorageLocationId);
                    }

                case PropertyRegisterKind.Safekeeping:
                    {
                        var item = await database.SafekeepingItems.SingleAsync();

                        return new ItemSnapshot(
                            item.ReportNumber, item.ItemNumber,
                            item.Description, item.Packaging, item.Notes,
                            item.CollectedBy, item.Status,
                            item.CurrentStorageLocationId);
                    }

                default:
                    throw new InvalidOperationException();
            }
        }

        public static async Task<TestDatabase> CreateAsync(
            PropertyRegisterKind register = PropertyRegisterKind.Evidence,
            UserRole otherRole = UserRole.Officer)
        {
            var test = new TestDatabase
            {
                _register = register
            };

            try
            {
                await test._connection.OpenAsync();

                await using var database = test.CreateContext();
                await database.Database.EnsureCreatedAsync();

                database.Users.AddRange(
                    new AppUser
                    {
                        Id = test.OwnerId,
                        Username = "test.correction.owner",
                        DisplayName = "Test Owner",
                        Role = UserRole.Officer,
                        IsActive = true
                    },
                    new AppUser
                    {
                        Id = test.OtherId,
                        Username = "test.correction.other",
                        DisplayName = "Other User",
                        Role = otherRole,
                        IsActive = true
                    },
                    new AppUser
                    {
                        Id = test.ReviewerId,
                        Username = "test.correction.reviewer",
                        DisplayName = "Test Reviewer",
                        Role = UserRole.Administrator,
                        IsActive = true
                    });

                database.StorageLocations.Add(new StorageLocation
                {
                    Id = test.TemporaryId,
                    Name = "Temporary Locker 01",
                    LocationType = StorageLocationType.Temporary
                });

                var review = new IntakeReview
                {
                    Id = test.ReviewId,
                    SubmittedByUserId = test.OwnerId,
                    SubmittedAt = test.OriginalSubmittedAt,
                    LastSubmittedAt = test.OriginalSubmittedAt,
                    Status = IntakeReviewStatus.NeedsCorrection,
                    CorrectionInstructions = "Clarify the wallet color.",
                    LastReviewedByUserId = test.ReviewerId,
                    LastReviewedAt = test.OriginalReviewedAt,
                    Revision = 1
                };

                var custody = new CustodyEvent
                {
                    EventType = CustodyEventType.Intake,
                    RecordedByUserId = test.OwnerId,
                    FromCustodian = "Test Owner",
                    ToCustodian = "Test Owner",
                    ToStorageLocationId = test.TemporaryId,
                    Reason = "Fictional correction test intake"
                };

                switch (register)
                {
                    case PropertyRegisterKind.Evidence:
                        database.EvidenceItems.Add(new EvidenceItem
                        {
                            Id = test.ItemId,
                            CaseNumber = "TEST-CORRECTION",
                            ItemNumber = "1",
                            Description = "Original wallet description",
                            Packaging = "Evidence envelope",
                            Notes = "Original training notes",
                            CollectedBy = "Test Owner",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.EvidenceItemId = test.ItemId;
                        custody.EvidenceItemId = test.ItemId;
                        break;

                    case PropertyRegisterKind.FoundProperty:
                        database.FoundPropertyItems.Add(new FoundPropertyItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-CORRECTION",
                            ItemNumber = "1",
                            Description = "Original wallet description",
                            Packaging = "Evidence envelope",
                            Notes = "Original training notes",
                            CollectedBy = "Test Owner",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.FoundPropertyItemId = test.ItemId;
                        custody.FoundPropertyItemId = test.ItemId;
                        break;

                    case PropertyRegisterKind.Safekeeping:
                        database.SafekeepingItems.Add(new SafekeepingItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-CORRECTION",
                            ItemNumber = "1",
                            Description = "Original wallet description",
                            Packaging = "Evidence envelope",
                            Notes = "Original training notes",
                            CollectedBy = "Test Owner",
                            HoldReason = "Development test",
                            HoldAuthority = "Fictional test reference",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.SafekeepingItemId = test.ItemId;
                        custody.SafekeepingItemId = test.ItemId;
                        break;
                }

                database.IntakeReviews.Add(review);
                database.CustodyEvents.Add(custody);

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