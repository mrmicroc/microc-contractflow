using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class CarryOverPolicySettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CarryOverRoundUpTo",
                table: "ContractTerms",
                newName: "CarryOverRoundingStep");

            migrationBuilder.AddColumn<string>(
                name: "CarryOverConsumption",
                table: "ContractTerms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CarryOverFirst");

            migrationBuilder.AddColumn<decimal>(
                name: "CarryOverMaxPad",
                table: "ContractTerms",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarryOverRounding",
                table: "ContractTerms",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Up");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarryOverConsumption",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "CarryOverMaxPad",
                table: "ContractTerms");

            migrationBuilder.DropColumn(
                name: "CarryOverRounding",
                table: "ContractTerms");

            migrationBuilder.RenameColumn(
                name: "CarryOverRoundingStep",
                table: "ContractTerms",
                newName: "CarryOverRoundUpTo");
        }
    }
}
