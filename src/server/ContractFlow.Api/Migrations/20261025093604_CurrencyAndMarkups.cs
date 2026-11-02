using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class CurrencyAndMarkups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MissingExchangeRate",
                table: "SubcontractorStatements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "StatementLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "StatementLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCurrency",
                table: "StatementLines",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MissingExchangeRate",
                table: "InvoiceSpecifications",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "InvoiceLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "InvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCurrency",
                table: "InvoiceLines",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpenseMarkups",
                table: "ContractTerms",
                type: "nvarchar(max)",
                nullable: true);

            // Existing terms carry no per-category mark-ups — an empty list,
            // not NULL, so the owned collection always materialises.
            migrationBuilder.Sql("UPDATE ContractTerms SET ExpenseMarkups = N'[]' WHERE ExpenseMarkups IS NULL;");

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    FromCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_Year_Month_FromCurrency_ToCurrency",
                table: "ExchangeRates",
                columns: new[] { "Year", "Month", "FromCurrency", "ToCurrency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropColumn(
                name: "MissingExchangeRate",
                table: "SubcontractorStatements");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "StatementLines");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "StatementLines");

            migrationBuilder.DropColumn(
                name: "OriginalCurrency",
                table: "StatementLines");

            migrationBuilder.DropColumn(
                name: "MissingExchangeRate",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "OriginalCurrency",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "ExpenseMarkups",
                table: "ContractTerms");
        }
    }
}
