using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentProblemPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentProblemPhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    FileKey = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentProblemPhotos", x => x.Id);
                    table.CheckConstraint("CK_AppointmentProblemPhotos_Position", "[Position] >= 0 AND [Position] < 5");
                    table.ForeignKey(
                        name: "FK_AppointmentProblemPhotos_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProblemPhotos_AppointmentId_Position",
                table: "AppointmentProblemPhotos",
                columns: new[] { "AppointmentId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentProblemPhotos_TenantId_FileKey",
                table: "AppointmentProblemPhotos",
                columns: new[] { "TenantId", "FileKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The photos customers sent with their visits would be lost: the previous version has nowhere to keep them (Fase 29H).
            // The lock holds until the table is dropped, in the same transaction: no photo saved in between is lost.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AppointmentProblemPhotos] WITH (TABLOCKX, HOLDLOCK))
    THROW 50056, N''Customers already sent photos of what happens with their visits; rolling back would lose them. Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "AppointmentProblemPhotos");
        }
    }
}
