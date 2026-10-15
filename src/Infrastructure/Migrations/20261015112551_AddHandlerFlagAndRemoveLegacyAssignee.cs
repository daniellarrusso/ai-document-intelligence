using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDocumentIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHandlerFlagAndRemoveLegacyAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "Claims");

            migrationBuilder.AddColumn<bool>(
                name: "IsHandler",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsHandler",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "Claims",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
