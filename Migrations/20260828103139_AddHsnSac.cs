using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddHsnSac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HsnSac",
                table: "SalesOrderItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnSac",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HsnSac",
                table: "SalesOrderItems");

            migrationBuilder.DropColumn(
                name: "HsnSac",
                table: "Products");
        }
    }
}
