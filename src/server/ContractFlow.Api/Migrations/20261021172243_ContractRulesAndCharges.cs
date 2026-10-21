using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ContractRulesAndCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions");

            // The fixed-price terms fields move to ScheduledCharges further down
            // (after that table exists) — EF's rename guess onto the new PAD
            // fields would have turned the fixed amount into a PAD rate.
            migrationBuilder.AddColumn<bool>(
                name: "UnbookedTimeConsumesPad",
                table: "ContractTerms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalPadRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CountsAsVisit",
                table: "WorkPackages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PricingMode",
                table: "WorkPackages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Contract");

            migrationBuilder.AddColumn<decimal>(
                name: "ContractedBaselinePad",
                table: "Projects",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresApproval",
                table: "PadTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TimeEntryId",
                table: "PadTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarryOverBasis",
                table: "ContractTerms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "UnusedBaseline");

            migrationBuilder.AddColumn<decimal>(
                name: "CarryOverRoundUpTo",
                table: "ContractTerms",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DefaultStartTime",
                table: "ContractTerms",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));

            migrationBuilder.AddColumn<int>(
                name: "IncludedVisits",
                table: "ContractTerms",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PadInvoicing",
                table: "ContractTerms",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Utilised");

            migrationBuilder.AddColumn<string>(
                name: "SharedDayMinimum",
                table: "ContractTerms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Proportional");

            migrationBuilder.AddColumn<int>(
                name: "StartTimeNoticeBusinessDays",
                table: "ContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "UnbookedTimeIncrementMinutes",
                table: "ContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<string>(
                name: "Representatives",
                table: "Contracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationChargeDayFraction",
                table: "Bookings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancellationChargeFull",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstructedAtUtc",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructedBy",
                table: "Bookings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "Bookings",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByRepresentative",
                table: "AdditionalPadRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookingProjectShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedHours = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingProjectShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingProjectShares_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingProjectShares_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingStartTimeChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    NotifiedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingStartTimeChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingStartTimeChanges_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledCharges_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledCharges_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Former one-off "fixed price" terms → contract-level scheduled
            // charges (one per distinct amount + date across the versions).
            // Already-billed fixed-price lines are re-pointed at their charge
            // so the new once-only rule never bills them a second time.
            migrationBuilder.Sql("""
                INSERT INTO ScheduledCharges (Id, ContractId, ProjectId, Date, Amount, Description, CreatedUtc)
                SELECT NEWID(), d.ContractId, NULL, d.FixedPriceDate, d.FixedPriceAmount,
                       LEFT(COALESCE(NULLIF(d.Note, ''), 'Fixed price'), 300), SYSUTCDATETIME()
                FROM (SELECT ContractId, FixedPriceDate, FixedPriceAmount, MAX(FixedPriceNote) AS Note
                      FROM ContractTerms
                      WHERE FixedPriceEnabled = 1 AND FixedPriceAmount > 0 AND FixedPriceDate IS NOT NULL
                      GROUP BY ContractId, FixedPriceDate, FixedPriceAmount) d;

                UPDATE l SET l.SourceId = sc.Id, l.Kind = 'ScheduledCharge'
                FROM InvoiceLines l
                JOIN InvoiceSpecifications s ON s.Id = l.InvoiceSpecificationId
                JOIN Projects p ON p.Id = s.ProjectId
                JOIN ScheduledCharges sc ON sc.ContractId = p.ContractId
                     AND sc.Date = l.Date AND sc.Amount = l.Amount
                WHERE l.Kind = 'FixedPrice';
                """);

            migrationBuilder.DropColumn(name: "FixedPriceEnabled", table: "ContractTerms");
            migrationBuilder.DropColumn(name: "FixedPriceAmount", table: "ContractTerms");
            migrationBuilder.DropColumn(name: "FixedPriceDate", table: "ContractTerms");
            migrationBuilder.DropColumn(name: "FixedPriceNote", table: "ContractTerms");

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions",
                columns: new[] { "BookingId", "ProjectId" },
                unique: true,
                filter: "[Type] = 'Commit' AND [BookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_TimeEntryId",
                table: "PadTransactions",
                column: "TimeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingProjectShares_BookingId_ProjectId",
                table: "BookingProjectShares",
                columns: new[] { "BookingId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingProjectShares_ProjectId",
                table: "BookingProjectShares",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingStartTimeChanges_BookingId_ChangedUtc",
                table: "BookingStartTimeChanges",
                columns: new[] { "BookingId", "ChangedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledCharges_ContractId_Date",
                table: "ScheduledCharges",
                columns: new[] { "ContractId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledCharges_ProjectId",
                table: "ScheduledCharges",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_PadTransactions_TimeEntries_TimeEntryId",
                table: "PadTransactions",
                column: "TimeEntryId",
                principalTable: "TimeEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PadTransactions_TimeEntries_TimeEntryId",
                table: "PadTransactions");

            migrationBuilder.DropTable(
                name: "BookingProjectShares");

            migrationBuilder.DropTable(
                name: "BookingStartTimeChanges");

            migrationBuilder.DropTable(
                name: "ScheduledCharges");

            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PadTransactions_TimeEntryId",
                table: "PadTransactions");

            migrationBuilder.DropColumn(
                name: "CountsAsVisit",
                table: "WorkPackages");

            migrationBuilder.DropColumn(
                name: "PricingMode",
                table: "WorkPackages");

            migrationBuilder.DropColumn(
                name: "ContractedBaselinePad",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RequiresApproval",
                table: "PadTransactions");

            migrationBuilder.DropColumn(
                name: "TimeEntryId",
                table: "PadTransactions");

            migrationBuilder.DropColumn(
                name: "CarryOverBasis",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "CarryOverRoundUpTo",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "DefaultStartTime",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "IncludedVisits",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "PadInvoicing",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "SharedDayMinimum",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "StartTimeNoticeBusinessDays",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "UnbookedTimeIncrementMinutes",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "Representatives",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "CancellationChargeDayFraction",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationChargeFull",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "InstructedAtUtc",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "InstructedBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ApprovedByRepresentative",
                table: "AdditionalPadRequests");

            migrationBuilder.DropColumn(name: "UnbookedTimeConsumesPad", table: "ContractTerms");
            migrationBuilder.DropColumn(name: "AdditionalPadRate", table: "ContractTerms");

            migrationBuilder.AddColumn<bool>(
                name: "FixedPriceEnabled",
                table: "ContractTerms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedPriceAmount",
                table: "ContractTerms",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FixedPriceDate",
                table: "ContractTerms",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FixedPriceNote",
                table: "ContractTerms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PadTransactions_BookingId_Commit",
                table: "PadTransactions",
                column: "BookingId",
                unique: true,
                filter: "[Type] = 'Commit' AND [BookingId] IS NOT NULL");
        }
    }
}
