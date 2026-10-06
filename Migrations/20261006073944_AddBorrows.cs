using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Borrows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BorrowNumber = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    BorrowerName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    BorrowDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Borrows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BorrowRepayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepaymentNumber = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    BorrowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorrowRepayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BorrowRepayments_Borrows_BorrowId",
                        column: x => x.BorrowId,
                        principalTable: "Borrows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRepayments_BorrowId",
                table: "BorrowRepayments",
                column: "BorrowId");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRepayments_PaymentDate",
                table: "BorrowRepayments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRepayments_RepaymentNumber",
                table: "BorrowRepayments",
                column: "RepaymentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Borrows_BorrowDate",
                table: "Borrows",
                column: "BorrowDate");

            migrationBuilder.CreateIndex(
                name: "IX_Borrows_BorrowerName",
                table: "Borrows",
                column: "BorrowerName");

            migrationBuilder.CreateIndex(
                name: "IX_Borrows_BorrowNumber",
                table: "Borrows",
                column: "BorrowNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BorrowRepayments");

            migrationBuilder.DropTable(
                name: "Borrows");
        }
    }
}
