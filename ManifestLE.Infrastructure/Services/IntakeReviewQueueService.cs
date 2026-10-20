using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ManifestLE.Core.Models;
using ManifestLE.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Services;

public class IntakeReviewQueueService
{
    private readonly Func<ManifestDbContext> _createContext;

    public IntakeReviewQueueService()
        : this(DevelopmentDatabase.CreateContext)
    {
    }

    public IntakeReviewQueueService(
        Func<ManifestDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public async Task<bool> CanReviewAsync(Guid userId)
    {
        await using var database = _createContext();

        var user = await RequireUserAsync(database, userId);
        return IsReviewer(user);
    }

    public async Task<List<IntakeReviewDestination>>
        GetPermanentDestinationsAsync(Guid userId)
    {
        await using var database = _createContext();

        var user = await RequireUserAsync(database, userId);

        if (!IsReviewer(user))
        {
            throw new InvalidOperationException(
                "Only authorized reviewers can select permanent storage.");
        }

        var ids = await database.StorageLocations
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.LocationType == StorageLocationType.Permanent)
            .Select(x => x.Id)
            .ToListAsync();

        var labels = await new StorageDisplayService(
            _createContext).GetLabelsAsync(userId);

        return ids
            .Select(id => new IntakeReviewDestination(
                id,
                StorageDisplayService.GetLabel(id, labels)))
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<List<IntakeReviewQueueRow>> GetQueueAsync(
        Guid userId)
    {
        await using var database = _createContext();

        var user = await RequireUserAsync(database, userId);
        bool canReview = IsReviewer(user);

        var query = database.IntakeReviews.AsNoTracking();

        if (!canReview)
        {
            query = query.Where(x => x.SubmittedByUserId == userId);
        }

        var reviews = await query.ToListAsync();

        var evidenceIds = reviews
            .Where(x => x.EvidenceItemId.HasValue)
            .Select(x => x.EvidenceItemId!.Value)
            .ToList();

        var foundIds = reviews
            .Where(x => x.FoundPropertyItemId.HasValue)
            .Select(x => x.FoundPropertyItemId!.Value)
            .ToList();

        var safekeepingIds = reviews
            .Where(x => x.SafekeepingItemId.HasValue)
            .Select(x => x.SafekeepingItemId!.Value)
            .ToList();

        var evidence = await database.EvidenceItems
            .AsNoTracking()
            .Where(x => evidenceIds.Contains(x.Id))
            .Select(x => new QueueItem(
                x.Id,
                x.CaseNumber,
                x.ItemNumber,
                x.Description,
                x.CurrentStorageLocationId))
            .ToListAsync();

        var found = await database.FoundPropertyItems
            .AsNoTracking()
            .Where(x => foundIds.Contains(x.Id))
            .Select(x => new QueueItem(
                x.Id,
                x.ReportNumber,
                x.ItemNumber,
                x.Description,
                x.CurrentStorageLocationId))
            .ToListAsync();

        var safekeeping = await database.SafekeepingItems
            .AsNoTracking()
            .Where(x => safekeepingIds.Contains(x.Id))
            .Select(x => new QueueItem(
                x.Id,
                x.ReportNumber,
                x.ItemNumber,
                x.Description,
                x.CurrentStorageLocationId))
            .ToListAsync();

        var evidenceById = evidence.ToDictionary(x => x.Id);
        var foundById = found.ToDictionary(x => x.Id);
        var safekeepingById = safekeeping.ToDictionary(x => x.Id);

        var submitterIds = reviews
            .Select(x => x.SubmittedByUserId)
            .Distinct()
            .ToList();

        var submitterNames = await database.Users
            .AsNoTracking()
            .Where(x => submitterIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);

        var storageLabels = await new StorageDisplayService(
            _createContext).GetLabelsAsync(userId);

        var rows = new List<IntakeReviewQueueRow>();

        foreach (var review in reviews)
        {
            QueueItem item;
            PropertyRegisterKind register;

            if (review.EvidenceItemId is Guid evidenceId)
            {
                item = evidenceById[evidenceId];
                register = PropertyRegisterKind.Evidence;
            }
            else if (review.FoundPropertyItemId is Guid foundId)
            {
                item = foundById[foundId];
                register = PropertyRegisterKind.FoundProperty;
            }
            else if (review.SafekeepingItemId is Guid safekeepingId)
            {
                item = safekeepingById[safekeepingId];
                register = PropertyRegisterKind.Safekeeping;
            }
            else
            {
                throw new InvalidOperationException(
                    "An intake review has no linked property item.");
            }

            rows.Add(new IntakeReviewQueueRow
            {
                ReviewId = review.Id,
                ItemId = item.Id,
                Register = register,
                ReportNumber = item.ReportNumber,
                ItemNumber = item.ItemNumber,
                Description = item.Description,
                SubmittedByUserId = review.SubmittedByUserId,
                SubmittedBy = submitterNames.GetValueOrDefault(
                    review.SubmittedByUserId,
                    "Unknown user"),
                SubmittedAt = review.SubmittedAt,
                LastSubmittedAt = review.LastSubmittedAt,
                Status = review.Status,
                Revision = review.Revision,
                Storage = StorageDisplayService.GetLabel(
                    item.LocationId,
                    storageLabels),
                CurrentStorageLocationId = canReview
                    ? item.LocationId
                    : null,
                CorrectionInstructions = review.CorrectionInstructions
            });
        }

        return rows
            .OrderBy(x => x.Status == IntakeReviewStatus.Accepted)
            .ThenBy(x => x.SubmittedAt)
            .ThenBy(x => x.ReportNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ItemNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReviewId)
            .ToList();
    }

    private static bool IsReviewer(AppUser user)
    {
        return user.Role == UserRole.Administrator ||
               user.Role == UserRole.EvidenceCustodian;
    }

    private static async Task<AppUser> RequireUserAsync(
        ManifestDbContext database,
        Guid userId)
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
                "An active account is required to view intake reviews.");
        }

        return user;
    }

    private sealed record QueueItem(
        Guid Id,
        string ReportNumber,
        string ItemNumber,
        string Description,
        Guid? LocationId);
}

public sealed record IntakeReviewDestination(Guid Id, string Name);

public sealed class IntakeReviewQueueRow
{
    public Guid ReviewId { get; init; }

    public Guid ItemId { get; init; }

    public PropertyRegisterKind Register { get; init; }

    public string ReportNumber { get; init; } = string.Empty;

    public string ItemNumber { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public Guid SubmittedByUserId { get; init; }

    public string SubmittedBy { get; init; } = string.Empty;

    public DateTimeOffset SubmittedAt { get; init; }

    public DateTimeOffset LastSubmittedAt { get; init; }

    public IntakeReviewStatus Status { get; init; }

    public int Revision { get; init; }

    public string Storage { get; init; } = string.Empty;

    // Available only to authorized reviewers.
    public Guid? CurrentStorageLocationId { get; init; }

    public string CorrectionInstructions { get; init; } = string.Empty;

    public string RegisterText => Register switch
    {
        PropertyRegisterKind.Evidence => "Evidence",
        PropertyRegisterKind.FoundProperty => "Found Property",
        PropertyRegisterKind.Safekeeping => "Safekeeping",
        _ => "Unknown"
    };

    public string StatusText => Status switch
    {
        IntakeReviewStatus.PendingReview => "Pending Review",
        IntakeReviewStatus.NeedsCorrection => "Needs Correction",
        IntakeReviewStatus.Resubmitted => "Resubmitted",
        IntakeReviewStatus.Accepted => "Accepted",
        _ => "Unknown"
    };

    public string SubmittedLocal =>
        SubmittedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}