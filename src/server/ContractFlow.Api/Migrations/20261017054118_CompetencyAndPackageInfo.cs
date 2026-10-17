using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class CompetencyAndPackageInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResourceCompetencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReminderDays = table.Column<int>(type: "int", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCompetencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceCompetencies_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkPackageInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkPackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkPackageInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkPackageInfos_WorkPackages_WorkPackageId",
                        column: x => x.WorkPackageId,
                        principalTable: "WorkPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCompetencies_ResourceId",
                table: "ResourceCompetencies",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkPackageInfos_WorkPackageId",
                table: "WorkPackageInfos",
                column: "WorkPackageId");

            // Carry the old free-text fields into their new homes before the
            // columns die: the whole competency text becomes one record, and
            // the address becomes an "Address" info field.
            migrationBuilder.Sql(
                """
                INSERT INTO ResourceCompetencies (Id, ResourceId, Label, CreatedUtc)
                SELECT NEWID(), Id, Competencies, SYSUTCDATETIME()
                FROM Resources
                WHERE Competencies IS NOT NULL AND LTRIM(RTRIM(Competencies)) <> '';
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO WorkPackageInfos (Id, WorkPackageId, Type, Description)
                SELECT NEWID(), Id, N'Address', Address
                FROM WorkPackages
                WHERE Address IS NOT NULL AND LTRIM(RTRIM(Address)) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Address",
                table: "WorkPackages");

            migrationBuilder.DropColumn(
                name: "Competencies",
                table: "Resources");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceCompetencies");

            migrationBuilder.DropTable(
                name: "WorkPackageInfos");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "WorkPackages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Competencies",
                table: "Resources",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
