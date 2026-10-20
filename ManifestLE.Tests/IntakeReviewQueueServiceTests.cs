using System;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public class IntakeReviewQueueServiceTests
{
    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task AuthorizedStaffSeeAllThreeRegisters(UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);

        var service = new IntakeReviewQueueService(test.CreateContext);
        var rows = await service.GetQueueAsync(test.ActorId);

        Assert.Equal(3, rows.Count);

        Assert.Contains(rows, x =>
            x.Register == PropertyRegisterKind.Evidence);

        Assert.Contains(rows, x =>
            x.Register == PropertyRegisterKind.FoundProperty);

        Assert.Contains(rows, x =>
            x.Register == PropertyRegisterKind.Safekeeping);

        Assert.Contains(rows, x =>
            x.SubmittedByUserId == test.OtherOfficerId);
    }

    [Fact]
    public async Task OfficerSeesOnlyOwnSubmissions()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewQueueService(test.CreateContext);
        var rows = await service.GetQueueAsync(test.ActorId);

        Assert.Equal(2, rows.Count);

        Assert.All(rows, row =>
            Assert.Equal(test.ActorId, row.SubmittedByUserId));

        Assert.Contains(rows, x =>
            x.ReviewId == test.EvidenceReviewId);

        Assert.Contains(rows, x =>
            x.ReviewId == test.AcceptedReviewId);

        Assert.DoesNotContain(rows, x =>
            x.ReviewId == test.OtherOfficerReviewId);

        Assert.DoesNotContain(rows, x =>
            x.CorrectionInstructions ==
            "Other officer correction instructions");
    }

    [Fact]
    public async Task InactiveAccountCannotReadQueue()
    {
        await using var test = await TestDatabase.CreateAsync();

        await using (var database = test.CreateContext())
        {
            var user = await database.Users.SingleAsync(
                x => x.Id == test.ActorId);

            user.IsActive = false;
            await database.SaveChangesAsync();
        }

        var service = new IntakeReviewQueueService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetQueueAsync(test.ActorId));
    }

    [Fact]
    public async Task UnknownAccountCannotReadQueue()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewQueueService(test.CreateContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetQueueAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OfficerSeesRestrictedLabelAfterPermanentStorageReceipt()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewQueueService(test.CreateContext);
        var rows = await service.GetQueueAsync(test.ActorId);

        var accepted = Assert.Single(
            rows.Where(x => x.ReviewId == test.AcceptedReviewId));

        Assert.Equal(IntakeReviewStatus.Accepted, accepted.Status);
        Assert.Equal(StorageDisplayService.RestrictedLabel, accepted.Storage);

        Assert.DoesNotContain("Evidence Room", accepted.Storage);
        Assert.DoesNotContain("Cabinet A", accepted.Storage);

        var pending = Assert.Single(
            rows.Where(x => x.ReviewId == test.EvidenceReviewId));

        Assert.Equal("Temporary Locker 01", pending.Storage);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.EvidenceCustodian)]
    public async Task AuthorizedStaffSeeFullPermanentStoragePath(
        UserRole role)
    {
        await using var test = await TestDatabase.CreateAsync(role);

        var service = new IntakeReviewQueueService(test.CreateContext);
        var rows = await service.GetQueueAsync(test.ActorId);

        var accepted = Assert.Single(
            rows.Where(x => x.ReviewId == test.AcceptedReviewId));

        Assert.Equal(
            "Evidence Room / Cabinet A",
            accepted.Storage);
    }

    [Fact]
    public async Task ReadingQueueDoesNotChangeReviewOrCustodyRecords()
    {
        await using var test = await TestDatabase.CreateAsync(
            UserRole.Administrator);

        var service = new IntakeReviewQueueService(test.CreateContext);

        await service.GetQueueAsync(test.ActorId);
        await service.GetQueueAsync(test.ActorId);

        await using var database = test.CreateContext();

        Assert.Equal(3, await database.IntakeReviews.CountAsync());
        Assert.Equal(0, await database.CustodyEvents.CountAsync());
        Assert.Equal(0, await database.AuditLogEntries.CountAsync());

        var pending = await database.IntakeReviews.SingleAsync(
            x => x.Id == test.EvidenceReviewId);

        var correction = await database.IntakeReviews.SingleAsync(
            x => x.Id == test.OtherOfficerReviewId);

        Assert.Equal(IntakeReviewStatus.PendingReview, pending.Status);
        Assert.Equal(0, pending.Revision);

        Assert.Equal(IntakeReviewStatus.NeedsCorrection, correction.Status);
        Assert.Equal(
            "Other officer correction instructions",
            correction.CorrectionInstructions);
    }

    [Fact]
    public async Task OpenReviewsAppearBeforeAcceptedInSubmissionOrder()
    {
        await using var test = await TestDatabase.CreateAsync(
            UserRole.Administrator);

        var service = new IntakeReviewQueueService(test.CreateContext);
        var rows = await service.GetQueueAsync(test.ActorId);

        Assert.Equal(3, rows.Count);
        Assert.Equal(test.EvidenceReviewId, rows[0].ReviewId);
        Assert.Equal(test.OtherOfficerReviewId, rows[1].ReviewId);
        Assert.Equal(test.AcceptedReviewId, rows[2].ReviewId);

        // The accepted item is older, but belongs below open work.
        Assert.True(rows[2].SubmittedAt < rows[0].SubmittedAt);
    }

    [Fact]
    public async Task AccountDeactivationIsCheckedOnNextQueueLoad()
    {
        await using var test = await TestDatabase.CreateAsync();

        var service = new IntakeReviewQueueService(test.CreateContext);

        var initial = await service.GetQueueAsync(test.ActorId);
        Assert.Equal(2, initial.Count);

        await using (var database = test.CreateContext())
        {
            var user = await database.Users.SingleAsync(
                x => x.Id == test.ActorId);

            user.IsActive = false;
            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetQueueAsync(test.ActorId));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:;Foreign Keys=True");

        public Guid ActorId { get; } = Guid.NewGuid();
        public Guid OtherOfficerId { get; } = Guid.NewGuid();
        public Guid ReviewerId { get; } = Guid.NewGuid();

        public Guid EvidenceReviewId { get; } = Guid.NewGuid();
        public Guid OtherOfficerReviewId { get; } = Guid.NewGuid();
        public Guid AcceptedReviewId { get; } = Guid.NewGuid();

        public ManifestDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ManifestDbContext>()
                    .UseSqlite(_connection)
                    .Options;

            return new ManifestDbContext(options);
        }

        public static async Task<TestDatabase> CreateAsync(
            UserRole actorRole = UserRole.Officer)
        {
            var test = new TestDatabase();

            try
            {
                await test._connection.OpenAsync();

                await using var database = test.CreateContext();
                await database.Database.EnsureCreatedAsync();

                database.Users.AddRange(
                    new AppUser
                    {
                        Id = test.ActorId,
                        Username = "test.queue.actor",
                        DisplayName = "Test Queue Actor",
                        Role = actorRole,
                        IsActive = true
                    },
                    new AppUser
                    {
                        Id = test.OtherOfficerId,
                        Username = "test.queue.other",
                        DisplayName = "Other Test Officer",
                        Role = UserRole.Officer,
                        IsActive = true
                    },
                    new AppUser
                    {
                        Id = test.ReviewerId,
                        Username = "test.queue.reviewer",
                        DisplayName = "Test Reviewer",
                        Role = UserRole.Administrator,
                        IsActive = true
                    });

                var temporary = new StorageLocation
                {
                    Name = "Temporary Locker 01",
                    LocationType = StorageLocationType.Temporary
                };

                var room = new StorageLocation
                {
                    Name = "Evidence Room",
                    LocationType = StorageLocationType.Permanent
                };

                var cabinet = new StorageLocation
                {
                    Name = "Cabinet A",
                    ParentLocationId = room.Id,
                    LocationType = StorageLocationType.Permanent
                };

                database.StorageLocations.AddRange(
                    temporary,
                    room,
                    cabinet);

                var evidence = new EvidenceItem
                {
                    CaseNumber = "TEST-QUEUE-E",
                    ItemNumber = "1",
                    Description = "Fictional pending evidence",
                    CollectedBy = "Test Queue Actor",
                    CurrentStorageLocationId = temporary.Id,
                    Status = EvidenceStatus.InCustody
                };

                var found = new FoundPropertyItem
                {
                    ReportNumber = "TEST-QUEUE-F",
                    ItemNumber = "1",
                    Description = "Fictional correction item",
                    CollectedBy = "Other Test Officer",
                    CurrentStorageLocationId = temporary.Id,
                    Status = EvidenceStatus.InCustody
                };

                var safekeeping = new SafekeepingItem
                {
                    ReportNumber = "TEST-QUEUE-S",
                    ItemNumber = "1",
                    Description = "Fictional accepted safekeeping",
                    CollectedBy = "Test Queue Actor",
                    HoldReason = "Development test",
                    HoldAuthority = "Fictional test reference",
                    CurrentStorageLocationId = cabinet.Id,
                    Status = EvidenceStatus.InCustody
                };

                database.EvidenceItems.Add(evidence);
                database.FoundPropertyItems.Add(found);
                database.SafekeepingItems.Add(safekeeping);

                var firstDay = new DateTimeOffset(
                    2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

                database.IntakeReviews.AddRange(
                    new IntakeReview
                    {
                        Id = test.EvidenceReviewId,
                        EvidenceItemId = evidence.Id,
                        SubmittedByUserId = test.ActorId,
                        SubmittedAt = firstDay.AddDays(1),
                        LastSubmittedAt = firstDay.AddDays(1),
                        Status = IntakeReviewStatus.PendingReview
                    },
                    new IntakeReview
                    {
                        Id = test.OtherOfficerReviewId,
                        FoundPropertyItemId = found.Id,
                        SubmittedByUserId = test.OtherOfficerId,
                        SubmittedAt = firstDay.AddDays(2),
                        LastSubmittedAt = firstDay.AddDays(2),
                        Status = IntakeReviewStatus.NeedsCorrection,
                        CorrectionInstructions =
                            "Other officer correction instructions",
                        LastReviewedByUserId = test.ReviewerId,
                        LastReviewedAt = firstDay.AddDays(3),
                        Revision = 1
                    },
                    new IntakeReview
                    {
                        Id = test.AcceptedReviewId,
                        SafekeepingItemId = safekeeping.Id,
                        SubmittedByUserId = test.ActorId,
                        SubmittedAt = firstDay,
                        LastSubmittedAt = firstDay,
                        Status = IntakeReviewStatus.Accepted,
                        LastReviewedByUserId = test.ReviewerId,
                        LastReviewedAt = firstDay.AddDays(1),
                        AcceptedByUserId = test.ReviewerId,
                        AcceptedAt = firstDay.AddDays(1),
                        Revision = 1
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