using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddRawMaterialUnitsAndSpecification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Specification",
                table: "RawMaterials",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "RawMaterials",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "kg");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Specification",
                table: "RawMaterials");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "RawMaterials");
        }
    }
}
