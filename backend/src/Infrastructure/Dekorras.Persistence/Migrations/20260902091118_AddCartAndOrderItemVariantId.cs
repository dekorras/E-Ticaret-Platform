using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dekorras.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCartAndOrderItemVariantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "OrderItem",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "CartItem",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "CartItem");
        }
    }
}
