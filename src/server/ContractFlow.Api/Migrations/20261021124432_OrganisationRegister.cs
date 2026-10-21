using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ContractFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrganisationRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_EndClients_EndClientId",
                table: "Contracts");

            // One organisation register: end clients move into Customers.
            // A name already in Customers merges (contracts repoint to that
            // row); any other end client becomes an organisation keeping its
            // id, so Contracts.EndClientId stays valid as-is.
            migrationBuilder.Sql("""
                UPDATE c SET c.EndClientId = m.Id
                FROM Contracts c
                JOIN EndClients e ON e.Id = c.EndClientId
                CROSS APPLY (SELECT TOP 1 cu.Id FROM Customers cu
                             WHERE cu.Name = e.Name ORDER BY cu.CreatedUtc) m;

                INSERT INTO Customers (Id, Name, AddressLine, PostalCode, City, Country,
                                       Notes, IsActive, CreatedUtc, Contacts)
                SELECT e.Id, e.Name, e.AddressLine, e.PostalCode, e.City, e.Country,
                       e.Notes, 1, SYSUTCDATETIME(), N'[]'
                FROM EndClients e
                WHERE NOT EXISTS (SELECT 1 FROM Customers cu WHERE cu.Name = e.Name);
                """);

            migrationBuilder.DropTable(
                name: "EndClients");

            migrationBuilder.AddColumn<string>(
                name: "CustomerName",
                table: "InvoiceSpecifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndClientName",
                table: "InvoiceSpecifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndClientReference",
                table: "InvoiceSpecifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndClientReference",
                table: "Contracts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // A contract whose end client is its own party is a direct contract.
            migrationBuilder.Sql(
                "UPDATE Contracts SET EndClientId = NULL, EndClientReference = NULL WHERE EndClientId = CustomerId;");

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Customers_EndClientId",
                table: "Contracts",
                column: "EndClientId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Customers_EndClientId",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "CustomerName",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "EndClientName",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "EndClientReference",
                table: "InvoiceSpecifications");

            migrationBuilder.DropColumn(
                name: "EndClientReference",
                table: "Contracts");

            migrationBuilder.CreateTable(
                name: "EndClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndClients", x => x.Id);
                });

            // Rolling back recreates end clients from the organisations in use
            // as one (same ids) so the old foreign key holds.
            migrationBuilder.Sql("""
                INSERT INTO EndClients (Id, Name, AddressLine, PostalCode, City, Country, Notes)
                SELECT cu.Id, cu.Name, cu.AddressLine, cu.PostalCode, cu.City, cu.Country, cu.Notes
                FROM Customers cu
                WHERE cu.Id IN (SELECT EndClientId FROM Contracts WHERE EndClientId IS NOT NULL);
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_EndClients_EndClientId",
                table: "Contracts",
                column: "EndClientId",
                principalTable: "EndClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
