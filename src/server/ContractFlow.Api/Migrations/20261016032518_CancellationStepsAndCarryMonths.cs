using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class CancellationStepsAndCarryMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginMonth",
                table: "PadTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginYear",
                table: "PadTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationSteps",
                table: "ContractTerms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CarryOverMonths",
                table: "ContractTerms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "HolidayDayRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SaturdayDayRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SundayDayRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationChargeAmount",
                table: "Bookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CancellationChargeHours",
                table: "Bookings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginMonth",
                table: "PadTransactions");

            migrationBuilder.DropColumn(
                name: "OriginYear",
                table: "PadTransactions");

            migrationBuilder.DropColumn(
                name: "CancellationSteps",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "CarryOverMonths",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "HolidayDayRate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "SaturdayDayRate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "SundayDayRate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "CancellationChargeAmount",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancellationChargeHours",
                table: "Bookings");
        }
    }
}
