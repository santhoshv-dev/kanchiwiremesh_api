using System;
using KanchimeshAPI.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanchimeshAPI.Migrations
{
    [DbContext(typeof(KanchimeshDbContext))]
    [Migration("20260822121500_AddEnquiryEmailDeliveryJobsAndIdempotency")]
    public partial class AddEnquiryEmailDeliveryJobsAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicSubmissionKey",
                table: "Enquiries",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailDeliveryJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnquiryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailDeliveryJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailDeliveryJobs_Enquiries_EnquiryId",
                        column: x => x.EnquiryId,
                        principalTable: "Enquiries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Enquiries_PublicSubmissionKey",
                table: "Enquiries",
                column: "PublicSubmissionKey",
                unique: true,
                filter: "[PublicSubmissionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryJobs_EnquiryId_Kind_Recipient",
                table: "EmailDeliveryJobs",
                columns: new[] { "EnquiryId", "Kind", "Recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailDeliveryJobs_Status_NextAttemptAtUtc",
                table: "EmailDeliveryJobs",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailDeliveryJobs");

            migrationBuilder.DropIndex(
                name: "IX_Enquiries_PublicSubmissionKey",
                table: "Enquiries");

            migrationBuilder.DropColumn(
                name: "PublicSubmissionKey",
                table: "Enquiries");
        }
    }
}
