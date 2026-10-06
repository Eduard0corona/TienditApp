using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Refresh tokens are found only by their hash. The plain values older versions stored (tokens issued before the
    /// hashing and not used since) get here the same SHA-256 as RefreshToken.HashOf, a replaced one still counts as
    /// rotated, and every value is emptied. The model no longer maps Token and ReplacedByToken, but the columns and
    /// their index stay: the version running while this is applied, and the one to go back to if this version is
    /// reverted (redeployed, or swapped back from the staging slot), still read them. A later migration drops them, once
    /// no deployable version maps them.
    /// </summary>
    public partial class StopUsingPlainRefreshTokenValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Inside EXEC: the idempotent script compiles every batch, also the ones it skips, so this one must still
            // compile once a later migration drops these columns. The values are Base64 (ASCII): as varchar they are
            // the same bytes as their UTF-8.
            migrationBuilder.Sql("""
                EXEC(N'
                UPDATE [RefreshTokens]
                SET [TokenHash] = CONVERT(varchar(64), HASHBYTES(''SHA2_256'', CONVERT(varchar(500), [Token])), 2)
                WHERE [TokenHash] IS NULL AND [Token] <> N'''';

                UPDATE [RefreshTokens]
                SET [RevocationReason] = 1
                WHERE [RevokedAt] IS NOT NULL AND [RevocationReason] IS NULL AND [ReplacedByToken] IS NOT NULL;

                UPDATE [RefreshTokens]
                SET [Token] = N'''', [ReplacedByToken] = NULL
                WHERE [Token] <> N'''' OR [ReplacedByToken] IS NOT NULL;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: the previous version works with hashed tokens, and plain values cannot (and need not)
            // come back.
        }
    }
}
