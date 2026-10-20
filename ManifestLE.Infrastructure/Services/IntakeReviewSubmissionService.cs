using System;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public static class IntakeReviewSubmissionService
{
    public static async Task<IntakeReview> AddAsync(
        ManifestDbContext database,
        Guid submittedByUserId,
        PropertyRegisterKind register,
        Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(database);

        if (database.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Intake and its review must be saved " +
                "within the same transaction.");
        }

        Guid? locationId;
        EvidenceStatus status;
        string entityType;
        string reportNumber;
        string itemNumber;
        EntityState state;

        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                {
                    var item = await database.EvidenceItems.FindAsync(itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The evidence item could not be found.");
                    }

                    locationId = item.CurrentStorageLocationId;
                    status = item.Status;
                    entityType = nameof(EvidenceItem);
                    reportNumber = item.CaseNumber;
                    itemNumber = item.ItemNumber;
                    state = database.Entry(item).State;
                    break;
                }

            case PropertyRegisterKind.FoundProperty:
                {
                    var item = await database.FoundPropertyItems.FindAsync(itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The found-property item could not be found.");
                    }

                    locationId = item.CurrentStorageLocationId;
                    status = item.Status;
                    entityType = nameof(FoundPropertyItem);
                    reportNumber = item.ReportNumber;
                    itemNumber = item.ItemNumber;
                    state = database.Entry(item).State;
                    break;
                }

            case PropertyRegisterKind.Safekeeping:
                {
                    var item = await database.SafekeepingItems.FindAsync(itemId);

                    if (item is null)
                    {
                        throw new InvalidOperationException(
                            "The safekeeping item could not be found.");
                    }

                    locationId = item.CurrentStorageLocationId;
                    status = item.Status;
                    entityType = nameof(SafekeepingItem);
                    reportNumber = item.ReportNumber;
                    itemNumber = item.ItemNumber;
                    state = database.Entry(item).State;
                    break;
                }

            default:
                throw new InvalidOperationException(
                    "Choose a valid property register.");
        }

        // This method belongs to new intake only.
        // Existing records require a separate, deliberate process.
        if (state != EntityState.Added)
        {
            throw new InvalidOperationException(
                "A submission review must be created " +
                "during the item's initial intake.");
        }

        if (status != EvidenceStatus.InCustody ||
            locationId is not Guid storageId)
        {
            throw new InvalidOperationException(
                "The submitted item must be in custody " +
                "at a temporary storage locker.");
        }

        var location =
            await StorageAccessService.RequireIntakeLocationAsync(
                database,
                submittedByUserId,
                storageId);

        if (location.LocationType != StorageLocationType.Temporary)
        {
            throw new InvalidOperationException(
                "Only temporary-locker submissions enter " +
                "the intake review queue.");
        }

        foreach (var entry in database.IntakeReviews.Local)
        {
            bool sameItem = register switch
            {
                PropertyRegisterKind.Evidence =>
                    entry.EvidenceItemId == itemId,

                PropertyRegisterKind.FoundProperty =>
                    entry.FoundPropertyItemId == itemId,

                PropertyRegisterKind.Safekeeping =>
                    entry.SafekeepingItemId == itemId,

                _ => false
            };

            if (sameItem)
            {
                throw new InvalidOperationException(
                    "A review has already been added for this item.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        var review = new IntakeReview
        {
            Status = IntakeReviewStatus.PendingReview,
            SubmittedByUserId = submittedByUserId,
            SubmittedAt = now,
            LastSubmittedAt = now,
            Revision = 0
        };

        switch (register)
        {
            case PropertyRegisterKind.Evidence:
                review.EvidenceItemId = itemId;
                break;

            case PropertyRegisterKind.FoundProperty:
                review.FoundPropertyItemId = itemId;
                break;

            case PropertyRegisterKind.Safekeeping:
                review.SafekeepingItemId = itemId;
                break;
        }

        database.IntakeReviews.Add(review);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = submittedByUserId,
            RecordedAt = now,
            Action = "IntakeReview.Submitted",
            EntityType = entityType,
            EntityId = itemId,
            Details =
                $"Report {reportNumber}, item {itemNumber} " +
                $"submitted for intake review. " +
                $"Review ID: {review.Id}. " +
                $"Temporary storage ID: {storageId}."
        });

        // The intake caller saves and commits the item,
        // custody event, review, and audit entries together.
        return review;
    }
}