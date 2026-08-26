using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitGstColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GstRate",
                table: "SalesOrderItems",
                newName: "SgstRate");

            migrationBuilder.RenameColumn(
                name: "GstRate",
                table: "Products",
                newName: "SgstRate");

            migrationBuilder.AddColumn<decimal>(
                name: "CgstRate",
                table: "SalesOrderItems",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstRate",
                table: "SalesOrderItems",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CgstRate",
                table: "Products",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstRate",
                table: "Products",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CgstRate",
                table: "SalesOrderItems");

            migrationBuilder.DropColumn(
                name: "IgstRate",
                table: "SalesOrderItems");

            migrationBuilder.DropColumn(
                name: "CgstRate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IgstRate",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "SgstRate",
                table: "SalesOrderItems",
                newName: "GstRate");

            migrationBuilder.RenameColumn(
                name: "SgstRate",
                table: "Products",
                newName: "GstRate");
        }
    }
}
