using ManifestLE.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ManifestLE.Infrastructure.Data;

public sealed class IntakeReviewConfiguration
    : IEntityTypeConfiguration<IntakeReview>
{
    public void Configure(
        EntityTypeBuilder<IntakeReview> entity)
    {
        entity.ToTable("IntakeReviews", table =>
        {
            table.HasCheckConstraint(
                "CK_IntakeReviews_ExactlyOneItem",
                """
                (
                    CASE WHEN "EvidenceItemId" IS NOT NULL
                        THEN 1 ELSE 0 END
                    +
                    CASE WHEN "FoundPropertyItemId" IS NOT NULL
                        THEN 1 ELSE 0 END
                    +
                    CASE WHEN "SafekeepingItemId" IS NOT NULL
                        THEN 1 ELSE 0 END
                ) = 1
                """);

            table.HasCheckConstraint(
                "CK_IntakeReviews_Status",
                """
                "Status" IN (
                    'PendingReview',
                    'NeedsCorrection',
                    'Resubmitted',
                    'Accepted'
                )
                """);

            table.HasCheckConstraint(
                "CK_IntakeReviews_Acceptance",
                """
                (
                    "Status" = 'Accepted'
                    AND "AcceptedByUserId" IS NOT NULL
                    AND "AcceptedAt" IS NOT NULL
                )
                OR
                (
                    "Status" <> 'Accepted'
                    AND "AcceptedByUserId" IS NULL
                    AND "AcceptedAt" IS NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_IntakeReviews_Reviewer",
                """
                (
                    "Status" = 'PendingReview'
                    AND "LastReviewedByUserId" IS NULL
                    AND "LastReviewedAt" IS NULL
                )
                OR
                (
                    "Status" IN (
                        'NeedsCorrection',
                        'Resubmitted',
                        'Accepted'
                    )
                    AND "LastReviewedByUserId" IS NOT NULL
                    AND "LastReviewedAt" IS NOT NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_IntakeReviews_CorrectionInstructions",
                """
                length("CorrectionInstructions") <= 4000
                AND
                (
                    "Status" NOT IN (
                        'NeedsCorrection',
                        'Resubmitted'
                    )
                    OR length(trim("CorrectionInstructions")) > 0
                )
                """);

            table.HasCheckConstraint(
                "CK_IntakeReviews_Revision",
                """
                "Revision" >= 0
                """);
        });

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        entity.Property(x => x.CorrectionInstructions)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(x => x.Revision)
            .IsConcurrencyToken();

        entity.HasOne<EvidenceItem>()
            .WithMany()
            .HasForeignKey(x => x.EvidenceItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<FoundPropertyItem>()
            .WithMany()
            .HasForeignKey(x => x.FoundPropertyItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<SafekeepingItem>()
            .WithMany()
            .HasForeignKey(x => x.SafekeepingItemId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.LastReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Each property item can have only one review record.
        entity.HasIndex(x => x.EvidenceItemId)
            .IsUnique();

        entity.HasIndex(x => x.FoundPropertyItemId)
            .IsUnique();

        entity.HasIndex(x => x.SafekeepingItemId)
            .IsUnique();

        entity.HasIndex(x => new
        {
            x.Status,
            x.SubmittedByUserId
        });
    }
}