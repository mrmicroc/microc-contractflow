using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReviewHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_BookingId",
                table: "PadTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceSpecifications_ProjectId_Year_Month",
                table: "InvoiceSpecifications");

            migrationBuilder.AddColumn<decimal>(
                name: "BillableMinimumAdjustmentHours",
                table: "TimeEntries",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceSpecificationId",
                table: "TimeEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "SupplierInvoices",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "SubcontractorStatements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningsJson",
                table: "SubcontractorStatements",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForcedReason",
                table: "ProjectMonthCloses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IssuedByUserId",
                table: "InvoiceSpecifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByUserId",
                table: "InvoiceSpecifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "InvoiceSpecifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WarningsJson",
                table: "InvoiceSpecifications",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                table: "AdditionalPadRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Changes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimesheetReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimesheetReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimesheetReviews_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_InvoiceSpecificationId",
                table: "TimeEntries",
                column: "InvoiceSpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_AdditionalPadRequestId_Approved",
                table: "PadTransactions",
                column: "AdditionalPadRequestId",
                unique: true,
                filter: "[Type] = 'AdditionalApproved' AND [AdditionalPadRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions",
                column: "BookingId",
                unique: true,
                filter: "[Type] = 'Commit' AND [BookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSpecifications_ProjectId_Year_Month_Revision",
                table: "InvoiceSpecifications",
                columns: new[] { "ProjectId", "Year", "Month", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdditionalPadRequests_BookingId",
                table: "AdditionalPadRequests",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredUtc",
                table: "AuditEntries",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TimesheetReviews_TimesheetId_AtUtc",
                table: "TimesheetReviews",
                columns: new[] { "TimesheetId", "AtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_AdditionalPadRequests_Bookings_BookingId",
                table: "AdditionalPadRequests",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_InvoiceSpecifications_InvoiceSpecificationId",
                table: "TimeEntries",
                column: "InvoiceSpecificationId",
                principalTable: "InvoiceSpecifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdditionalPadRequests_Bookings_BookingId",
                table: "AdditionalPadRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_InvoiceSpecifications_InvoiceSpecificationId",
                table: "TimeEntries");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "TimesheetReviews");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_InvoiceSpecificationId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_AdditionalPadRequestId_Approved",
                table: "PadTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceSpecifications_ProjectId_Year_Month_Revision",
                table: "InvoiceSpecifications");

            migrationBuilder.DropIndex(
                name: "IX_AdditionalPadRequests_BookingId",
                table: "AdditionalPadRequests");

            migrationBuilder.DropColumn(
                name: "BillableMinimumAdjustmentHours",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "InvoiceSpecificationId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "SupplierInvoices");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "SubcontractorStatements");

            migrationBuilder.DropColumn(
                name: "WarningsJson",
                table: "SubcontractorStatements");

            migrationBuilder.DropColumn(
                name: "ForcedReason",
                table: "ProjectMonthCloses");

            migrationBuilder.DropColumn(
                name: "IssuedByUserId",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "PaidByUserId",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "WarningsJson",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "AdditionalPadRequests");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_BookingId",
                table: "PadTransactions",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSpecifications_ProjectId_Year_Month",
                table: "InvoiceSpecifications",
                columns: new[] { "ProjectId", "Year", "Month" },
                unique: true);
        }
    }
}
