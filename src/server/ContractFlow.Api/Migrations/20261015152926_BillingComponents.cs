using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class BillingComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "Contracts");

            migrationBuilder.AddColumn<decimal>(
                name: "DayRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedPriceAmount",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FixedPriceDate",
                table: "ContractTerms",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FixedPriceEnabled",
                table: "ContractTerms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FixedPriceNote",
                table: "ContractTerms",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HalfDayMaxHours",
                table: "ContractTerms",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HalfDayRate",
                table: "ContractTerms",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PadEnabled",
                table: "ContractTerms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimeBilling",
                table: "ContractTerms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Backfill: every existing version billed per hour, and versions
            // carrying a PAD rate keep the PAD component on (matches the old
            // Contract.Model=padBased semantics). PadRate IS NOT NULL is the
            // same signal DevSeed and tests used to mark PAD contracts.
            migrationBuilder.Sql("""
                UPDATE ContractTerms SET TimeBilling = 'hourly';
                UPDATE ContractTerms SET PadEnabled = 1 WHERE PadRate IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayRate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "FixedPriceAmount",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "FixedPriceDate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "FixedPriceEnabled",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "FixedPriceNote",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "HalfDayMaxHours",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "HalfDayRate",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "PadEnabled",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "TimeBilling",
                table: "ContractTerms");

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Contracts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
