using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessFieldsAndCustomerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address_City",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Country",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_ExteriorNumber",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_InteriorNumber",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Neighborhood",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_State",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_Street",
                table: "Tenants",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_ZipCode",
                table: "Tenants",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Tenants",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rfc",
                table: "Tenants",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegime",
                table: "Tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address_City",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_Country",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_ExteriorNumber",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_InteriorNumber",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_Neighborhood",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_State",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_Street",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Address_ZipCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Industry",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Rfc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TaxRegime",
                table: "Tenants");
        }
    }
}
