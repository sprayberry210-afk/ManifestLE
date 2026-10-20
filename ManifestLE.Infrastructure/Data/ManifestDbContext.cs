using ManifestLE.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ManifestLE.Infrastructure.Data;

public class ManifestDbContext : DbContext
{
    public ManifestDbContext(
        DbContextOptions<ManifestDbContext> options)
        : base(options)
    {
    }

    public DbSet<Agency> Agencies => Set<Agency>();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<UserCredential> UserCredentials =>
        Set<UserCredential>();

    public DbSet<EvidenceItem> EvidenceItems =>
        Set<EvidenceItem>();

    public DbSet<FoundPropertyItem> FoundPropertyItems =>
        Set<FoundPropertyItem>();

    public DbSet<SafekeepingItem> SafekeepingItems =>
        Set<SafekeepingItem>();

    public DbSet<StorageLocation> StorageLocations =>
        Set<StorageLocation>();

    public DbSet<CustodyEvent> CustodyEvents =>
        Set<CustodyEvent>();

    public DbSet<AuditLogEntry> AuditLogEntries =>
        Set<AuditLogEntry>();

    public DbSet<InventoryAudit> InventoryAudits =>
        Set<InventoryAudit>();

    public DbSet<InventoryAuditItem> InventoryAuditItems =>
        Set<InventoryAuditItem>();

    public DbSet<InventoryAuditFollowUp> InventoryAuditFollowUps =>
        Set<InventoryAuditFollowUp>();

