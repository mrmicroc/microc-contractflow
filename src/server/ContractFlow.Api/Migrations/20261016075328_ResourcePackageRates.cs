using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class ResourcePackageRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkPackageRates_WorkPackageId_EffectiveFrom",
                table: "WorkPackageRates");

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "WorkPackageRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "WorkPackageRates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkPackageId",
                table: "TimeEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProjectResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectResources_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectResources_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageRates_ResourceId",
                table: "WorkPackageRates",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageRates_WorkPackageId_ResourceId_EffectiveFrom",
                table: "WorkPackageRates",
                columns: new[] { "WorkPackageId", "ResourceId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_WorkPackageId",
                table: "TimeEntries",
                column: "WorkPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResources_ProjectId_ResourceId",
                table: "ProjectResources",
                columns: new[] { "ProjectId", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResources_ResourceId",
                table: "ProjectResources",
                column: "ResourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_TimeEntries_WorkPackages_WorkPackageId",
                table: "TimeEntries",
                column: "WorkPackageId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkPackageRates_Resources_ResourceId",
                table: "WorkPackageRates",
                column: "ResourceId",
                principalTable: "Resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TimeEntries_WorkPackages_WorkPackageId",
                table: "TimeEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkPackageRates_Resources_ResourceId",
                table: "WorkPackageRates");

            migrationBuilder.DropTable(
                name: "ProjectResources");

            migrationBuilder.DropIndex(
                name: "IX_WorkPackageRates_ResourceId",
                table: "WorkPackageRates");

            migrationBuilder.DropIndex(
                name: "IX_WorkPackageRates_WorkPackageId_ResourceId_EffectiveFrom",
                table: "WorkPackageRates");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_WorkPackageId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "WorkPackageRates");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "WorkPackageRates");

            migrationBuilder.DropColumn(
                name: "WorkPackageId",
                table: "TimeEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageRates_WorkPackageId_EffectiveFrom",
                table: "WorkPackageRates",
                columns: new[] { "WorkPackageId", "EffectiveFrom" });
        }
    }
}
