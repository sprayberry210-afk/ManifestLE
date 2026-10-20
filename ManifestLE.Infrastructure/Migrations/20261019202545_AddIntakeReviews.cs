using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ManifestLE.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EvidenceItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FoundPropertyItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SafekeepingItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CorrectionInstructions = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    LastReviewedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AcceptedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeReviews", x => x.Id);
                    table.CheckConstraint("CK_IntakeReviews_Acceptance", "(\r\n    \"Status\" = 'Accepted'\r\n    AND \"AcceptedByUserId\" IS NOT NULL\r\n    AND \"AcceptedAt\" IS NOT NULL\r\n)\r\nOR\r\n(\r\n    \"Status\" <> 'Accepted'\r\n    AND \"AcceptedByUserId\" IS NULL\r\n    AND \"AcceptedAt\" IS NULL\r\n)");
                    table.CheckConstraint("CK_IntakeReviews_CorrectionInstructions", "length(\"CorrectionInstructions\") <= 4000\r\nAND\r\n(\r\n    \"Status\" NOT IN (\r\n        'NeedsCorrection',\r\n        'Resubmitted'\r\n    )\r\n    OR length(trim(\"CorrectionInstructions\")) > 0\r\n)");
                    table.CheckConstraint("CK_IntakeReviews_ExactlyOneItem", "(\r\n    CASE WHEN \"EvidenceItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"FoundPropertyItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n    +\r\n    CASE WHEN \"SafekeepingItemId\" IS NOT NULL\r\n        THEN 1 ELSE 0 END\r\n) = 1");
                    table.CheckConstraint("CK_IntakeReviews_Reviewer", "(\r\n    \"Status\" = 'PendingReview'\r\n    AND \"LastReviewedByUserId\" IS NULL\r\n    AND \"LastReviewedAt\" IS NULL\r\n)\r\nOR\r\n(\r\n    \"Status\" IN (\r\n        'NeedsCorrection',\r\n        'Resubmitted',\r\n        'Accepted'\r\n    )\r\n    AND \"LastReviewedByUserId\" IS NOT NULL\r\n    AND \"LastReviewedAt\" IS NOT NULL\r\n)");
                    table.CheckConstraint("CK_IntakeReviews_Revision", "\"Revision\" >= 0");
                    table.CheckConstraint("CK_IntakeReviews_Status", "\"Status\" IN (\r\n    'PendingReview',\r\n    'NeedsCorrection',\r\n    'Resubmitted',\r\n    'Accepted'\r\n)");
                    table.ForeignKey(
                        name: "FK_IntakeReviews_EvidenceItems_EvidenceItemId",
                        column: x => x.EvidenceItemId,
                        principalTable: "EvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviews_FoundPropertyItems_FoundPropertyItemId",
                        column: x => x.FoundPropertyItemId,
                        principalTable: "FoundPropertyItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviews_SafekeepingItems_SafekeepingItemId",
                        column: x => x.SafekeepingItemId,
                        principalTable: "SafekeepingItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviews_Users_AcceptedByUserId",
                        column: x => x.AcceptedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviews_Users_LastReviewedByUserId",
                        column: x => x.LastReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviews_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_AcceptedByUserId",
                table: "IntakeReviews",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_EvidenceItemId",
                table: "IntakeReviews",
                column: "EvidenceItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_FoundPropertyItemId",
                table: "IntakeReviews",
                column: "FoundPropertyItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_LastReviewedByUserId",
                table: "IntakeReviews",
                column: "LastReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_SafekeepingItemId",
                table: "IntakeReviews",
                column: "SafekeepingItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_Status_SubmittedByUserId",
                table: "IntakeReviews",
                columns: new[] { "Status", "SubmittedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviews_SubmittedByUserId",
                table: "IntakeReviews",
                column: "SubmittedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeReviews");
        }
    }
}
