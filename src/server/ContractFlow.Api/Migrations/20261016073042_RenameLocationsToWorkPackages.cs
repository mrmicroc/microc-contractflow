using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameLocationsToWorkPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_MobilisationPeriods_Locations_LocationId",
                table: "MobilisationPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_LocationRates_Locations_LocationId",
                table: "LocationRates");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Projects_ProjectId",
                table: "Locations");

            migrationBuilder.RenameTable(
                name: "LocationRates",
                newName: "WorkPackageRates");

            migrationBuilder.RenameTable(
                name: "Locations",
                newName: "WorkPackages");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "WorkPackageRates",
                newName: "WorkPackageId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "MobilisationPeriods",
                newName: "WorkPackageId");

            migrationBuilder.RenameColumn(
                name: "LocationId",
                table: "Bookings",
                newName: "WorkPackageId");

            migrationBuilder.RenameIndex(
                name: "IX_LocationRates_LocationId_EffectiveFrom",
                table: "WorkPackageRates",
                newName: "IX_WorkPackageRates_WorkPackageId_EffectiveFrom");

            migrationBuilder.RenameIndex(
                name: "IX_Locations_ProjectId",
                table: "WorkPackages",
                newName: "IX_WorkPackages_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_MobilisationPeriods_LocationId",
                table: "MobilisationPeriods",
                newName: "IX_MobilisationPeriods_WorkPackageId");

            migrationBuilder.RenameIndex(
                name: "IX_Bookings_LocationId",
                table: "Bookings",
                newName: "IX_Bookings_WorkPackageId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkPackages_Projects_ProjectId",
                table: "WorkPackages",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkPackageRates_WorkPackages_WorkPackageId",
                table: "WorkPackageRates",
                column: "WorkPackageId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_WorkPackages_WorkPackageId",
                table: "Bookings",
                column: "WorkPackageId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MobilisationPeriods_WorkPackages_WorkPackageId",
                table: "MobilisationPeriods",
                column: "WorkPackageId",
                principalTable: "WorkPackages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_WorkPackages_WorkPackageId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_MobilisationPeriods_WorkPackages_WorkPackageId",
                table: "MobilisationPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkPackageRates_WorkPackages_WorkPackageId",
                table: "WorkPackageRates");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkPackages_Projects_ProjectId",
                table: "WorkPackages");

            migrationBuilder.RenameColumn(
                name: "WorkPackageId",
                table: "WorkPackageRates",
                newName: "LocationId");

            migrationBuilder.RenameColumn(
                name: "WorkPackageId",
                table: "MobilisationPeriods",
                newName: "LocationId");

            migrationBuilder.RenameColumn(
                name: "WorkPackageId",
                table: "Bookings",
                newName: "LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_WorkPackageRates_WorkPackageId_EffectiveFrom",
                table: "WorkPackageRates",
                newName: "IX_LocationRates_LocationId_EffectiveFrom");

            migrationBuilder.RenameIndex(
                name: "IX_WorkPackages_ProjectId",
                table: "WorkPackages",
                newName: "IX_Locations_ProjectId");

            migrationBuilder.RenameIndex(
                name: "IX_MobilisationPeriods_WorkPackageId",
                table: "MobilisationPeriods",
                newName: "IX_MobilisationPeriods_LocationId");

            migrationBuilder.RenameIndex(
                name: "IX_Bookings_WorkPackageId",
                table: "Bookings",
                newName: "IX_Bookings_LocationId");

            migrationBuilder.RenameTable(
                name: "WorkPackageRates",
                newName: "LocationRates");

            migrationBuilder.RenameTable(
                name: "WorkPackages",
                newName: "Locations");

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Projects_ProjectId",
                table: "Locations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LocationRates_Locations_LocationId",
                table: "LocationRates",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MobilisationPeriods_Locations_LocationId",
                table: "MobilisationPeriods",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
