using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class IntakeReviewDecisionServiceTests
{
    [Theory]
    [InlineData(PropertyRegisterKind.Evidence)]
    [InlineData(PropertyRegisterKind.FoundProperty)]
    [InlineData(PropertyRegisterKind.Safekeeping)]
    public async Task AcceptanceMovesItemAndRecordsDecisionTogether(
        PropertyRegisterKind register)
    {
        await using var test = await TestDatabase.CreateAsync(register);

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await service.AcceptAsync(
            test.ReviewerId,
            test.ReviewId,
            0,
            test.TemporaryId,
            test.PermanentId,
            "Training receipt notes");

        await using var database = test.CreateContext();

        var review = await database.IntakeReviews.SingleAsync();

        Assert.Equal(IntakeReviewStatus.Accepted, review.Status);
        Assert.Equal(1, review.Revision);
        Assert.Equal(test.ReviewerId, review.AcceptedByUserId);
        Assert.Equal(test.ReviewerId, review.LastReviewedByUserId);
        Assert.NotNull(review.AcceptedAt);
        Assert.Equal(review.AcceptedAt, review.LastReviewedAt);

        Assert.Equal(
            test.PermanentId,
            await test.GetLocationAsync(database));

        Assert.Equal(2, await database.CustodyEvents.CountAsync());

        var movement = await database.CustodyEvents.SingleAsync(
            x => x.EventType == CustodyEventType.Transfer);

        Assert.Equal(test.TemporaryId, movement.FromStorageLocationId);
        Assert.Equal(test.PermanentId, movement.ToStorageLocationId);
        Assert.Equal(test.ReviewerId, movement.RecordedByUserId);
        Assert.Equal("Test Reviewer", movement.ToCustodian);
        Assert.Equal("Training receipt notes", movement.Notes);

        Assert.Equal(2, await database.AuditLogEntries.CountAsync());

        Assert.True(await database.AuditLogEntries.AnyAsync(
            x => x.Action == "Custody.Transfer" &&
                 x.EntityId == test.ItemId &&
                 x.UserId == test.ReviewerId));

        Assert.True(await database.AuditLogEntries.AnyAsync(
            x => x.Action == "IntakeReview.Accepted" &&
                 x.EntityId == test.ItemId &&
                 x.UserId == test.ReviewerId));
    }

    [Fact]
    public async Task CorrectionRequestPreservesPhysicalCustody()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await service.RequestCorrectionAsync(
            test.ReviewerId,
            test.ReviewId,
            0,
            "  Correct the packaging description.  ");

        await using var database = test.CreateContext();

        var review = await database.IntakeReviews.SingleAsync();

        Assert.Equal(IntakeReviewStatus.NeedsCorrection, review.Status);
        Assert.Equal(
            "Correct the packaging description.",
            review.CorrectionInstructions);

        Assert.Equal(1, review.Revision);
        Assert.Equal(test.ReviewerId, review.LastReviewedByUserId);
        Assert.NotNull(review.LastReviewedAt);
        Assert.Null(review.AcceptedByUserId);
        Assert.Null(review.AcceptedAt);

        Assert.Equal(
            test.TemporaryId,
            await test.GetLocationAsync(database));

        // Only the original intake event remains.
        Assert.Equal(1, await database.CustodyEvents.CountAsync());

        var audit = await database.AuditLogEntries.SingleAsync();

        Assert.Equal("IntakeReview.CorrectionRequested", audit.Action);
        Assert.Equal(test.ReviewerId, audit.UserId);
        Assert.Contains(
            "Correct the packaging description.",
            audit.Details);
    }

    [Theory]
    [InlineData(UserRole.Officer)]
    [InlineData((UserRole)999)]
    public async Task UnauthorizedRoleCannotMakeEitherDecision(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(
            reviewerRole: role);

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestCorrectionAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                "Unauthorized correction"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                test.PermanentId,
                ""));

        await AssertPendingUnchangedAsync(test);
    }

    [Fact]
    public async Task InactiveReviewerCannotMakeEitherDecision()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var reviewer = await database.Users.SingleAsync(
                x => x.Id == test.ReviewerId);

            reviewer.IsActive = false;
            await database.SaveChangesAsync();
        }

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestCorrectionAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                "Correction instructions"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                test.PermanentId,
                ""));

        await AssertPendingUnchangedAsync(test);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleRevisionCannotMakeDecision(bool accept)
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewDecisionService(test.CreateContext);

        if (accept)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AcceptAsync(
                    test.ReviewerId,
                    test.ReviewId,
                    99,
                    test.TemporaryId,
                    test.PermanentId,
                    ""));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RequestCorrectionAsync(
                    test.ReviewerId,
                    test.ReviewId,
                    99,
                    "Correction instructions"));
        }

        await AssertPendingUnchangedAsync(test);
    }

    [Theory]
    [InlineData(IntakeReviewStatus.NeedsCorrection)]
    [InlineData(IntakeReviewStatus.Accepted)]
    public async Task NonReviewableStatusRejectsBothDecisions(
        IntakeReviewStatus status)
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var review = await database.IntakeReviews.SingleAsync();

            review.Status = status;
            review.LastReviewedByUserId = test.ReviewerId;
            review.LastReviewedAt = DateTimeOffset.UtcNow;

            if (status == IntakeReviewStatus.NeedsCorrection)
            {
                review.CorrectionInstructions = "Existing instructions";
            }
            else
            {
                review.AcceptedByUserId = test.ReviewerId;
                review.AcceptedAt = review.LastReviewedAt;
            }

            await database.SaveChangesAsync();
        }

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestCorrectionAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                "Replacement instructions"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                test.PermanentId,
                ""));

        await using var verify = test.CreateContext();

        var saved = await verify.IntakeReviews.SingleAsync();

        Assert.Equal(status, saved.Status);
        Assert.Equal(0, saved.Revision);
        Assert.Equal(0, await verify.AuditLogEntries.CountAsync());
        Assert.Equal(1, await verify.CustodyEvents.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptanceRequiresActivePermanentDestination(
        bool inactivePermanent)
    {
        await using var test = await TestDatabase.CreateAsync();

        Guid destinationId = test.TemporaryId;

        if (inactivePermanent)
        {
            await using var database = test.CreateContext();

            var location = await database.StorageLocations.SingleAsync(
                x => x.Id == test.PermanentId);

            location.IsActive = false;
            await database.SaveChangesAsync();

            destinationId = test.PermanentId;
        }

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                destinationId,
                ""));

        await AssertPendingUnchangedAsync(test);
    }

    [Fact]
    public async Task ChangedTemporaryLocationRejectsStaleReceipt()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var item = await database.EvidenceItems.SingleAsync();
            item.CurrentStorageLocationId = test.OtherTemporaryId;
            await database.SaveChangesAsync();
        }

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                test.PermanentId,
                ""));

        await using var verify = test.CreateContext();

        var review = await verify.IntakeReviews.SingleAsync();

        Assert.Equal(IntakeReviewStatus.PendingReview, review.Status);
        Assert.Equal(
            test.OtherTemporaryId,
            await test.GetLocationAsync(verify));

        Assert.Equal(1, await verify.CustodyEvents.CountAsync());
        Assert.Equal(0, await verify.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task BlankCorrectionInstructionsAreRejected()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RequestCorrectionAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                "   "));

        await AssertPendingUnchangedAsync(test);
    }

    [Fact]
    public async Task AuditSaveFailureRollsBackAcceptanceAndMovement()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            await database.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER RejectTestAudit
                BEFORE INSERT ON "AuditLogEntries"
                BEGIN
                    SELECT RAISE(ABORT, 'Test audit failure');
                END;
                """);
        }

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                0,
                test.TemporaryId,
                test.PermanentId,
                "Test receipt"));

        await AssertPendingUnchangedAsync(test);
    }

    [Fact]
    public async Task AcceptedReviewCannotCreateDuplicateMovement()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewDecisionService(test.CreateContext);

        await service.AcceptAsync(
            test.ReviewerId,
            test.ReviewId,
            0,
            test.TemporaryId,
            test.PermanentId,
            "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AcceptAsync(
                test.ReviewerId,
                test.ReviewId,
                1,
                test.TemporaryId,
                test.PermanentId,
                ""));

        await using var database = test.CreateContext();

        Assert.Equal(2, await database.CustodyEvents.CountAsync());
        Assert.Equal(2, await database.AuditLogEntries.CountAsync());

        var review = await database.IntakeReviews.SingleAsync();
        Assert.Equal(1, review.Revision);
    }

    private static async Task AssertPendingUnchangedAsync(
        TestDatabase test)
    {
        await using var database = test.CreateContext();

        var review = await database.IntakeReviews.SingleAsync();

        Assert.Equal(IntakeReviewStatus.PendingReview, review.Status);
        Assert.Equal(0, review.Revision);
        Assert.Equal(string.Empty, review.CorrectionInstructions);
        Assert.Null(review.LastReviewedByUserId);
        Assert.Null(review.LastReviewedAt);
        Assert.Null(review.AcceptedByUserId);
        Assert.Null(review.AcceptedAt);

        Assert.Equal(
            test.TemporaryId,
            await test.GetLocationAsync(database));

        Assert.Equal(1, await database.CustodyEvents.CountAsync());
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:;Foreign Keys=True");

        private PropertyRegisterKind _register;

        public Guid ReviewerId { get; } = Guid.NewGuid();
        public Guid OfficerId { get; } = Guid.NewGuid();
        public Guid ReviewId { get; } = Guid.NewGuid();
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid TemporaryId { get; } = Guid.NewGuid();
        public Guid OtherTemporaryId { get; } = Guid.NewGuid();
        public Guid PermanentId { get; } = Guid.NewGuid();

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public async Task<Guid?> GetLocationAsync(
            ManifestDbContext database)
        {
            return _register switch
            {
                PropertyRegisterKind.Evidence =>
                    (await database.EvidenceItems.SingleAsync())
                        .CurrentStorageLocationId,

                PropertyRegisterKind.FoundProperty =>
                    (await database.FoundPropertyItems.SingleAsync())
                        .CurrentStorageLocationId,

                PropertyRegisterKind.Safekeeping =>
                    (await database.SafekeepingItems.SingleAsync())
                        .CurrentStorageLocationId,

                _ => throw new InvalidOperationException()
            };
        }

        public static async Task<TestDatabase> CreateAsync(
            PropertyRegisterKind register = PropertyRegisterKind.Evidence,
            UserRole reviewerRole = UserRole.Administrator)
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
                        Id = test.ReviewerId,
                        Username = "test.decision.reviewer",
                        DisplayName = "Test Reviewer",
                        Role = reviewerRole,
                        IsActive = true
                    },
                    new AppUser
                    {
                        Id = test.OfficerId,
                        Username = "test.decision.officer",
                        DisplayName = "Test Officer",
                        Role = UserRole.Officer,
                        IsActive = true
                    });

                database.StorageLocations.AddRange(
                    new StorageLocation
                    {
                        Id = test.TemporaryId,
                        Name = "Temporary Locker 01",
                        LocationType = StorageLocationType.Temporary
                    },
                    new StorageLocation
                    {
                        Id = test.OtherTemporaryId,
                        Name = "Temporary Locker 02",
                        LocationType = StorageLocationType.Temporary
                    },
                    new StorageLocation
                    {
                        Id = test.PermanentId,
                        Name = "Permanent Cabinet A",
                        LocationType = StorageLocationType.Permanent
                    });

                var review = new IntakeReview
                {
                    Id = test.ReviewId,
                    SubmittedByUserId = test.OfficerId,
                    Status = IntakeReviewStatus.PendingReview
                };

                var intake = new CustodyEvent
                {
                    EventType = CustodyEventType.Intake,
                    RecordedByUserId = test.OfficerId,
                    FromCustodian = "Test Officer",
                    ToCustodian = "Test Officer",
                    ToStorageLocationId = test.TemporaryId,
                    Reason = "Fictional intake for decision tests"
                };

                switch (register)
                {
                    case PropertyRegisterKind.Evidence:
                        database.EvidenceItems.Add(new EvidenceItem
                        {
                            Id = test.ItemId,
                            CaseNumber = "TEST-DECISION-E",
                            ItemNumber = "1",
                            Description = "Fictional evidence",
                            CollectedBy = "Test Officer",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.EvidenceItemId = test.ItemId;
                        intake.EvidenceItemId = test.ItemId;
                        break;

                    case PropertyRegisterKind.FoundProperty:
                        database.FoundPropertyItems.Add(new FoundPropertyItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-DECISION-F",
                            ItemNumber = "1",
                            Description = "Fictional found property",
                            CollectedBy = "Test Officer",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.FoundPropertyItemId = test.ItemId;
                        intake.FoundPropertyItemId = test.ItemId;
                        break;

                    case PropertyRegisterKind.Safekeeping:
                        database.SafekeepingItems.Add(new SafekeepingItem
                        {
                            Id = test.ItemId,
                            ReportNumber = "TEST-DECISION-S",
                            ItemNumber = "1",
                            Description = "Fictional safekeeping",
                            CollectedBy = "Test Officer",
                            HoldReason = "Development test",
                            HoldAuthority = "Fictional test reference",
                            Status = EvidenceStatus.InCustody,
                            CurrentStorageLocationId = test.TemporaryId
                        });

                        review.SafekeepingItemId = test.ItemId;
                        intake.SafekeepingItemId = test.ItemId;
                        break;
                }

                database.IntakeReviews.Add(review);
                database.CustodyEvents.Add(intake);

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