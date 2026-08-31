using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseNumber = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ProductCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    BuyerContactNumber = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    BuyerGstNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    BuyerLocation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupplierName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    PurchaseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    QuantityPurchased = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PurchaseAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    PaymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRecords_PurchaseDate",
                table: "PurchaseRecords",
                column: "PurchaseDate");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRecords_PurchaseNumber",
                table: "PurchaseRecords",
                column: "PurchaseNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseRecords");
        }
    }
}
