using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiDocumentIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractedTextToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "Documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "Documents");
        }
    }
}
