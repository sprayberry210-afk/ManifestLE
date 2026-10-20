using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class IntakeCorrectionService
{
    public static IReadOnlyList<string> PackagingOptions { get; } =
        Array.AsReadOnly(new[]
        {
            "Paper bag",
            "Evidence envelope",
            "Evidence box",
            "Plastic bag",
            "Sharps container",
            "Firearm box",
            "Other / see notes",
            "Unpackaged / see notes"
        });

    private readonly Func<ManifestDbContext> _createContext;

    public IntakeCorrectionService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public IntakeCorrectionService(
        Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task<IntakeCorrectionDetails> GetAsync(
        Guid userId,
        Guid reviewId)
    {
        await using var database = _createContext();

        var review = await RequireOwnedReviewAsync(
            database,
            userId,
            reviewId);

        var item = await LoadItemAsync(database, review);

        await RequireTemporaryCustodyAsync(database, item);

        return new IntakeCorrectionDetails(
            review.Id,
            review.Revision,
            item.Register,
            item.ReportNumber,
            item.ItemNumber,
            item.Description,
            item.Packaging,
            item.Notes,
            review.CorrectionInstructions);
    }

    public async Task ResubmitAsync(
        Guid userId,
        Guid reviewId,
        int expectedRevision,
        string description,
        string packaging,
        string notes)
    {
        description = (description ?? string.Empty).Trim();
        packaging = (packaging ?? string.Empty).Trim();
        notes = (notes ?? string.Empty).Trim();

        await using var database = _createContext();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

        var review = await RequireOwnedReviewAsync(
            database,
            userId,
            reviewId);

        if (review.Revision != expectedRevision)
        {
            throw new InvalidOperationException(
                "This submission changed after you opened it. " +
                "Refresh and reopen the correction form.");
        }

        ValidateCorrection(description, packaging, notes);

        var item = await LoadItemAsync(database, review);

        await RequireTemporaryCustodyAsync(database, item);

        string previousDescription = item.Description;
        string previousPackaging = item.Packaging;
        string previousNotes = item.Notes;

        item.ApplyCorrection(description, packaging, notes);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        review.Status = IntakeReviewStatus.Resubmitted;
        review.LastSubmittedAt = now;
        review.Revision = checked(review.Revision + 1);

        // Preserve the original submitter, submission time,
        // correction instructions, and last review details.
        database.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = userId,
            RecordedAt = now,
            Action = "IntakeReview.Resubmitted",
            EntityType = item.EntityType,
            EntityId = item.Id,
            Details =
                $"Report {item.ReportNumber}, item {item.ItemNumber}. " +
                $"Review ID: {review.Id}. " +
                "Status: NeedsCorrection -> Resubmitted.\n\n" +
                $"Previous description: {previousDescription}\n" +
                $"Corrected description: {description}\n\n" +
                $"Previous packaging: {previousPackaging}\n" +
                $"Corrected packaging: {packaging}\n\n" +
                $"Previous intake notes: {previousNotes}\n" +
                $"Corrected intake notes: {notes}\n\n" +
                $"Correction instructions: {review.CorrectionInstructions}"
        });

        // Documentation changes do not create a custody movement.
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateCorrection(
        string description,
        string packaging,
        string notes)
    {
        if (string.IsNullOrWhiteSpace(description) ||
            description.Length > 2000)
        {
            throw new InvalidOperationException(
                "Enter a description of 1–2000 characters.");
        }

        if (!PackagingOptions.Contains(
            packaging,
            StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Choose a valid packaging option.");
        }

        if (notes.Length > 4000)
        {
            throw new InvalidOperationException(
                "Use intake notes of no more than 4000 characters.");
        }

        if ((packaging == "Other / see notes" ||
             packaging == "Unpackaged / see notes") &&
            string.IsNullOrWhiteSpace(notes))
        {
            throw new InvalidOperationException(
                "Describe the packaging circumstances in intake notes.");
        }
    }

    private static async Task<IntakeReview> RequireOwnedReviewAsync(
        ManifestDbContext database,
        Guid userId,
        Guid reviewId)
    {
        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId);

        if (user is null ||
            !user.IsActive ||
            (user.Role != UserRole.Officer &&
             user.Role != UserRole.EvidenceCustodian &&
             user.Role != UserRole.Administrator))
        {
            throw new InvalidOperationException(
                "An active account is required to correct a submission.");
        }

        var review = await database.IntakeReviews
            .SingleOrDefaultAsync(x =>
                x.Id == reviewId &&
                x.SubmittedByUserId == userId);

        if (review is null)
        {
            throw new InvalidOperationException(
                "You can correct only submissions made under your account.");
        }

        if (review.Status != IntakeReviewStatus.NeedsCorrection)
        {
            throw new InvalidOperationException(
                "Only submissions marked Needs Correction can be edited.");
        }

        return review;
    }

    private static async Task RequireTemporaryCustodyAsync(
        ManifestDbContext database,
        CorrectionItem item)
    {
        if (item.Status != EvidenceStatus.InCustody ||
            item.LocationId is not Guid locationId)
        {
            throw new InvalidOperationException(
                "The item must remain in temporary custody " +
                "while its intake documentation is corrected.");
        }

        bool temporary = await database.StorageLocations
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == locationId &&
                x.LocationType == StorageLocationType.Temporary);

        if (!temporary)
        {
            throw new InvalidOperationException(
                "The item's storage state changed. " +
                "Contact the evidence custodian before continuing.");
        }
    }

    private static async Task<CorrectionItem> LoadItemAsync(
        ManifestDbContext database,
        IntakeReview review)
    {
        if (review.EvidenceItemId is Guid evidenceId)
        {
            var item = await database.EvidenceItems
                .SingleAsync(x => x.Id == evidenceId);

            return new CorrectionItem(
                PropertyRegisterKind.Evidence,
                item.Id,
                nameof(EvidenceItem),
                item.CaseNumber,
                item.ItemNumber,
                item.Description,
                item.Packaging,
                item.Notes,
                item.Status,
                item.CurrentStorageLocationId,
                (description, packaging, notes) =>
                {
                    item.Description = description;
                    item.Packaging = packaging;
                    item.Notes = notes;
                });
        }

        if (review.FoundPropertyItemId is Guid foundId)
        {
            var item = await database.FoundPropertyItems
                .SingleAsync(x => x.Id == foundId);

            return new CorrectionItem(
                PropertyRegisterKind.FoundProperty,
                item.Id,
                nameof(FoundPropertyItem),
                item.ReportNumber,
                item.ItemNumber,
                item.Description,
                item.Packaging,
                item.Notes,
                item.Status,
                item.CurrentStorageLocationId,
                (description, packaging, notes) =>
                {
                    item.Description = description;
                    item.Packaging = packaging;
                    item.Notes = notes;
                });
        }

        if (review.SafekeepingItemId is Guid safekeepingId)
        {
            var item = await database.SafekeepingItems
                .SingleAsync(x => x.Id == safekeepingId);

            return new CorrectionItem(
                PropertyRegisterKind.Safekeeping,
                item.Id,
                nameof(SafekeepingItem),
                item.ReportNumber,
                item.ItemNumber,
                item.Description,
                item.Packaging,
                item.Notes,
                item.Status,
                item.CurrentStorageLocationId,
                (description, packaging, notes) =>
                {
                    item.Description = description;
                    item.Packaging = packaging;
                    item.Notes = notes;
                });
        }

        throw new InvalidOperationException(
            "The review has no linked property item.");
    }

    private sealed record CorrectionItem(
        PropertyRegisterKind Register,
        Guid Id,
        string EntityType,
        string ReportNumber,
        string ItemNumber,
        string Description,
        string Packaging,
        string Notes,
        EvidenceStatus Status,
        Guid? LocationId,
        Action<string, string, string> ApplyCorrection);
}

public sealed record IntakeCorrectionDetails(
    Guid ReviewId,
    int Revision,
    PropertyRegisterKind Register,
    string ReportNumber,
    string ItemNumber,
    string Description,
    string Packaging,
    string Notes,
    string CorrectionInstructions);