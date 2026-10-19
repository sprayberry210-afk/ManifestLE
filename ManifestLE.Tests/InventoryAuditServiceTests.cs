using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public sealed class InventoryAuditServiceTests
{
    [Fact]
    public async Task Start_IncludesChildLocations_AndExcludesCheckedOutItems()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            true,
            "Test audit");

        await using var database = fixture.CreateContext();

        var audit = await database.InventoryAudits
            .Include(x => x.Items)
            .SingleAsync(x => x.Id == auditId);

        Assert.Equal(InventoryAuditStatus.InProgress, audit.Status);
        Assert.Equal(2, audit.Items.Count);

        Assert.Contains(audit.Items, x =>
            x.EvidenceItemId == fixture.RoomItemId);

        Assert.Contains(audit.Items, x =>
            x.EvidenceItemId == fixture.ShelfItemId);

        Assert.DoesNotContain(audit.Items, x =>
            x.EvidenceItemId == fixture.CheckedOutItemId);

        Assert.All(audit.Items, item =>
        {
            Assert.Equal(InventoryAuditResult.Pending, item.Result);
            Assert.Null(item.CheckedByUserId);
            Assert.Null(item.CheckedAt);
        });

        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task Start_WithoutChildren_OnlyIncludesSelectedLocation()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "");

        await using var database = fixture.CreateContext();

        var item = await database.InventoryAuditItems
            .SingleAsync(x => x.InventoryAuditId == auditId);

        Assert.Equal(fixture.RoomItemId, item.EvidenceItemId);
    }

    [Fact]
    public async Task Snapshot_RemainsUnchanged_WhenPropertyMoves()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            true,
            "");

        await using (var database = fixture.CreateContext())
        {
            var evidence = await database.EvidenceItems
                .SingleAsync(x => x.Id == fixture.ShelfItemId);

            evidence.CaseNumber = "CHANGED";
            evidence.Description = "Changed description";
            evidence.CurrentStorageLocationId = fixture.RoomId;

            await database.SaveChangesAsync();
        }

        await using var verification = fixture.CreateContext();

        var snapshot = await verification.InventoryAuditItems
            .SingleAsync(x =>
                x.InventoryAuditId == auditId &&
                x.EvidenceItemId == fixture.ShelfItemId);

        Assert.Equal("TEST-AUDIT", snapshot.ReportNumberSnapshot);
        Assert.Equal("2", snapshot.ItemNumberSnapshot);
        Assert.Equal("Shelf test item", snapshot.DescriptionSnapshot);
        Assert.Equal(fixture.ShelfId, snapshot.ExpectedStorageLocationId);
        Assert.Equal(
            "Evidence Room / Shelf 1",
            snapshot.ExpectedStoragePathSnapshot);
    }

    [Fact]
    public async Task Start_RejectsOverlappingOpenAudit()
    {
        using var fixture = new AuditFixture();

        await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            true,
            "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartAsync(
                fixture.UserId,
                fixture.ShelfId,
                false,
                ""));

        await using var database = fixture.CreateContext();

        Assert.Equal(1, await database.InventoryAudits.CountAsync());
        Assert.Equal(2, await database.InventoryAuditItems.CountAsync());
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Theory]
    [InlineData(UserRole.Officer, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task Start_RejectsUnauthorizedAccount(
        UserRole role,
        bool active)
    {
        using var fixture = new AuditFixture();

        await using (var database = fixture.CreateContext())
        {
            var user = await database.Users
                .SingleAsync(x => x.Id == fixture.UserId);

            user.Role = role;
            user.IsActive = active;

            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.StartAsync(
                fixture.UserId,
                fixture.RoomId,
                true,
                ""));

        await using var verification = fixture.CreateContext();

        Assert.Equal(0, await verification.InventoryAudits.CountAsync());
        Assert.Equal(0, await verification.AuditLogEntries.CountAsync());
    }

    [Theory]
    [InlineData(InventoryAuditResult.Missing)]
    [InlineData(InventoryAuditResult.Discrepancy)]
    public async Task RecordResult_RequiresNotesForProblems(
        InventoryAuditResult result)
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "");

        Guid itemId = await fixture.GetChecklistItemIdAsync(auditId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordResultAsync(
                fixture.UserId,
                auditId,
                itemId,
                result,
                "",
                InventoryAuditResult.Pending,
                null));

        await using var database = fixture.CreateContext();

        var item = await database.InventoryAuditItems
            .SingleAsync(x => x.Id == itemId);

        Assert.Equal(InventoryAuditResult.Pending, item.Result);
        Assert.Null(item.CheckedAt);
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task RecordResult_SavesCheckAndAuditEntryTogether()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "");

        Guid itemId = await fixture.GetChecklistItemIdAsync(auditId);

        await fixture.Service.RecordResultAsync(
            fixture.UserId,
            auditId,
            itemId,
            InventoryAuditResult.Verified,
            "Seal and label checked",
            InventoryAuditResult.Pending,
            null);

        await using var database = fixture.CreateContext();

        var item = await database.InventoryAuditItems
            .SingleAsync(x => x.Id == itemId);

        Assert.Equal(InventoryAuditResult.Verified, item.Result);
        Assert.Equal(fixture.UserId, item.CheckedByUserId);
        Assert.NotNull(item.CheckedAt);
        Assert.Equal("Seal and label checked", item.Notes);

        var entry = await database.AuditLogEntries
            .SingleAsync(x => x.Action == "InventoryAudit.ItemChecked");

        Assert.Equal(itemId, entry.EntityId);
        Assert.Contains("TEST-AUDIT", entry.Details);
    }

    [Fact]
    public async Task RecordResult_RejectsStaleDisplayedState()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "");

        Guid itemId = await fixture.GetChecklistItemIdAsync(auditId);

        await fixture.Service.RecordResultAsync(
            fixture.UserId,
            auditId,
            itemId,
            InventoryAuditResult.Verified,
            "",
            InventoryAuditResult.Pending,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordResultAsync(
                fixture.UserId,
                auditId,
                itemId,
                InventoryAuditResult.Missing,
                "Stale screen test",
                InventoryAuditResult.Pending,
                null));

        await using var database = fixture.CreateContext();

        var item = await database.InventoryAuditItems
            .SingleAsync(x => x.Id == itemId);

        Assert.Equal(InventoryAuditResult.Verified, item.Result);
        Assert.Equal(2, await database.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task Complete_RejectsPendingItems()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CompleteAsync(
                fixture.UserId,
                auditId,
                ""));

        await using var database = fixture.CreateContext();

        var audit = await database.InventoryAudits
            .SingleAsync(x => x.Id == auditId);

        Assert.Equal(InventoryAuditStatus.InProgress, audit.Status);
        Assert.Null(audit.CompletedAt);
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Fact]
    public async Task Complete_LocksResultsAndPreventsRepeatCompletion()
    {
        using var fixture = new AuditFixture();

        Guid auditId = await fixture.Service.StartAsync(
            fixture.UserId,
            fixture.RoomId,
            false,
            "Original notes");

        Guid itemId = await fixture.GetChecklistItemIdAsync(auditId);

        await fixture.Service.RecordResultAsync(
            fixture.UserId,
            auditId,
            itemId,
            InventoryAuditResult.Verified,
            "",
            InventoryAuditResult.Pending,
            null);

        DateTimeOffset? checkedAt;

        await using (var database = fixture.CreateContext())
        {
            checkedAt = (await database.InventoryAuditItems
                .SingleAsync(x => x.Id == itemId)).CheckedAt;
        }

        await fixture.Service.CompleteAsync(
            fixture.UserId,
            auditId,
            "All items accounted for");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordResultAsync(
                fixture.UserId,
                auditId,
                itemId,
                InventoryAuditResult.Missing,
                "Attempted change",
                InventoryAuditResult.Verified,
                checkedAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.CompleteAsync(
                fixture.UserId,
                auditId,
                "Attempted second completion"));

        await using var verification = fixture.CreateContext();

        var audit = await verification.InventoryAudits
            .SingleAsync(x => x.Id == auditId);

        var item = await verification.InventoryAuditItems
            .SingleAsync(x => x.Id == itemId);

        Assert.Equal(InventoryAuditStatus.Completed, audit.Status);
        Assert.Equal(fixture.UserId, audit.CompletedByUserId);
        Assert.NotNull(audit.CompletedAt);
        Assert.Equal("Original notes", audit.Notes);
        Assert.Equal(InventoryAuditResult.Verified, item.Result);

        Assert.Equal(3, await verification.AuditLogEntries.CountAsync());
    }

    private sealed class AuditFixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ManifestDbContext> _options;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid RoomId { get; } = Guid.NewGuid();
        public Guid ShelfId { get; } = Guid.NewGuid();

        public Guid RoomItemId { get; } = Guid.NewGuid();
        public Guid ShelfItemId { get; } = Guid.NewGuid();
        public Guid CheckedOutItemId { get; } = Guid.NewGuid();

        public InventoryAuditService Service { get; }

        public AuditFixture()
        {
            _connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");

            _connection.Open();

            _options = new DbContextOptionsBuilder<ManifestDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var database = CreateContext();

            database.Database.EnsureCreated();

            database.Users.Add(new AppUser
            {
                Id = UserId,
                Username = "audit.tester",
                DisplayName = "Audit Tester",
                Role = UserRole.EvidenceCustodian,
                IsActive = true
            });

            database.StorageLocations.AddRange(
                new StorageLocation
                {
                    Id = RoomId,
                    Name = "Evidence Room",
                    IsActive = true
                },
                new StorageLocation
                {
                    Id = ShelfId,
                    Name = "Shelf 1",
                    ParentLocationId = RoomId,
                    IsActive = true
                });

            database.EvidenceItems.AddRange(
                new EvidenceItem
                {
                    Id = RoomItemId,
                    CaseNumber = "TEST-AUDIT",
                    ItemNumber = "1",
                    Description = "Room test item",
                    CollectedBy = "Audit Tester",
                    CurrentStorageLocationId = RoomId,
                    Status = EvidenceStatus.InCustody
                },
                new EvidenceItem
                {
                    Id = ShelfItemId,
                    CaseNumber = "TEST-AUDIT",
                    ItemNumber = "2",
                    Description = "Shelf test item",
                    CollectedBy = "Audit Tester",
                    CurrentStorageLocationId = ShelfId,
                    Status = EvidenceStatus.InCustody
                },
                new EvidenceItem
                {
                    Id = CheckedOutItemId,
                    CaseNumber = "TEST-AUDIT",
                    ItemNumber = "3",
                    Description = "Checked-out test item",
                    CollectedBy = "Audit Tester",
                    CurrentStorageLocationId = null,
                    Status = EvidenceStatus.CheckedOut
                });

            database.SaveChanges();

            Service = new InventoryAuditService(CreateContext);
        }

        public ManifestDbContext CreateContext()
        {
            return new ManifestDbContext(_options);
        }

        public async Task<Guid> GetChecklistItemIdAsync(Guid auditId)
        {
            await using var database = CreateContext();

            return (await database.InventoryAuditItems
                .SingleAsync(x => x.InventoryAuditId == auditId)).Id;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}