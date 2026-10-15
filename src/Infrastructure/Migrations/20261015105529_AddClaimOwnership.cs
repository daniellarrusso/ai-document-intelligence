using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDocumentIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UploadedByUserId",
                table: "Documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToUserId",
                table: "Claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Claims",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Claims",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_AssignedToUserId",
                table: "Claims",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_CreatedByUserId",
                table: "Claims",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_Users_AssignedToUserId",
                table: "Claims",
                column: "AssignedToUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Claims_Users_CreatedByUserId",
                table: "Claims",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Claims_Users_AssignedToUserId",
                table: "Claims");

            migrationBuilder.DropForeignKey(
                name: "FK_Claims_Users_CreatedByUserId",
                table: "Claims");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Claims_AssignedToUserId",
                table: "Claims");

            migrationBuilder.DropIndex(
                name: "IX_Claims_CreatedByUserId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Claims");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Claims");
        }
    }
}
