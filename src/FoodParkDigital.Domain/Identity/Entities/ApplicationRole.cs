using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Domain.Identity.Entities;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName)
    {
        Id = Guid.NewGuid();
    }
}
