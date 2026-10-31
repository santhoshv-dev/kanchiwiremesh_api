using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDispatchDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillOfLading",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyersOrderDate",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyersOrderNo",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNote",
                table: "SalesOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryNoteDate",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Destination",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchDocNo",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispatchedThrough",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeOfPayment",
                table: "SalesOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotorVehicleNo",
                table: "SalesOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OtherReferences",
                table: "SalesOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNo",
                table: "SalesOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillOfLading",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BuyersOrderDate",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "BuyersOrderNo",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DeliveryNote",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DeliveryNoteDate",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Destination",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DispatchDocNo",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DispatchedThrough",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ModeOfPayment",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "MotorVehicleNo",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "OtherReferences",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ReferenceNo",
                table: "SalesOrders");
        }
    }
}
