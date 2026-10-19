using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using ManifestLE.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ManifestLE.Tests;

public sealed class InventoryAuditFollowUpServiceTests
{
    [Fact]
    public async Task InvestigationNote_SavesFollowUpAndAuditEntry()
    {
        using var fixture = new FollowUpFixture();

        Guid id = await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.InvestigationNote,
            "Checked adjacent shelves; investigation continues.");

        await using var database = fixture.CreateContext();

        var followUp = await database.InventoryAuditFollowUps
            .SingleAsync(x => x.Id == id);

        Assert.Equal(fixture.AuditItemId, followUp.InventoryAuditItemId);
        Assert.Equal(fixture.UserId, followUp.RecordedByUserId);
        Assert.Equal(
            InventoryAuditFollowUpType.InvestigationNote,
            followUp.Type);

        var entry = await database.AuditLogEntries.SingleAsync();

        Assert.Equal("InventoryAudit.FollowUpRecorded", entry.Action);
        Assert.Equal(nameof(InventoryAuditItem), entry.EntityType);
        Assert.Equal(fixture.AuditItemId, entry.EntityId);
        Assert.Contains(id.ToString(), entry.Details);
        Assert.Contains("TEST-FOLLOWUP", entry.Details);
    }

    [Fact]
    public async Task Resolution_PreservesOriginalFindingAndPropertyState()
    {
        using var fixture = new FollowUpFixture();

        await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.Resolved,
            "Located in its assigned cabinet; label was obscured.");

        await using var database = fixture.CreateContext();

        var item = await database.InventoryAuditItems
            .SingleAsync(x => x.Id == fixture.AuditItemId);

        var audit = await database.InventoryAudits
            .SingleAsync(x => x.Id == fixture.AuditId);

        var evidence = await database.EvidenceItems
            .SingleAsync(x => x.Id == fixture.EvidenceId);

        Assert.Equal(InventoryAuditResult.Missing, item.Result);
        Assert.Equal("Not located during inspection.", item.Notes);
        Assert.Equal(fixture.InspectedAt, item.CheckedAt);
        Assert.Equal(InventoryAuditStatus.Completed, audit.Status);
        Assert.Equal(fixture.CompletedAt, audit.CompletedAt);

        Assert.Equal(EvidenceStatus.InCustody, evidence.Status);
        Assert.Equal(fixture.LocationId, evidence.CurrentStorageLocationId);
        Assert.Equal(0, await database.CustodyEvents.CountAsync());

        var entry = await database.AuditLogEntries.SingleAsync();
        Assert.Equal("InventoryAudit.FindingResolved", entry.Action);
    }

    [Fact]
    public async Task MultipleInvestigationNotes_ArePreservedSeparately()
    {
        using var fixture = new FollowUpFixture();

        await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.InvestigationNote,
            "Checked adjacent shelves.");

        await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.InvestigationNote,
            "Reviewed movement history.");

        await using var database = fixture.CreateContext();

        Assert.Equal(2, await database.InventoryAuditFollowUps.CountAsync());
        Assert.Equal(2, await database.AuditLogEntries.CountAsync());
    }

    [Theory]
    [InlineData(InventoryAuditFollowUpType.InvestigationNote)]
    [InlineData(InventoryAuditFollowUpType.Resolved)]
    public async Task ResolvedFinding_RejectsFurtherFollowUps(
        InventoryAuditFollowUpType nextType)
    {
        using var fixture = new FollowUpFixture();

        await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.Resolved,
            "Located and verified.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                nextType,
                "Attempted additional entry."));

        await using var database = fixture.CreateContext();

        Assert.Equal(1, await database.InventoryAuditFollowUps.CountAsync());
        Assert.Equal(1, await database.AuditLogEntries.CountAsync());
    }

    [Theory]
    [InlineData(InventoryAuditFollowUpType.InvestigationNote)]
    [InlineData(InventoryAuditFollowUpType.Resolved)]
    public async Task BlankNotes_AreRejected(
        InventoryAuditFollowUpType type)
    {
        using var fixture = new FollowUpFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                type,
                "   "));

        await fixture.AssertNoWritesAsync();
    }

    [Fact]
    public async Task OversizedNotes_AreRejected()
    {
        using var fixture = new FollowUpFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                InventoryAuditFollowUpType.InvestigationNote,
                new string('x', 4001)));

        await fixture.AssertNoWritesAsync();
    }

    [Theory]
    [InlineData(UserRole.Officer, true)]
    [InlineData(UserRole.Administrator, false)]
    public async Task UnauthorizedAccount_IsRejected(
        UserRole role,
        bool active)
    {
        using var fixture = new FollowUpFixture();

        await using (var database = fixture.CreateContext())
        {
            var user = await database.Users
                .SingleAsync(x => x.Id == fixture.UserId);

            user.Role = role;
            user.IsActive = active;

            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                InventoryAuditFollowUpType.InvestigationNote,
                "Investigation note."));

        await fixture.AssertNoWritesAsync();
    }

    [Fact]
    public async Task InProgressAudit_IsRejected()
    {
        using var fixture = new FollowUpFixture();

        await using (var database = fixture.CreateContext())
        {
            var audit = await database.InventoryAudits
                .SingleAsync(x => x.Id == fixture.AuditId);

            audit.Status = InventoryAuditStatus.InProgress;
            audit.CompletedAt = null;
            audit.CompletedByUserId = null;

            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                InventoryAuditFollowUpType.InvestigationNote,
                "Investigation note."));

        await fixture.AssertNoWritesAsync();
    }

    [Fact]
    public async Task VerifiedItem_IsRejected()
    {
        using var fixture = new FollowUpFixture();

        await using (var database = fixture.CreateContext())
        {
            var item = await database.InventoryAuditItems
                .SingleAsync(x => x.Id == fixture.AuditItemId);

            item.Result = InventoryAuditResult.Verified;

            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                fixture.AuditId,
                fixture.AuditItemId,
                InventoryAuditFollowUpType.InvestigationNote,
                "Investigation note."));

        await fixture.AssertNoWritesAsync();
    }

    [Fact]
    public async Task ItemFromDifferentAudit_IsRejected()
    {
        using var fixture = new FollowUpFixture();

        Guid otherAuditId = Guid.NewGuid();

        await using (var database = fixture.CreateContext())
        {
            database.InventoryAudits.Add(new InventoryAudit
            {
                Id = otherAuditId,
                StorageLocationId = fixture.LocationId,
                StoragePathSnapshot = "Evidence Room",
                StartedByUserId = fixture.UserId,
                Status = InventoryAuditStatus.Completed,
                CompletedByUserId = fixture.UserId,
                CompletedAt = fixture.CompletedAt
            });

            await database.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RecordAsync(
                fixture.UserId,
                otherAuditId,
                fixture.AuditItemId,
                InventoryAuditFollowUpType.InvestigationNote,
                "Investigation note."));

        await fixture.AssertNoWritesAsync();
    }

    [Fact]
    public async Task DiscrepancyFinding_CanBeResolved()
    {
        using var fixture = new FollowUpFixture();

        await using (var database = fixture.CreateContext())
        {
            var item = await database.InventoryAuditItems
                .SingleAsync(x => x.Id == fixture.AuditItemId);

            item.Result = InventoryAuditResult.Discrepancy;
            item.Notes = "Packaging label requires review.";

            await database.SaveChangesAsync();
        }

        await fixture.Service.RecordAsync(
            fixture.UserId,
            fixture.AuditId,
            fixture.AuditItemId,
            InventoryAuditFollowUpType.Resolved,
            "Reviewed original documentation and confirmed identification.");

        await using var verification = fixture.CreateContext();

        Assert.Equal(1,
            await verification.InventoryAuditFollowUps.CountAsync());

        Assert.Equal(
            InventoryAuditResult.Discrepancy,
            (await verification.InventoryAuditItems
                .SingleAsync(x => x.Id == fixture.AuditItemId)).Result);
    }

    private sealed class FollowUpFixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ManifestDbContext> _options;

        public Guid UserId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid EvidenceId { get; } = Guid.NewGuid();
        public Guid AuditId { get; } = Guid.NewGuid();
        public Guid AuditItemId { get; } = Guid.NewGuid();

        public DateTimeOffset InspectedAt { get; } =
            DateTimeOffset.UtcNow.AddMinutes(-10);

        public DateTimeOffset CompletedAt { get; } =
            DateTimeOffset.UtcNow.AddMinutes(-5);

        public InventoryAuditFollowUpService Service { get; }

        public FollowUpFixture()
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
                Username = "followup.tester",
                DisplayName = "Follow-up Tester",
                Role = UserRole.EvidenceCustodian,
                IsActive = true
            });

            database.StorageLocations.Add(new StorageLocation
            {
                Id = LocationId,
                Name = "Evidence Room",
                IsActive = true
            });

            database.EvidenceItems.Add(new EvidenceItem
            {
                Id = EvidenceId,
                CaseNumber = "TEST-FOLLOWUP",
                ItemNumber = "1",
                Description = "Test property",
                CollectedBy = "Follow-up Tester",
                Status = EvidenceStatus.InCustody,
                CurrentStorageLocationId = LocationId
            });

            database.InventoryAudits.Add(new InventoryAudit
            {
                Id = AuditId,
                StorageLocationId = LocationId,
                StoragePathSnapshot = "Evidence Room",
                StartedByUserId = UserId,
                StartedAt = InspectedAt.AddMinutes(-5),
                Status = InventoryAuditStatus.Completed,
                CompletedByUserId = UserId,
                CompletedAt = CompletedAt
            });

            database.InventoryAuditItems.Add(new InventoryAuditItem
            {
                Id = AuditItemId,
                InventoryAuditId = AuditId,
                EvidenceItemId = EvidenceId,
                ReportNumberSnapshot = "TEST-FOLLOWUP",
                ItemNumberSnapshot = "1",
                DescriptionSnapshot = "Test property",
                ExpectedStorageLocationId = LocationId,
                ExpectedStoragePathSnapshot = "Evidence Room",
                ExpectedStatus = EvidenceStatus.InCustody,
                Result = InventoryAuditResult.Missing,
                CheckedByUserId = UserId,
                CheckedAt = InspectedAt,
                Notes = "Not located during inspection."
            });

            database.SaveChanges();

            Service = new InventoryAuditFollowUpService(CreateContext);
        }

        public ManifestDbContext CreateContext()
        {
            return new ManifestDbContext(_options);
        }

        public async Task AssertNoWritesAsync()
        {
            await using var database = CreateContext();

            Assert.Equal(0,
                await database.InventoryAuditFollowUps.CountAsync());

            Assert.Equal(0,
                await database.AuditLogEntries.CountAsync());
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}