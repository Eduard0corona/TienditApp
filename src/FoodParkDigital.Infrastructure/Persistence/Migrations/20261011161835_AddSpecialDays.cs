using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpecialDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FirstOpens = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: true),
                    FirstCloses = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: true),
                    SecondOpens = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: true),
                    SecondCloses = table.Column<TimeOnly>(type: "time(0)", precision: 0, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialDays", x => x.Id);
                    table.CheckConstraint("CK_SpecialDays_Kind", "[Kind] IN (0, 1)");
                    table.CheckConstraint("CK_SpecialDays_Openings", "([Kind] = 0 AND [FirstOpens] IS NULL AND [FirstCloses] IS NULL AND [SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([Kind] = 1 AND [FirstOpens] IS NOT NULL AND [FirstCloses] IS NOT NULL AND (([SecondOpens] IS NULL AND [SecondCloses] IS NULL) OR ([SecondOpens] IS NOT NULL AND [SecondCloses] IS NOT NULL)))");
                    table.CheckConstraint("CK_SpecialDays_Reason", "[Reason] IS NULL OR [Reason] <> ''");
                    table.CheckConstraint("CK_SpecialDays_Revision", "[Revision] >= 1");
                    table.CheckConstraint("CK_SpecialDays_SecondOpening", "[SecondOpens] IS NULL OR ([FirstCloses] > [FirstOpens] AND [SecondOpens] >= [FirstCloses] AND [SecondCloses] <> [SecondOpens])");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpecialDays_TenantId_Date",
                table: "SpecialDays",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpecialDays_TenantId_PeriodId",
                table: "SpecialDays",
                columns: new[] { "TenantId", "PeriodId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SpecialDays");
        }
    }
}
