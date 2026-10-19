using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class CustodyService
{
    private readonly Func<ManifestDbContext> _createContext;

    public CustodyService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public CustodyService(Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task RecordMovementAsync(
        Guid recordedByUserId,
        PropertyRegisterKind register,
        Guid itemId,
        CustodyEventType action,
        EvidenceStatus expectedStatus,
        Guid? expectedLocationId,
        Guid? destinationLocationId,
        string fromCustodian,
        string toCustodian,
        string reason,
        string notes)
    {
        if (action != CustodyEventType.Transfer &&
            action != CustodyEventType.CheckOut &&
            action != CustodyEventType.CheckIn)
        {
            throw new InvalidOperationException(
                "Choose a storage transfer, checkout, or check-in.");
        }

        fromCustodian = (fromCustodian ?? string.Empty).Trim();
        toCustodian = (toCustodian ?? string.Empty).Trim();
        reason = (reason ?? string.Empty).Trim();
        notes = (notes ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(fromCustodian) ||
            string.IsNullOrWhiteSpace(toCustodian) ||
            string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException(
                "Enter both custodians and a reason.");
        }

        if (fromCustodian.Length > 200 ||
            toCustodian.Length > 200 ||
            reason.Length > 1000 ||
            notes.Length > 4000)
        {
            throw new InvalidOperationException(
                "One or more custody fields exceed the allowed length.");
        }

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync();

        var user = await database.Users
            .SingleOrDefaultAsync(x => x.Id == recordedByUserId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Administrator &&
             user.Role != UserRole.EvidenceCustodian))
        {
            throw new InvalidOperationException(
                "An active administrator or evidence custodian " +
                "must record this action.");
        }

        var item = await LoadItemAsync(database, register, itemId);

        if (item.Status != expectedStatus ||
            item.LocationId != expectedLocationId)
        {
            throw new InvalidOperationException(
                "This item's status or location changed. " +
                "Reload the item before continuing.");
        }

        EvidenceStatus newStatus;
        Guid? newLocationId;

        switch (action)
        {
            case CustodyEventType.Transfer:
                if (item.Status != EvidenceStatus.InCustody ||
                    item.LocationId is null)
                {
                    throw new InvalidOperationException(
                        "Only an in-custody item with a storage location " +
                        "can be transferred.");
                }

                if (destinationLocationId is null)
                {
                    throw new InvalidOperationException(
                        "Choose a destination storage location.");
                }

                if (destinationLocationId == item.LocationId)
                {
                    throw new InvalidOperationException(
                        "Choose a different storage location.");
                }

                newStatus = EvidenceStatus.InCustody;
                newLocationId = destinationLocationId;
                break;

            case CustodyEventType.CheckOut:
                if (item.Status != EvidenceStatus.InCustody ||
                    item.LocationId is null)
                {
                    throw new InvalidOperationException(
                        "Only an in-custody item with a storage location " +
                        "can be checked out.");
                }

                if (destinationLocationId is not null)
                {
                    throw new InvalidOperationException(
                        "A checked-out item cannot have a current " +
                        "storage location.");
                }

                newStatus = EvidenceStatus.CheckedOut;
                newLocationId = null;
                break;

            case CustodyEventType.CheckIn:
                if (item.Status != EvidenceStatus.CheckedOut)
                {
                    throw new InvalidOperationException(
                        "Only a checked-out item can be checked in.");
                }

                if (item.LocationId is not null)
                {
                    throw new InvalidOperationException(
                        "This checked-out item has an unexpected storage " +
                        "location. Review the record before continuing.");
                }

                if (destinationLocationId is null)
                {
                    throw new InvalidOperationException(
                        "Choose the storage location receiving the item.");
                }

                newStatus = EvidenceStatus.InCustody;
                newLocationId = destinationLocationId;
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported custody action.");
        }

        if (newLocationId is Guid destinationId)
        {
            bool destinationAvailable =
                await database.StorageLocations.AnyAsync(
                    x => x.Id == destinationId && x.IsActive);

            if (!destinationAvailable)
            {
                throw new InvalidOperationException(
                    "The destination storage location is unavailable.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        var custody = new CustodyEvent
        {
            EventType = action,
            OccurredAt = now,
            RecordedAt = now,
            RecordedByUserId = recordedByUserId,
            FromCustodian = fromCustodian,
            ToCustodian = toCustodian,
            FromStorageLocationId = item.LocationId,
            ToStorageLocationId = newLocationId,
            Reason = reason,
            Notes = notes
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

            default:
                throw new InvalidOperationException("Unknown register.");
        }

        item.ApplyState(newStatus, newLocationId);

        database.CustodyEvents.Add(custody);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = recordedByUserId,
            Action = $"Custody.{action}",
            EntityType = item.EntityType,
            EntityId = itemId,
            RecordedAt = now,
            Details =
                $"Status: {item.Status} -> {newStatus}. " +
                $"Storage: {item.LocationId?.ToString() ?? "None"} -> " +
                $"{newLocationId?.ToString() ?? "None"}. " +
                $"Custody event: {custody.Id}."
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task<TrackedItem> LoadItemAsync(
        ManifestDbContext database,
        PropertyRegisterKind register,
        Guid itemId)
    {
        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                {
                    var item = await database.EvidenceItems
                        .SingleOrDefaultAsync(x => x.Id == itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The evidence item could not be found.");
                    }

                    return new TrackedItem
                    {
                        Status = item.Status,
                        LocationId = item.CurrentStorageLocationId,
                        EntityType = nameof(EvidenceItem),
                        ApplyState = (status, location) =>
                        {
                            item.Status = status;
                            item.CurrentStorageLocationId = location;
                        }
                    };
                }

            case PropertyRegisterKind.FoundProperty:
                {
                    var item = await database.FoundPropertyItems
                        .SingleOrDefaultAsync(x => x.Id == itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The found-property item could not be found.");
                    }

                    return new TrackedItem
                    {
                        Status = item.Status,
                        LocationId = item.CurrentStorageLocationId,
                        EntityType = nameof(FoundPropertyItem),
                        ApplyState = (status, location) =>
                        {
                            item.Status = status;
                            item.CurrentStorageLocationId = location;
                        }
                    };
                }

            case PropertyRegisterKind.Safekeeping:
                {
                    var item = await database.SafekeepingItems
                        .SingleOrDefaultAsync(x => x.Id == itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The safekeeping item could not be found.");
                    }

                    return new TrackedItem
                    {
                        Status = item.Status,
                        LocationId = item.CurrentStorageLocationId,
                        EntityType = nameof(SafekeepingItem),
                        ApplyState = (status, location) =>
                        {
                            item.Status = status;
                            item.CurrentStorageLocationId = location;
                        }
                    };
                }

            default:
                throw new InvalidOperationException("Unknown register.");
        }
    }

    private sealed class TrackedItem
    {
        public EvidenceStatus Status { get; init; }

        public Guid? LocationId { get; init; }

        public string EntityType { get; init; } = string.Empty;

        public Action<EvidenceStatus, Guid?> ApplyState { get; init; }
            = (_, _) => { };
    }
}