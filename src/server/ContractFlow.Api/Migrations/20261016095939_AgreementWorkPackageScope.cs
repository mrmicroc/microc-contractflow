using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AgreementWorkPackageScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_Version",
                table: "CompensationAgreements");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkPackageId",
                table: "CompensationAgreements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_Version",
                table: "CompensationAgreements",
                columns: new[] { "ResourceId", "ContractId", "Version" },
                unique: true,
                filter: "[ContractId] IS NOT NULL AND [WorkPackageId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_WorkPackageId_Version",
                table: "CompensationAgreements",
                columns: new[] { "ResourceId", "ContractId", "WorkPackageId", "Version" },
                unique: true,
                filter: "[WorkPackageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationAgreements_WorkPackageId",
                table: "CompensationAgreements",
                column: "WorkPackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompensationAgreements_WorkPackages_WorkPackageId",
                table: "CompensationAgreements",
                column: "WorkPackageId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompensationAgreements_WorkPackages_WorkPackageId",
                table: "CompensationAgreements");

            migrationBuilder.DropIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_Version",
                table: "CompensationAgreements");

            migrationBuilder.DropIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_WorkPackageId_Version",
                table: "CompensationAgreements");

            migrationBuilder.DropIndex(
                name: "IX_CompensationAgreements_WorkPackageId",
                table: "CompensationAgreements");

            migrationBuilder.DropColumn(
                name: "WorkPackageId",
                table: "CompensationAgreements");

            migrationBuilder.CreateIndex(
                name: "IX_CompensationAgreements_ResourceId_ContractId_Version",
                table: "CompensationAgreements",
                columns: new[] { "ResourceId", "ContractId", "Version" },
                unique: true,
                filter: "[ContractId] IS NOT NULL");
        }
    }
}
