using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssistantClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<string>(type: "varchar(43)", unicode: false, maxLength: 43, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastConnectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RedirectUris = table.Column<string>(type: "varchar(2577)", unicode: false, maxLength: 2577, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantClients", x => x.Id);
                    table.CheckConstraint("CK_AssistantClients_ClientId", "LEN([ClientId]) = 43");
                });

            migrationBuilder.CreateTable(
                name: "AssistantConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RedirectUri = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantConnections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantConnections_AssistantClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AssistantClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssistantAuthorizationCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RedirectUri = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    CodeChallenge = table.Column<string>(type: "varchar(43)", unicode: false, maxLength: 43, nullable: false),
                    Resource = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Scope = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantAuthorizationCodes", x => x.Id);
                    table.CheckConstraint("CK_AssistantAuthorizationCodes_CodeChallenge", "LEN([CodeChallenge]) = 43");
                    table.CheckConstraint("CK_AssistantAuthorizationCodes_CodeHash", "LEN([CodeHash]) = 64");
                    table.CheckConstraint("CK_AssistantAuthorizationCodes_Redeemed", "([RedeemedAt] IS NULL AND [ConnectionId] IS NULL) OR ([RedeemedAt] IS NOT NULL AND [ConnectionId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AssistantAuthorizationCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantAuthorizationCodes_AssistantClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AssistantClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssistantAuthorizationCodes_AssistantConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "AssistantConnections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AssistantTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RotatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantTokens", x => x.Id);
                    table.CheckConstraint("CK_AssistantTokens_Kind", "[Kind] IN (1, 2)");
                    table.CheckConstraint("CK_AssistantTokens_RotatedAt", "[RotatedAt] IS NULL OR [Kind] = 2");
                    table.CheckConstraint("CK_AssistantTokens_TokenHash", "LEN([TokenHash]) = 64");
                    table.ForeignKey(
                        name: "FK_AssistantTokens_AssistantConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "AssistantConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthorizationCodes_ClientId",
                table: "AssistantAuthorizationCodes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthorizationCodes_CodeHash",
                table: "AssistantAuthorizationCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthorizationCodes_ConnectionId",
                table: "AssistantAuthorizationCodes",
                column: "ConnectionId",
                filter: "[ConnectionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthorizationCodes_ExpiresAt",
                table: "AssistantAuthorizationCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantAuthorizationCodes_UserId",
                table: "AssistantAuthorizationCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantClients_ClientId",
                table: "AssistantClients",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantClients_LastConnectedAt",
                table: "AssistantClients",
                column: "LastConnectedAt",
                filter: "[LastConnectedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantClients_RegisteredAt",
                table: "AssistantClients",
                column: "RegisteredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConnections_ClientId",
                table: "AssistantConnections",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConnections_RevokedAt",
                table: "AssistantConnections",
                column: "RevokedAt",
                filter: "[RevokedAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConnections_UserId_ClientId",
                table: "AssistantConnections",
                columns: new[] { "UserId", "ClientId" },
                unique: true,
                filter: "[RevokedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTokens_ConnectionId",
                table: "AssistantTokens",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTokens_ExpiresAt",
                table: "AssistantTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantTokens_TokenHash",
                table: "AssistantTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fase 32E (ADR 0097): rolling back would disconnect, without a word, every assistant a customer connected to their
            // account. Once one is connected, it stops before anything changes; query 2 of verify.sql counts them. Clients no
            // account connected, codes and the tokens of disconnected ones may go. A missing table compiles (deferred name
            // resolution): a second run skips it.
            migrationBuilder.Sql(@"
EXEC(N'IF EXISTS (SELECT 1 FROM [AssistantConnections] WITH (TABLOCKX, HOLDLOCK) WHERE [RevokedAt] IS NULL)
    THROW 50065, N''Customers have assistants connected to their accounts; rolling back would disconnect them (query 2 of verify.sql). Roll forward instead.'', 1;');
");

            migrationBuilder.DropTable(
                name: "AssistantAuthorizationCodes");

            migrationBuilder.DropTable(
                name: "AssistantTokens");

            migrationBuilder.DropTable(
                name: "AssistantConnections");

            migrationBuilder.DropTable(
                name: "AssistantClients");
        }
    }
}
