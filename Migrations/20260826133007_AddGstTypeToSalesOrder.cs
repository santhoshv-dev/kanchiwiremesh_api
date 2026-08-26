using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddGstTypeToSalesOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Subtotal",
                table: "SalesOrders",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(48)",
                oldMaxLength: 48);

            migrationBuilder.AddColumn<string>(
                name: "GstType",
                table: "SalesOrders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GstType",
                table: "SalesOrders");

            migrationBuilder.AlterColumn<decimal>(
                name: "Subtotal",
                table: "SalesOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "SalesOrders",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);
        }
    }
}