    public DbSet<IntakeReview> IntakeReviews =>
        Set<IntakeReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Agency>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.AgencyCode).IsRequired();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Username).IsRequired();
            entity.Property(x => x.DisplayName).IsRequired();

            entity.HasIndex(x => x.Username).IsUnique();

            entity.Property(x => x.Role)
                .HasConversion<string>();
        });

        modelBuilder.Entity<UserCredential>(entity =>
        {
            entity.HasKey(x => x.UserId);

            entity.Property(x => x.PasswordHash).IsRequired();

            entity.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<UserCredential>(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StorageLocation>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name).IsRequired();

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.ParentLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EvidenceItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.CaseNumber).IsRequired();
            entity.Property(x => x.ItemNumber).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.CollectedBy).IsRequired();

            entity.HasIndex(x => new
            {
                x.CaseNumber,
                x.ItemNumber
            }).IsUnique();

            entity.Property(x => x.Status)
                .HasConversion<string>();

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.CurrentStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FoundPropertyItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ReportNumber).IsRequired();
            entity.Property(x => x.ItemNumber).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.CollectedBy).IsRequired();

            entity.HasIndex(x => new
            {
                x.ReportNumber,
                x.ItemNumber
            }).IsUnique();

            entity.Property(x => x.Status)
                .HasConversion<string>();

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.CurrentStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SafekeepingItem>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ReportNumber).IsRequired();
            entity.Property(x => x.ItemNumber).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.CollectedBy).IsRequired();
            entity.Property(x => x.HoldReason).IsRequired();
            entity.Property(x => x.HoldAuthority).IsRequired();

            entity.HasIndex(x => new
            {
                x.ReportNumber,
                x.ItemNumber
            }).IsUnique();

            entity.Property(x => x.Status)
                .HasConversion<string>();

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.CurrentStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustodyEvent>(entity =>
        {
            entity.ToTable("CustodyEvents", table =>
            {
                table.HasCheckConstraint(
                    "CK_CustodyEvents_ExactlyOneItem",
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
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Reason).IsRequired();

            entity.Property(x => x.EventType)
                .HasConversion<string>();

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
                .HasForeignKey(x => x.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.FromStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.ToStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => x.EvidenceItemId);
            entity.HasIndex(x => x.FoundPropertyItemId);
            entity.HasIndex(x => x.SafekeepingItemId);
        });

        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action).IsRequired();
            entity.Property(x => x.EntityType).IsRequired();

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.EntityType,
                x.EntityId
            });
        });

        modelBuilder.Entity<InventoryAudit>(entity =>
        {
            entity.ToTable("InventoryAudits", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryAudits_Status",
                    """
                    "Status" IN ('InProgress', 'Completed')
                    """);

                table.HasCheckConstraint(
                    "CK_InventoryAudits_Completion",
                    """
                    (
                        "Status" = 'InProgress'
                        AND "CompletedByUserId" IS NULL
                        AND "CompletedAt" IS NULL
                    )
                    OR
                    (
                        "Status" = 'Completed'
                        AND "CompletedByUserId" IS NOT NULL
                        AND "CompletedAt" IS NOT NULL
                    )
                    """);
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.StoragePathSnapshot)
                .IsRequired();

            entity.Property(x => x.Notes)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasConversion<string>()
                .IsRequired();

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.StorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.StartedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(x => x.Items)
                .WithOne(x => x.InventoryAudit)
                .HasForeignKey(x => x.InventoryAuditId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.StorageLocationId,
                x.Status
            });
        });

        modelBuilder.Entity<InventoryAuditItem>(entity =>
        {
            entity.ToTable("InventoryAuditItems", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryAuditItems_ExactlyOneItem",
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
                    "CK_InventoryAuditItems_Result",
                    """
                    "Result" IN (
                        'Pending',
                        'Verified',
                        'Missing',
                        'Discrepancy'
                    )
                    """);

                table.HasCheckConstraint(
                    "CK_InventoryAuditItems_CheckDetails",
                    """
                    (
                        "Result" = 'Pending'
                        AND "CheckedByUserId" IS NULL
                        AND "CheckedAt" IS NULL
                    )
                    OR
                    (
                        "Result" IN ('Verified', 'Missing', 'Discrepancy')
                        AND "CheckedByUserId" IS NOT NULL
                        AND "CheckedAt" IS NOT NULL
                    )
                    """);

                table.HasCheckConstraint(
                    "CK_InventoryAuditItems_ExpectedStatus",
                    """
                    "ExpectedStatus" = 'InCustody'
                    """);
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.ReportNumberSnapshot)
                .IsRequired();

            entity.Property(x => x.ItemNumberSnapshot)
                .IsRequired();

            entity.Property(x => x.DescriptionSnapshot)
                .IsRequired();

            entity.Property(x => x.ExpectedStoragePathSnapshot)
                .IsRequired();

            entity.Property(x => x.Notes)
                .IsRequired();

            entity.Property(x => x.ExpectedStatus)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(x => x.Result)
                .HasConversion<string>()
                .IsRequired();

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

            entity.HasOne<StorageLocation>()
                .WithMany()
                .HasForeignKey(x => x.ExpectedStorageLocationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.CheckedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.InventoryAuditId,
                x.EvidenceItemId
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.InventoryAuditId,
                x.FoundPropertyItemId
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.InventoryAuditId,
                x.SafekeepingItemId
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.InventoryAuditId,
                x.Result
            });
        });

        modelBuilder.Entity<InventoryAuditFollowUp>(entity =>
        {
            entity.ToTable("InventoryAuditFollowUps", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryAuditFollowUps_Type",
                    """
                    "Type" IN ('InvestigationNote', 'Resolved')
                    """);

                table.HasCheckConstraint(
                    "CK_InventoryAuditFollowUps_Notes",
                    """
                    length(trim("Notes")) > 0
                    AND length("Notes") <= 4000
                    """);
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Type)
                .HasConversion<string>()
                .IsRequired();

            entity.Property(x => x.Notes)
                .IsRequired()
                .HasMaxLength(4000);

            entity.HasOne(x => x.InventoryAuditItem)
                .WithMany()
                .HasForeignKey(x => x.InventoryAuditItemId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(x => x.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.InventoryAuditItemId,
                x.RecordedAt
            });

            entity.HasIndex(x => x.InventoryAuditItemId)
                .HasDatabaseName(
                    "UX_InventoryAuditFollowUps_OneResolutionPerItem")
                .IsUnique()
                .HasFilter("\"Type\" = 'Resolved'");
        });

        modelBuilder.ApplyConfiguration(
            new IntakeReviewConfiguration());
    }
}