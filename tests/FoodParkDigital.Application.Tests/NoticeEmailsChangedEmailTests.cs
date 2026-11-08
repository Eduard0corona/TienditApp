using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Domain.Tenants.Events;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The email that tells a person of the team that someone else changed their notices by email, and who (Fase 30A, ADR 0081).
/// </summary>
public class NoticeEmailsChangedEmailTests
{
    [Fact]
    public async Task ThePerson_ShouldReadWhoChangedTheirNotices_InWhichBusiness_AndWhatTheyGetNow()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "Estética <Luna>");
        var owner = await MemberAsync(fixture, salon, "Mariana", TenantUserRole.Owner);
        var receptionist = await MemberAsync(fixture, salon, "Rosa", TenantUserRole.Staff);

        var sent = await SendAsync(fixture, salon, receptionist, owner, requestEmails: true, quoteReplyEmails: false);

        var (to, subject, body) = Assert.Single(sent);
        Assert.Equal($"Rosa.{salon.Slug}@demo.com", to);
        Assert.Equal("Mariana Equipo cambió tus avisos por correo en Estética <Luna>", subject);
        // What people typed goes encoded in the body (WebUtility.HtmlEncode, as every notice).
        Assert.Contains("<strong>Mariana Equipo</strong> cambió los avisos que te llegan de Est&#233;tica &lt;Luna&gt;:", body);
        Assert.Contains("<li>Ahora te llegan las solicitudes en línea de la Agenda, su recordatorio antes del límite y las citas que sus clientes cancelan.</li>", body);
        Assert.Contains("<li>Ya no te llegan las respuestas de los clientes a las cotizaciones.</li>", body);
        Assert.Contains($"<a href='{AppUrl}/profile'>Mi perfil</a>", body);
        Assert.DoesNotContain("Estética <Luna>", body);
    }

    [Fact]
    public async Task OnlyWhatChanged_ShouldBeSaid_AndSomeoneWhoLeft_ShouldBeSomeoneOfTheTeam()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "Estética Luna");
        var receptionist = await MemberAsync(fixture, salon, "Rosa", TenantUserRole.Staff);

        var (_, subject, body) = Assert.Single(await SendAsync(fixture, salon, receptionist, Guid.NewGuid(), quoteReplyEmails: true));

        Assert.Equal("Alguien de tu equipo cambió tus avisos por correo en Estética Luna", subject);
        Assert.Contains("<li>Ahora te llegan las respuestas de los clientes a las cotizaciones.</li>", body);
        Assert.DoesNotContain("Agenda", body);
    }

    [Fact]
    public async Task NoEmail_ShouldGo_ToSomeoneNoLongerInTheTeam_OrWithoutAConfirmedEmail_OrOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "Estética Luna");
        var other = await SalonAsync(fixture, "Barbería Don Pepe");
        var owner = await MemberAsync(fixture, salon, "Mariana", TenantUserRole.Owner);
        var disabled = await MemberAsync(fixture, salon, "Rosa", TenantUserRole.Staff, status: TenantUserStatus.Disabled);
        var unconfirmed = await MemberAsync(fixture, salon, "Lupita", TenantUserRole.Staff, emailConfirmed: false);
        var elsewhere = await MemberAsync(fixture, other, "Pepe", TenantUserRole.Staff);

        Assert.Empty(await SendAsync(fixture, salon, disabled, owner, requestEmails: true));
        Assert.Empty(await SendAsync(fixture, salon, unconfirmed, owner, requestEmails: true));
        // A membership of another business, in a message of this one.
        Assert.Empty(await SendAsync(fixture, salon, elsewhere, owner, requestEmails: true, membershipOf: other));
    }

    private static async Task<Tenant> SalonAsync(SalesTestFixture fixture, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments);
        tenant.Update(name, $"{tenant.Slug}@demo.com", null, null);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Guid memberUserId, Guid changedBy, bool? requestEmails = null,
        bool? quoteReplyEmails = null, Tenant? membershipOf = null)
    {
        var membership = await fixture.Context.TenantUsers.IgnoreQueryFilters()
            .SingleAsync(m => m.UserId == memberUserId && m.TenantId == (membershipOf ?? tenant).Id);
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails();
        await new NoticeEmailsChangedEmailHandler(fixture.Context, emails, new Links(AppUrl))
            .HandleAsync(
                new TenantUserNoticeEmailsChangedEvent(membership.Id, changedBy, requestEmails, quoteReplyEmails),
                Context(tenant),
                CancellationToken.None);
        return emails.Sent;
    }
}
