using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using MediatR;

namespace FoodParkDigital.Application.Identity.Queries;

/// <summary>
/// What the signed-in user can do in the current business, resolved by the server (the same rules the
/// authorization behavior applies). The client reads it instead of trusting the claims of an older token, so a
/// permission change applies without signing in again.
/// </summary>
[RequiresAuthentication]
public record GetMyAccessQuery : IRequest<MyAccessDto>;

public class MyAccessDto
{
    public string? UserId { get; set; }
    public string? Role { get; set; }
    public string? TenantRole { get; set; }
    public Guid? TenantId { get; set; }
    public bool IsMember { get; set; }
    public bool IsTenantManager { get; set; }
    public bool IsPlatformAdmin { get; set; }
    public List<string> Permissions { get; set; } = [];
    /// <summary>Modules of the business (TenantCapability flags).</summary>
    public int Capabilities { get; set; }
}

public class GetMyAccessQueryHandler : IRequestHandler<GetMyAccessQuery, MyAccessDto>
{
    private readonly CurrentAccessContext _access;
    private readonly ICurrentUserService _user;

    public GetMyAccessQueryHandler(CurrentAccessContext access, ICurrentUserService user)
    {
        _access = access;
        _user = user;
    }

    public async Task<MyAccessDto> Handle(GetMyAccessQuery request, CancellationToken cancellationToken)
    {
        var dto = new MyAccessDto
        {
            UserId = _user.UserId,
            Role = _user.Role,
            TenantRole = _user.TenantRole,
            IsPlatformAdmin = _access.IsPlatformAdmin
        };
        if (_access.TenantId == Guid.Empty)
            return dto;

        var snapshot = await _access.GetSnapshotAsync(cancellationToken);
        dto.TenantId = _access.TenantId;
        dto.IsMember = snapshot?.HasActiveAccess == true;
        dto.IsTenantManager = (dto.IsMember || dto.IsPlatformAdmin) && _access.IsTenantManager;
        dto.Permissions = dto.IsMember || dto.IsPlatformAdmin
            ? (await _access.GetPermissionsAsync(cancellationToken)).OrderBy(p => p, StringComparer.Ordinal).ToList()
            : [];
        dto.Capabilities = (int)await _access.GetCapabilitiesAsync(cancellationToken);
        return dto;
    }
}
