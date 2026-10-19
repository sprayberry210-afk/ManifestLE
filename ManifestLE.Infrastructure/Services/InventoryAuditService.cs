using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public sealed class InventoryAuditService
{
    private readonly Func<ManifestDbContext> _createContext;

    public InventoryAuditService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public InventoryAuditService(
        Func<ManifestDbContext> createContext)
    {
        _createContext = createContext
            ?? throw new ArgumentNullException(nameof(createContext));
    }

    public async Task<Guid> StartAsync(
        Guid userId,
        Guid storageLocationId,
        bool includeChildLocations,
        string notes)
    {
        notes = ValidateNotes(notes);

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        await RequireAuthorizedUserAsync(database, userId);

        var locations = await database.StorageLocations
            .AsNoTracking()
            .ToListAsync();

        var lookup = locations.ToDictionary(x => x.Id);

        if (!lookup.TryGetValue(storageLocationId, out var root) ||
            !root.IsActive)
        {
            throw new InvalidOperationException(
                "Select an active storage location.");
        }

        var scope = BuildScope(
            storageLocationId,
            includeChildLocations,
            locations);

        // Prevent simultaneous audits of overlapping storage areas.
        var openAudits = await database.InventoryAudits
            .AsNoTracking()
            .Where(x => x.Status == InventoryAuditStatus.InProgress)
            .ToListAsync();

        foreach (var existing in openAudits)
        {
            var existingScope = BuildScope(
                existing.StorageLocationId,
                existing.IncludeChildLocations,
                locations);

            if (scope.Overlaps(existingScope))
            {
                throw new InvalidOperationException(
                    "An unfinished audit already covers this location " +
                    "or an overlapping storage area. Resume that audit first.");
            }
        }

        var audit = new InventoryAudit
        {
            StorageLocationId = storageLocationId,
            StoragePathSnapshot = BuildPath(storageLocationId, lookup),
            IncludeChildLocations = includeChildLocations,
            StartedByUserId = userId,
            StartedAt = DateTimeOffset.UtcNow,
            Status = InventoryAuditStatus.InProgress,
            Notes = notes
        };

        var scopeIds = scope.ToList();

        var evidence = await database.EvidenceItems
            .AsNoTracking()
            .Where(x =>
                x.Status == EvidenceStatus.InCustody &&
                x.CurrentStorageLocationId.HasValue &&
                scopeIds.Contains(x.CurrentStorageLocationId.Value))
            .ToListAsync();

        foreach (var item in evidence)
        {
            audit.Items.Add(CreateChecklistItem(
                audit.Id,
                item.Id,
                null,
                null,
                item.CaseNumber,
                item.ItemNumber,
                item.Description,
                item.CurrentStorageLocationId!.Value,
                lookup));
        }

        var foundProperty = await database.FoundPropertyItems
            .AsNoTracking()
            .Where(x =>
                x.Status == EvidenceStatus.InCustody &&
                x.CurrentStorageLocationId.HasValue &&
                scopeIds.Contains(x.CurrentStorageLocationId.Value))
            .ToListAsync();

        foreach (var item in foundProperty)
        {
            audit.Items.Add(CreateChecklistItem(
                audit.Id,
                null,
                item.Id,
                null,
                item.ReportNumber,
                item.ItemNumber,
                item.Description,
                item.CurrentStorageLocationId!.Value,
                lookup));
        }

        var safekeeping = await database.SafekeepingItems
            .AsNoTracking()
            .Where(x =>
                x.Status == EvidenceStatus.InCustody &&
                x.CurrentStorageLocationId.HasValue &&
                scopeIds.Contains(x.CurrentStorageLocationId.Value))
            .ToListAsync();

        foreach (var item in safekeeping)
        {
            audit.Items.Add(CreateChecklistItem(
                audit.Id,
                null,
                null,
                item.Id,
                item.ReportNumber,
                item.ItemNumber,
                item.Description,
                item.CurrentStorageLocationId!.Value,
                lookup));
        }

        if (audit.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "No in-custody property is assigned to this storage area.");
        }

        database.InventoryAudits.Add(audit);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = "InventoryAudit.Started",
            EntityType = nameof(InventoryAudit),
            EntityId = audit.Id,
            Details =
                $"Started inventory audit at {audit.StoragePathSnapshot}. " +
                $"Include child locations: {includeChildLocations}. " +
                $"Expected items: {audit.Items.Count}. " +
                $"Notes: {notes}"
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        return audit.Id;
    }

    public async Task RecordResultAsync(
        Guid userId,
        Guid auditId,
        Guid auditItemId,
        InventoryAuditResult result,
        string notes,
        InventoryAuditResult expectedResult,
        DateTimeOffset? expectedCheckedAt)
    {
        if (!Enum.IsDefined(typeof(InventoryAuditResult), result))
        {
            throw new InvalidOperationException(
                "Select a valid audit result.");
        }

        notes = ValidateNotes(notes);

        if ((result == InventoryAuditResult.Missing ||
             result == InventoryAuditResult.Discrepancy) &&
            string.IsNullOrWhiteSpace(notes))
        {
            throw new InvalidOperationException(
                "Enter notes explaining the missing item or discrepancy.");
        }

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        await RequireAuthorizedUserAsync(database, userId);

        var audit = await database.InventoryAudits
            .SingleOrDefaultAsync(x => x.Id == auditId);

        RequireOpenAudit(audit);

        var item = await database.InventoryAuditItems
            .SingleOrDefaultAsync(x =>
                x.Id == auditItemId &&
                x.InventoryAuditId == auditId);

        if (item is null)
        {
            throw new InvalidOperationException(
                "This item does not belong to the selected audit.");
        }

        if (item.Result != expectedResult ||
            item.CheckedAt != expectedCheckedAt)
        {
            throw new InvalidOperationException(
                "This item was changed after you opened it. " +
                "Refresh the audit before saving.");
        }

        var previousResult = item.Result;
        string previousNotes = item.Notes;

        // Avoid creating duplicate entries for an unchanged save.
        if (previousResult == result && previousNotes == notes)
        {
            await transaction.CommitAsync();
            return;
        }

        item.Result = result;
        item.Notes = notes;

        if (result == InventoryAuditResult.Pending)
        {
            item.CheckedByUserId = null;
            item.CheckedAt = null;
        }
        else
        {
            item.CheckedByUserId = userId;
            item.CheckedAt = DateTimeOffset.UtcNow;
        }

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = "InventoryAudit.ItemChecked",
            EntityType = nameof(InventoryAuditItem),
            EntityId = item.Id,
            Details =
                $"Audit: {auditId}. " +
                $"Case / report: {item.ReportNumberSnapshot}. " +
                $"Item: {item.ItemNumberSnapshot}. " +
                $"Expected storage: {item.ExpectedStoragePathSnapshot}. " +
                $"Result: {previousResult} -> {result}. " +
                $"Previous notes: {previousNotes}. " +
                $"Notes: {notes}"
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task CompleteAsync(
        Guid userId,
        Guid auditId,
        string completionNotes)
    {
        completionNotes = ValidateNotes(completionNotes);

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        await RequireAuthorizedUserAsync(database, userId);

        var audit = await database.InventoryAudits
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == auditId);

        RequireOpenAudit(audit);

        if (audit!.Items.Count == 0)
        {
            throw new InvalidOperationException(
                "An audit must contain at least one item.");
        }

        int pending = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Pending);

        if (pending > 0)
        {
            throw new InvalidOperationException(
                $"Check all items before completing this audit. " +
                $"{pending} item(s) remain pending.");
        }

        if (audit.Items.Any(x =>
                !Enum.IsDefined(typeof(InventoryAuditResult), x.Result) ||
                x.CheckedByUserId is null ||
                x.CheckedAt is null))
        {
            throw new InvalidOperationException(
                "One or more item checks are incomplete.");
        }

        if (audit.Items.Any(x =>
                (x.Result == InventoryAuditResult.Missing ||
                 x.Result == InventoryAuditResult.Discrepancy) &&
                string.IsNullOrWhiteSpace(x.Notes)))
        {
            throw new InvalidOperationException(
                "Missing items and discrepancies require explanatory notes.");
        }

        int verified = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Verified);

        int missing = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Missing);

        int discrepancies = audit.Items.Count(x =>
            x.Result == InventoryAuditResult.Discrepancy);

        audit.Status = InventoryAuditStatus.Completed;
        audit.CompletedByUserId = userId;
        audit.CompletedAt = DateTimeOffset.UtcNow;

        // Preserve the original audit notes.
        // Completion notes are recorded in the completion log entry.
        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = userId,
            Action = "InventoryAudit.Completed",
            EntityType = nameof(InventoryAudit),
            EntityId = audit.Id,
            Details =
                $"Completed inventory audit at {audit.StoragePathSnapshot}. " +
                $"Total: {audit.Items.Count}. " +
                $"Verified: {verified}. " +
                $"Missing: {missing}. " +
                $"Discrepancies: {discrepancies}. " +
                $"Completion notes: {completionNotes}"
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task RequireAuthorizedUserAsync(
        ManifestDbContext database,
        Guid userId)
    {
        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Administrator &&
             user.Role != UserRole.EvidenceCustodian))
        {
            throw new InvalidOperationException(
                "Inventory audits require an active administrator " +
                "or evidence custodian account.");
        }
    }

    private static void RequireOpenAudit(InventoryAudit? audit)
    {
        if (audit is null)
        {
            throw new InvalidOperationException(
                "The inventory audit could not be found.");
        }

        if (audit.Status != InventoryAuditStatus.InProgress)
        {
            throw new InvalidOperationException(
                "This audit is completed and cannot be changed.");
        }
    }

    private static string ValidateNotes(string? notes)
    {
        string value = (notes ?? "").Trim();

        if (value.Length > 4000)
        {
            throw new InvalidOperationException(
                "Notes cannot exceed 4,000 characters.");
        }

        return value;
    }

    private static InventoryAuditItem CreateChecklistItem(
        Guid auditId,
        Guid? evidenceItemId,
        Guid? foundPropertyItemId,
        Guid? safekeepingItemId,
        string reportNumber,
        string itemNumber,
        string description,
        Guid locationId,
        IReadOnlyDictionary<Guid, StorageLocation> locations)
    {
        return new InventoryAuditItem
        {
            InventoryAuditId = auditId,
            EvidenceItemId = evidenceItemId,
            FoundPropertyItemId = foundPropertyItemId,
            SafekeepingItemId = safekeepingItemId,
            ReportNumberSnapshot = reportNumber,
            ItemNumberSnapshot = itemNumber,
            DescriptionSnapshot = description,
            ExpectedStorageLocationId = locationId,
            ExpectedStoragePathSnapshot = BuildPath(
                locationId,
                locations),
            ExpectedStatus = EvidenceStatus.InCustody,
            Result = InventoryAuditResult.Pending
        };
    }

    private static HashSet<Guid> BuildScope(
        Guid rootId,
        bool includeChildren,
        IReadOnlyCollection<StorageLocation> locations)
    {
        var scope = new HashSet<Guid> { rootId };

        if (!includeChildren)
        {
            return scope;
        }

        var children = locations
            .Where(x => x.ParentLocationId.HasValue)
            .ToLookup(x => x.ParentLocationId!.Value);

        var pending = new Queue<Guid>();
        pending.Enqueue(rootId);

        while (pending.Count > 0)
        {
            Guid parentId = pending.Dequeue();

            foreach (var child in children[parentId])
            {
                // Include inactive descendants so their stored
                // property is not silently omitted.
                if (scope.Add(child.Id))
                {
                    pending.Enqueue(child.Id);
                }
            }
        }

        return scope;
    }

    private static string BuildPath(
        Guid locationId,
        IReadOnlyDictionary<Guid, StorageLocation> locations)
    {
        var parts = new List<string>();
        var visited = new HashSet<Guid>();

        Guid? currentId = locationId;

        while (currentId is Guid id)
        {
            if (!visited.Add(id))
            {
                throw new InvalidOperationException(
                    "The storage hierarchy contains a circular reference.");
            }

            if (!locations.TryGetValue(id, out var location))
            {
                throw new InvalidOperationException(
                    "A storage location in the hierarchy could not be found.");
            }

            parts.Add(location.IsActive
                ? location.Name
                : $"{location.Name} (inactive)");

            currentId = location.ParentLocationId;
        }

        parts.Reverse();

        return string.Join(" / ", parts);
    }
}