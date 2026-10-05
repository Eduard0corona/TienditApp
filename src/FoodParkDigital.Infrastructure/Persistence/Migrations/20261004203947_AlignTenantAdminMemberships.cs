using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FoodParkDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignTenantAdminMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Until now an account with the TENANT_ADMIN role administered every business it belonged to, whatever its
            // membership said. From now on the membership decides. An admin account whose only membership says staff
            // (data from before both were kept in sync) becomes admin of that business, so nobody loses the access
            // they have today. Accounts in several businesses keep their memberships as they are.
            // 1 = UserRole.TenantAdmin, 2 = TenantUserRole.Staff, 1 = TenantUserRole.Admin.
            migrationBuilder.Sql(@"
UPDATE tu SET [Role] = 1
FROM [TenantUsers] tu
INNER JOIN [AspNetUsers] u ON u.[Id] = tu.[UserId]
WHERE u.[Role] = 1
  AND tu.[Role] = 2
  AND NOT EXISTS (SELECT 1 FROM [TenantUsers] other WHERE other.[UserId] = tu.[UserId] AND other.[Id] <> tu.[Id]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: with the previous code those accounts administered the business anyway, through their
            // account role, so the aligned memberships keep the same access.
        }
    }
}
