using KanchimeshAPI.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    [DbContext(typeof(KanchimeshDbContext))]
    [Migration("20260822143000_DisableForcedPasswordChanges")]
    public partial class DisableForcedPasswordChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "ApplicationUsers",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            // Existing bootstrap accounts should not be routed to a mandatory
            // password-change experience after this release.
            migrationBuilder.Sql(
                "UPDATE [ApplicationUsers] SET [MustChangePassword] = CAST(0 AS bit) WHERE [MustChangePassword] = CAST(1 AS bit);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "MustChangePassword",
                table: "ApplicationUsers",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);
        }
    }
}
