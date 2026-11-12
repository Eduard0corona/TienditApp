using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Employees.DTOs;

public class EmployeeDto
{
    public Guid TenantUserId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string? PhoneNumber { get; set; }
    public string AppRole { get; set; } = null!;
    public string TenantRole { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<string> Permissions { get; set; } = [];
    /// <summary>Only right after inviting someone new: the link to set their password, to share it by WhatsApp.</summary>
    public string? InvitationUrl { get; set; }
    /// <summary>The notices by email this person gets in the business (Fase 29Q, ADR 0071).</summary>
    public EmployeeNoticeEmailsDto NoticeEmails { get; set; } = new();
}

/// <summary>
/// What a person gets by email from the business: what they or whoever runs the team chose or, until someone chooses, whether
/// they own it. Each arrives only while they may act on it and their email is confirmed.
/// </summary>
public class EmployeeNoticeEmailsDto
{
    /// <summary>One with each request online and with each appointment its customer cancels.</summary>
    public bool Requests { get; set; }
    /// <summary>One each time a customer accepts or rejects a quote from its link.</summary>
    public bool QuoteReplies { get; set; }
    /// <summary>
    /// One each time a customer cancels an online order before it was accepted (Fase 30B) or asks to cancel one (Fase 31B), and
    /// the reminder of the one still waiting to be accepted 15 minutes after it arrived (Fase 31K).
    /// </summary>
    public bool OrderCancellations { get; set; }
    /// <summary>One each time a customer writes a new review of the business (Fase 31K, ADR 0091).</summary>
    public bool Reviews { get; set; }
    /// <summary>Whether the email of their account is confirmed: until it is, none reaches them.</summary>
    public bool EmailConfirmed { get; set; }

    public static EmployeeNoticeEmailsDto From(TenantUser member, bool emailConfirmed) => new()
    {
        Requests = member.GetsRequestEmails,
        QuoteReplies = member.GetsQuoteReplyEmails,
        OrderCancellations = member.GetsOrderCancellationEmails,
        Reviews = member.GetsReviewEmails,
        EmailConfirmed = emailConfirmed
    };
}

/// <summary>The link where an invited person chooses their password, relative to the app.</summary>
public class EmployeeInvitationDto
{
    public string InvitationUrl { get; set; } = null!;
    public string Email { get; set; } = null!;
}

public class EmployeePermissionOptionDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
}

/// <summary>A named role (Gerente, Cajero, Mesero...) that fills the account role and the permissions.</summary>
public class EmployeeRolePresetDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public UserRole AppRole { get; set; }
    public TenantUserRole TenantRole { get; set; }
    public List<string> Permissions { get; set; } = [];
}
