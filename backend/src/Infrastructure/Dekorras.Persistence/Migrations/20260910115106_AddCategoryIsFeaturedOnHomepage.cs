using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dekorras.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIsFeaturedOnHomepage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFeaturedOnHomepage",
                table: "Categories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFeaturedOnHomepage",
                table: "Categories");
        }
    }
}
