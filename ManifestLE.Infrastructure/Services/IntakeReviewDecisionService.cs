using System;
using System.Data;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class IntakeReviewDecisionService
{
    private readonly Func<ManifestDbContext> _createContext;

    public IntakeReviewDecisionService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public IntakeReviewDecisionService(
        Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task RequestCorrectionAsync(
        Guid reviewerId,
        Guid reviewId,
        int expectedRevision,
        string instructions)
    {
        instructions = (instructions ?? string.Empty).Trim();

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        await RequireReviewerAsync(database, reviewerId);

        if (string.IsNullOrWhiteSpace(instructions) ||
            instructions.Length > 4000)
        {
            throw new InvalidOperationException(
                "Enter correction instructions of 1–4000 characters.");
        }

        var review = await RequireReviewAsync(
            database,
            reviewId,
            expectedRevision);

        var item = await LoadItemAsync(database, review);

        await RequireTemporaryStorageAsync(database, item);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IntakeReviewStatus previousStatus = review.Status;
        string previousInstructions = review.CorrectionInstructions;

        review.Status = IntakeReviewStatus.NeedsCorrection;
        review.CorrectionInstructions = instructions;
        review.LastReviewedByUserId = reviewerId;
        review.LastReviewedAt = now;
        review.Revision = checked(review.Revision + 1);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = reviewerId,
            RecordedAt = now,
            Action = "IntakeReview.CorrectionRequested",
            EntityType = item.EntityType,
            EntityId = item.Id,
            Details =
                $"Report {item.ReportNumber}, item {item.ItemNumber}. " +
                $"Review ID: {review.Id}. " +
                $"Status: {previousStatus} -> NeedsCorrection. " +
                $"Previous instructions: {previousInstructions}\n" +
                $"Correction instructions: {instructions}"
        });

        // No property state or custody event is changed here.
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task AcceptAsync(
        Guid reviewerId,
        Guid reviewId,
        int expectedRevision,
        Guid expectedTemporaryLocationId,
        Guid permanentLocationId,
        string receiptNotes)
    {
        receiptNotes = (receiptNotes ?? string.Empty).Trim();

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var reviewer = await RequireReviewerAsync(
            database,
            reviewerId);

        if (receiptNotes.Length > 4000)
        {
            throw new InvalidOperationException(
                "Use receipt notes of no more than 4000 characters.");
        }

        var review = await RequireReviewAsync(
            database,
            reviewId,
            expectedRevision);

        var item = await LoadItemAsync(database, review);

        await RequireTemporaryStorageAsync(database, item);

        if (item.LocationId != expectedTemporaryLocationId)
        {
            throw new InvalidOperationException(
                "The item's temporary location changed. " +
                "Refresh the queue before accepting it.");
        }

        var destination = await database.StorageLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == permanentLocationId);

        if (destination is null ||
            !destination.IsActive ||
            destination.LocationType != StorageLocationType.Permanent)
        {
            throw new InvalidOperationException(
                "Choose an active permanent storage location.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        IntakeReviewStatus previousStatus = review.Status;

        var custody = new CustodyEvent
        {
            EventType = CustodyEventType.Transfer,
            OccurredAt = now,
            RecordedAt = now,
            RecordedByUserId = reviewerId,
            FromCustodian = "Temporary storage",
            ToCustodian = reviewer.DisplayName,
            FromStorageLocationId = item.LocationId,
            ToStorageLocationId = destination.Id,
            Reason = "Received into permanent storage after intake review.",
            Notes = receiptNotes
        };

        switch (item.Register)
        {
            case PropertyRegisterKind.Evidence:
                custody.EvidenceItemId = item.Id;
                break;

            case PropertyRegisterKind.FoundProperty:
                custody.FoundPropertyItemId = item.Id;
                break;

            case PropertyRegisterKind.Safekeeping:
                custody.SafekeepingItemId = item.Id;
                break;
        }

        item.SetLocation(destination.Id);

        review.Status = IntakeReviewStatus.Accepted;
        review.LastReviewedByUserId = reviewerId;
        review.LastReviewedAt = now;
        review.AcceptedByUserId = reviewerId;
        review.AcceptedAt = now;
        review.Revision = checked(review.Revision + 1);

        database.CustodyEvents.Add(custody);

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = reviewerId,
            RecordedAt = now,
            Action = "Custody.Transfer",
            EntityType = item.EntityType,
            EntityId = item.Id,
            Details =
                $"Report {item.ReportNumber}, item {item.ItemNumber}. " +
                $"Intake review receipt. " +
                $"Storage: {item.LocationId} -> {destination.Id}. " +
                $"Custody event: {custody.Id}. " +
                $"Review ID: {review.Id}."
        });

        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = reviewerId,
            RecordedAt = now,
            Action = "IntakeReview.Accepted",
            EntityType = item.EntityType,
            EntityId = item.Id,
            Details =
                $"Report {item.ReportNumber}, item {item.ItemNumber}. " +
                $"Review ID: {review.Id}. " +
                $"Status: {previousStatus} -> Accepted. " +
                $"Permanent storage ID: {destination.Id}. " +
                $"Custody event: {custody.Id}. " +
                $"Receipt notes: {receiptNotes}"
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task<AppUser> RequireReviewerAsync(
        ManifestDbContext database,
        Guid reviewerId)
    {
        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reviewerId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Administrator &&
             user.Role != UserRole.EvidenceCustodian))
        {
            throw new InvalidOperationException(
                "An active administrator or evidence custodian " +
                "must review submissions.");
        }

        return user;
    }

    private static async Task<IntakeReview> RequireReviewAsync(
        ManifestDbContext database,
        Guid reviewId,
        int expectedRevision)
    {
        var review = await database.IntakeReviews
            .SingleOrDefaultAsync(x => x.Id == reviewId);

        if (review is null)
        {
            throw new InvalidOperationException(
                "The intake review could not be found.");
        }

        if (review.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                "This review changed after you loaded it. " +
                "Refresh the queue before continuing.");
        }

        if (review.Status != IntakeReviewStatus.PendingReview &&
            review.Status != IntakeReviewStatus.Resubmitted)
        {
            throw new InvalidOperationException(
                "Only pending or resubmitted items can be reviewed. " +
                "Items needing correction must be resubmitted first.");
        }

        return review;
    }

    private static async Task RequireTemporaryStorageAsync(
        ManifestDbContext database,
        ReviewItem item)
    {
        if (item.Status != EvidenceStatus.InCustody ||
            item.LocationId is not Guid locationId)
        {
            throw new InvalidOperationException(
                "The item must be in custody in temporary storage.");
        }

        var source = await database.StorageLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == locationId);

        // An inactive locker may still contain property that
        // authorized staff need to review and collect.
        if (source is null ||
            source.LocationType != StorageLocationType.Temporary)
        {
            throw new InvalidOperationException(
                "The item is no longer in temporary storage. " +
                "Refresh and review its custody history.");
        }
    }

    private static async Task<ReviewItem> LoadItemAsync(
        ManifestDbContext database,
        IntakeReview review)
    {
        if (review.EvidenceItemId is Guid evidenceId)
        {
            var item = await database.EvidenceItems
                .SingleAsync(x => x.Id == evidenceId);

            return new ReviewItem(
                PropertyRegisterKind.Evidence,
                item.Id,
                nameof(EvidenceItem),
                item.CaseNumber,
                item.ItemNumber,
                item.Status,
                item.CurrentStorageLocationId,
                locationId => item.CurrentStorageLocationId = locationId);
        }

        if (review.FoundPropertyItemId is Guid foundId)
        {
            var item = await database.FoundPropertyItems
                .SingleAsync(x => x.Id == foundId);

            return new ReviewItem(
                PropertyRegisterKind.FoundProperty,
                item.Id,
                nameof(FoundPropertyItem),
                item.ReportNumber,
                item.ItemNumber,
                item.Status,
                item.CurrentStorageLocationId,
                locationId => item.CurrentStorageLocationId = locationId);
        }

        if (review.SafekeepingItemId is Guid safekeepingId)
        {
            var item = await database.SafekeepingItems
                .SingleAsync(x => x.Id == safekeepingId);

            return new ReviewItem(
                PropertyRegisterKind.Safekeeping,
                item.Id,
                nameof(SafekeepingItem),
                item.ReportNumber,
                item.ItemNumber,
                item.Status,
                item.CurrentStorageLocationId,
                locationId => item.CurrentStorageLocationId = locationId);
        }

        throw new InvalidOperationException(
            "The review has no linked property item.");
    }

    private sealed record ReviewItem(
        PropertyRegisterKind Register,
        Guid Id,
        string EntityType,
        string ReportNumber,
        string ItemNumber,
        EvidenceStatus Status,
        Guid? LocationId,
        Action<Guid> SetLocation);
}