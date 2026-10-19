using System;
using System.Data;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public sealed class InventoryAuditFollowUpService
{
    private readonly Func<ManifestDbContext> _createContext;

    public InventoryAuditFollowUpService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public InventoryAuditFollowUpService(
        Func<ManifestDbContext> createContext)
    {
        _createContext = createContext
            ?? throw new ArgumentNullException(nameof(createContext));
    }

    public async Task<Guid> RecordAsync(
        Guid userId,
        Guid auditId,
        Guid auditItemId,
        InventoryAuditFollowUpType type,
        string notes)
    {
        if (!Enum.IsDefined(
                typeof(InventoryAuditFollowUpType),
                type))
        {
            throw new InvalidOperationException(
                "Select a valid follow-up type.");
        }

        string validatedNotes = (notes ?? "").Trim();

        if (validatedNotes.Length == 0)
        {
            throw new InvalidOperationException(
                type == InventoryAuditFollowUpType.Resolved
                    ? "Explain how the finding was resolved."
                    : "Enter an investigation note.");
        }

        if (validatedNotes.Length > 4000)
        {
            throw new InvalidOperationException(
                "Follow-up notes cannot exceed 4,000 characters.");
        }

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Administrator &&
             user.Role != UserRole.EvidenceCustodian))
        {
            throw new InvalidOperationException(
                "Audit follow-ups require an active administrator " +
                "or evidence custodian account.");
        }

        var audit = await database.InventoryAudits
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == auditId);

        if (audit is null)
        {
            throw new InvalidOperationException(
                "The inventory audit could not be found.");
        }

        if (audit.Status != InventoryAuditStatus.Completed)
        {
            throw new InvalidOperationException(
                "Complete the inventory audit before recording follow-ups. " +
                "Use inspection notes while the audit is in progress.");
        }

        var item = await database.InventoryAuditItems
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == auditItemId &&
                x.InventoryAuditId == auditId);

        if (item is null)
        {
            throw new InvalidOperationException(
                "This item does not belong to the selected audit.");
        }

        if (item.Result != InventoryAuditResult.Missing &&
            item.Result != InventoryAuditResult.Discrepancy)
        {
            throw new InvalidOperationException(
                "Follow-ups are available only for missing items " +
                "or discrepancy findings.");
        }

        bool alreadyResolved = await database.InventoryAuditFollowUps
            .AsNoTracking()
            .AnyAsync(x =>
                x.InventoryAuditItemId == auditItemId &&
                x.Type == InventoryAuditFollowUpType.Resolved);

        if (alreadyResolved)
        {
            throw new InvalidOperationException(
                "This finding has already been resolved. " +
                "Its saved follow-up history remains available for review.");
        }

        var now = DateTimeOffset.UtcNow;

        var followUp = new InventoryAuditFollowUp
        {
            InventoryAuditItemId = auditItemId,
            Type = type,
            RecordedByUserId = userId,
            RecordedAt = now,
            Notes = validatedNotes
        };

        database.InventoryAuditFollowUps.Add(followUp);

        // Link the log entry to the audit item so its case,
        // item number, and parent audit remain discoverable
        // through the existing Audit History lookups.
        database.AuditLogEntries.Add(new AuditLogEntry
        {
            RecordedAt = now,
            UserId = userId,
            Action = type == InventoryAuditFollowUpType.Resolved
                ? "InventoryAudit.FindingResolved"
                : "InventoryAudit.FollowUpRecorded",
            EntityType = nameof(InventoryAuditItem),
            EntityId = auditItemId,
            Details =
                $"Inventory audit: {auditId}. " +
                $"Follow-up entry: {followUp.Id}. " +
                $"Case / report: {item.ReportNumberSnapshot}. " +
                $"Item: {item.ItemNumberSnapshot}. " +
                $"Original finding: {item.Result}. " +
                $"Expected storage: {item.ExpectedStoragePathSnapshot}. " +
                $"Follow-up type: {type}. " +
                $"Notes: {validatedNotes}"
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        return followUp.Id;
    }
}