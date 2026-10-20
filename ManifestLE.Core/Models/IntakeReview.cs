namespace ManifestLE.Core.Models;

public enum IntakeReviewStatus
{
    PendingReview,
    NeedsCorrection,
    Resubmitted,
    Accepted
}

public class IntakeReview
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Exactly one property reference must be populated.
    public Guid? EvidenceItemId { get; set; }

    public Guid? FoundPropertyItemId { get; set; }

    public Guid? SafekeepingItemId { get; set; }

    public IntakeReviewStatus Status { get; set; }
        = IntakeReviewStatus.PendingReview;

    // Original submitting account remains attached to the review.
    public Guid SubmittedByUserId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
        = DateTimeOffset.UtcNow;

    // Updated when the officer resubmits a correction.
    public DateTimeOffset LastSubmittedAt { get; set; }
        = DateTimeOffset.UtcNow;

    // Current correction instructions.
    // Prior instructions will be preserved in audit entries.
    public string CorrectionInstructions { get; set; }
        = string.Empty;

    public Guid? LastReviewedByUserId { get; set; }

    public DateTimeOffset? LastReviewedAt { get; set; }

    public Guid? AcceptedByUserId { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    // Used to reject saves based on an outdated review.
    public int Revision { get; set; }
}