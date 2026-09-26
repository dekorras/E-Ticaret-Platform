using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dekorras.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWallCoveringModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationJson",
                table: "OrderItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PricingSnapshotJson",
                table: "OrderItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PerUserLimit",
                table: "Coupons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfigHash",
                table: "CartItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfigurationJson",
                table: "CartItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceSnapshotJson",
                table: "CartItem",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DesignRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmbedClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedOrigins = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedImageHosts = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DailyQuota = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UsageDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbedClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmbedClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUrlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PreviewUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThumbUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WidthPx = table.Column<int>(type: "int", nullable: false),
                    HeightPx = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Features = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PricePerM2 = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PanelWidthCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxHeightCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WeightGsm = table.Column<int>(type: "int", nullable: false),
                    FireRating = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsSelfAdhesive = table.Column<bool>(type: "bit", nullable: false),
                    RequiresGlue = table.Column<bool>(type: "bit", nullable: false),
                    BleedCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinBillableAreaM2 = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SampleProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GlueProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FileKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PanelCount = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionProofs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviewUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomerNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoApproveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoApproved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionProofs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductMaterialOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricePerM2 = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    IsAllowed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMaterialOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomPreviewRenders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RoomSceneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomPreviewRenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoomScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    RoomType = table.Column<int>(type: "int", nullable: false),
                    BaseImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShadowMapUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ForegroundMaskUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageWidthPx = table.Column<int>(type: "int", nullable: false),
                    ImageHeightPx = table.Column<int>(type: "int", nullable: false),
                    TopLeftX = table.Column<double>(type: "float", nullable: false),
                    TopLeftY = table.Column<double>(type: "float", nullable: false),
                    TopRightX = table.Column<double>(type: "float", nullable: false),
                    TopRightY = table.Column<double>(type: "float", nullable: false),
                    BottomRightX = table.Column<double>(type: "float", nullable: false),
                    BottomRightY = table.Column<double>(type: "float", nullable: false),
                    BottomLeftX = table.Column<double>(type: "float", nullable: false),
                    BottomLeftY = table.Column<double>(type: "float", nullable: false),
                    RealWallWidthCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RealWallHeightCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    OwnerKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomScenes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Group = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Hex = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TryOnLists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ShareToken = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TryOnLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WallpaperProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductType = table.Column<int>(type: "int", nullable: false),
                    RepeatWidthCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RepeatHeightCm = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RepeatType = table.Column<int>(type: "int", nullable: false),
                    OriginalImageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImageWidthPx = table.Column<int>(type: "int", nullable: false),
                    ImageHeightPx = table.Column<int>(type: "int", nullable: false),
                    Dpi = table.Column<int>(type: "int", nullable: true),
                    AspectRatio = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DominantColors = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ThumbUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ListUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreviewUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LqipBase64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SceneThumbUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DerivativesGeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PopularityScore = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WallpaperProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WallPreviewEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitorKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WallPreviewEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DesignRequestAttachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesignRequestAttachment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesignRequestAttachment_DesignRequests_DesignRequestId",
                        column: x => x.DesignRequestId,
                        principalTable: "DesignRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TryOnListItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TryOnListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TryOnListItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TryOnListItem_TryOnLists_TryOnListId",
                        column: x => x.TryOnListId,
                        principalTable: "TryOnLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DesignRequestAttachment_DesignRequestId",
                table: "DesignRequestAttachment",
                column: "DesignRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DesignRequests_Status_DueAtUtc",
                table: "DesignRequests",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmbedClients_PublicKey",
                table: "EmbedClients",
                column: "PublicKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalImages_EmbedClientId_SourceUrlHash",
                table: "ExternalImages",
                columns: new[] { "EmbedClientId", "SourceUrlHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Code",
                table: "Materials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionFiles_OrderId",
                table: "ProductionFiles",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionFiles_OrderItemId",
                table: "ProductionFiles",
                column: "OrderItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionProofs_OrderId",
                table: "ProductionProofs",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionProofs_OrderItemId",
                table: "ProductionProofs",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionProofs_Token",
                table: "ProductionProofs",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductMaterialOverrides_ProductId_MaterialId",
                table: "ProductMaterialOverrides",
                columns: new[] { "ProductId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_ProductId_TagId",
                table: "ProductTags",
                columns: new[] { "ProductId", "TagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductTags_TagId",
                table: "ProductTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomPreviewRenders_OwnerKey_CreatedAtUtc",
                table: "RoomPreviewRenders",
                columns: new[] { "OwnerKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomScenes_OwnerKey",
                table: "RoomScenes",
                column: "OwnerKey");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Group_Value",
                table: "Tags",
                columns: new[] { "Group", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TryOnListItem_TryOnListId",
                table: "TryOnListItem",
                column: "TryOnListId");

            migrationBuilder.CreateIndex(
                name: "IX_TryOnLists_OwnerKey",
                table: "TryOnLists",
                column: "OwnerKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TryOnLists_ShareToken",
                table: "TryOnLists",
                column: "ShareToken",
                unique: true,
                filter: "[ShareToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WallpaperProfiles_IsEnabled_PopularityScore",
                table: "WallpaperProfiles",
                columns: new[] { "IsEnabled", "PopularityScore" });

            migrationBuilder.CreateIndex(
                name: "IX_WallpaperProfiles_ProductId",
                table: "WallpaperProfiles",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WallPreviewEvents_EventType_OccurredAtUtc",
                table: "WallPreviewEvents",
                columns: new[] { "EventType", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DesignRequestAttachment");

            migrationBuilder.DropTable(
                name: "EmbedClients");

            migrationBuilder.DropTable(
                name: "ExternalImages");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "ProductionFiles");

            migrationBuilder.DropTable(
                name: "ProductionProofs");

            migrationBuilder.DropTable(
                name: "ProductMaterialOverrides");

            migrationBuilder.DropTable(
                name: "ProductTags");

            migrationBuilder.DropTable(
                name: "RoomPreviewRenders");

            migrationBuilder.DropTable(
                name: "RoomScenes");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "TryOnListItem");

            migrationBuilder.DropTable(
                name: "WallpaperProfiles");

            migrationBuilder.DropTable(
                name: "WallPreviewEvents");

            migrationBuilder.DropTable(
                name: "DesignRequests");

            migrationBuilder.DropTable(
                name: "TryOnLists");

            migrationBuilder.DropColumn(
                name: "ConfigurationJson",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "PricingSnapshotJson",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "PerUserLimit",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "ConfigHash",
                table: "CartItem");

            migrationBuilder.DropColumn(
                name: "ConfigurationJson",
                table: "CartItem");

            migrationBuilder.DropColumn(
                name: "PriceSnapshotJson",
                table: "CartItem");
        }
    }
}
