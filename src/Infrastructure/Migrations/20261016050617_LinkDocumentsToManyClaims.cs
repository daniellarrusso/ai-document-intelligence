using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDocumentIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkDocumentsToManyClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimDocuments",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimDocuments", x => new { x.ClaimId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Users_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_DocumentId",
                table: "ClaimDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_LinkedByUserId",
                table: "ClaimDocuments",
                column: "LinkedByUserId");

            // Carry every existing document-to-claim link over before the old column goes.
            migrationBuilder.Sql(
                """
                INSERT INTO "ClaimDocuments" ("ClaimId", "DocumentId", "LinkedAt", "LinkedByUserId")
                SELECT "ClaimId", "Id", "UploadedAt", "UploadedByUserId"
                FROM "Documents"
                WHERE "ClaimId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Claims_ClaimId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_ClaimId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ClaimId",
                table: "Documents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ClaimId",
                table: "Documents",
                column: "ClaimId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Claims_ClaimId",
                table: "Documents",
                column: "ClaimId",
                principalTable: "Claims",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A document can only keep one claim again: keep the one it was linked to first.
            migrationBuilder.Sql(
                """
                UPDATE "Documents" d
                SET "ClaimId" = l."ClaimId"
                FROM (
                    SELECT DISTINCT ON ("DocumentId") "DocumentId", "ClaimId"
                    FROM "ClaimDocuments"
                    ORDER BY "DocumentId", "LinkedAt"
                ) l
                WHERE l."DocumentId" = d."Id";
                """);

            migrationBuilder.DropTable(
                name: "ClaimDocuments");
        }
    }
}
