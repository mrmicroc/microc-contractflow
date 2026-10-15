using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class TimeReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinBillableHoursPerDay",
                table: "ContractTerms",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinPayableHoursPerDay",
                table: "CompensationAgreements",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MobilisationPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ConfirmedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobilisationPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobilisationPeriods_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobilisationPeriods_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Timesheets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    IsoWeek = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timesheets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timesheets_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllowanceClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowanceClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllowanceClaims_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllowanceClaims_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllowanceClaims_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiptRef = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReimbursableToResource = table.Column<bool>(type: "bit", nullable: false),
                    RechargeableToCustomer = table.Column<bool>(type: "bit", nullable: false),
                    IncludedInRate = table.Column<bool>(type: "bit", nullable: false),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Expenses_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActualHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PayableHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    BillableHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    MinimumAdjustmentHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TravelRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    FromPlace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ToPlace = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DistanceKm = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    DurationHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    MobilisationPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TimesheetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TravelRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TravelRecords_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TravelRecords_MobilisationPeriods_MobilisationPeriodId",
                        column: x => x.MobilisationPeriodId,
                        principalTable: "MobilisationPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TravelRecords_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelRecords_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TravelRecords_Timesheets_TimesheetId",
                        column: x => x.TimesheetId,
                        principalTable: "Timesheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowanceClaims_ProjectId",
                table: "AllowanceClaims",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AllowanceClaims_ResourceId_Date",
                table: "AllowanceClaims",
                columns: new[] { "ResourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowanceClaims_TimesheetId",
                table: "AllowanceClaims",
                column: "TimesheetId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BookingId",
                table: "Expenses",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ProjectId",
                table: "Expenses",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ResourceId_Date",
                table: "Expenses",
                columns: new[] { "ResourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TimesheetId",
                table: "Expenses",
                column: "TimesheetId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilisationPeriods_ProjectId",
                table: "MobilisationPeriods",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MobilisationPeriods_ResourceId_StartDate",
                table: "MobilisationPeriods",
                columns: new[] { "ResourceId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_BookingId",
                table: "TimeEntries",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ProjectId_Date",
                table: "TimeEntries",
                columns: new[] { "ProjectId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_ResourceId_Date",
                table: "TimeEntries",
                columns: new[] { "ResourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TimesheetId",
                table: "TimeEntries",
                column: "TimesheetId");

            migrationBuilder.CreateIndex(
                name: "IX_Timesheets_ResourceId_Year_IsoWeek",
                table: "Timesheets",
                columns: new[] { "ResourceId", "Year", "IsoWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TravelRecords_BookingId",
                table: "TravelRecords",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRecords_MobilisationPeriodId",
                table: "TravelRecords",
                column: "MobilisationPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRecords_ProjectId",
                table: "TravelRecords",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TravelRecords_ResourceId_Date",
                table: "TravelRecords",
                columns: new[] { "ResourceId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TravelRecords_TimesheetId",
                table: "TravelRecords",
                column: "TimesheetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowanceClaims");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "TravelRecords");

            migrationBuilder.DropTable(
                name: "MobilisationPeriods");

            migrationBuilder.DropTable(
                name: "Timesheets");

            migrationBuilder.DropColumn(
                name: "MinBillableHoursPerDay",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "MinPayableHoursPerDay",
                table: "CompensationAgreements");
        }
    }
}
