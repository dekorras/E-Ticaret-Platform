using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dekorras.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductVariantSkuIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "ProductVariant",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_Sku",
                table: "ProductVariant",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariant_Sku",
                table: "ProductVariant");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "ProductVariant",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
