using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers;

/// <summary>
/// A customer's own term (Fase 26C, ADR 0035): how many days this customer has to pay what they owe, instead of the
/// business's; null gives them the business's again. The owner's decision, recorded in the activity log. Like the business's
/// term, it only tells what is overdue.
/// </summary>
[RequiresTenantManager]
public record SetCustomerCreditTermCommand : IRequest<Result<CustomerDto>>
{
    public Guid CustomerId { get; init; }
    public int? CreditTermDays { get; init; }
}

public class SetCustomerCreditTermCommandValidator : AbstractValidator<SetCustomerCreditTermCommand>
{
    public SetCustomerCreditTermCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.CreditTermDays!.Value)
            .InclusiveBetween(TenantSettings.MinCreditTermDays, TenantSettings.MaxCreditTermDays)
            .When(x => x.CreditTermDays.HasValue)
            .WithName(nameof(SetCustomerCreditTermCommand.CreditTermDays))
            .WithMessage(Customer.CreditTermRangeMessage);
    }
}

public class SetCustomerCreditTermCommandHandler : IRequestHandler<SetCustomerCreditTermCommand, Result<CustomerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;

    public SetCustomerCreditTermCommandHandler(IApplicationDbContext context, ICurrentTenantService tenant, ICurrentUserService currentUser)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
    }

    public async Task<Result<CustomerDto>> Handle(SetCustomerCreditTermCommand request, CancellationToken cancellationToken)
    {
        var customer = await _context.Customers
            .Include(c => c.Addresses)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new EntityNotFoundException("Customer", request.CustomerId);

        var previous = customer.CreditTermDays;
        if (previous == request.CreditTermDays)
            return Result.Success(CustomerDto.From(customer));

        try
        {
            customer.SetCreditTermDays(request.CreditTermDays);
        }
        catch (DomainException refused)
        {
            return Result.Failure<CustomerDto>(refused.Message);
        }

        // How long a customer has to pay decides what of theirs is overdue and reminded: worth recording.
        _context.AuditLogs.Add(new AuditLog(
            tenantId: _tenant.TenantId,
            entityName: nameof(Customer),
            entityId: customer.Id.ToString(),
            action: "CustomerCreditTermUpdated",
            performedBy: _currentUser.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new { Previous = previous, New = customer.CreditTermDays })));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(CustomerDto.From(customer));
    }
}

/// <summary>The business's reminders of what is overdue (Fase 26C), as Por cobrar reads them.</summary>
public static class CreditReminders
{
    /// <summary>Every how many days the business reminds what is overdue; null while the reminders are off.</summary>
    public static Task<int?> EveryDaysAsync(IApplicationDbContext context, CancellationToken cancellationToken)
        => context.TenantSettings.AsNoTracking()
            .Select(s => s.CreditRemindersEnabled ? (int?)s.CreditReminderEveryDays : null)
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary>The reminders of what is overdue (Fase 26C) and what the owner needs to set them up.</summary>
public class CreditRemindersDto
{
    public bool Enabled { get; set; }
    public int EveryDays { get; set; }
    /// <summary>The template; null uses <see cref="DefaultBody"/>.</summary>
    public Guid? TemplateId { get; set; }
    /// <summary>The business connected its WhatsApp: without it the reminders cannot go out.</summary>
    public bool WhatsAppConnected { get; set; }
    /// <summary>The business's term (Fase 26B); null without one, when only customers with their own term have something overdue.</summary>
    public int? CreditTermDays { get; set; }
    /// <summary>How many customers have a term of their own.</summary>
    public int CustomersWithOwnTerm { get; set; }
    public string DefaultBody { get; set; } = null!;
    public List<string> Placeholders { get; set; } = [];
    /// <summary>The active credit reminder templates to choose from.</summary>
    public List<CreditReminderTemplateDto> Templates { get; set; } = [];
    public int MinEveryDays { get; set; }
    public int MaxEveryDays { get; set; }
    /// <summary>The hours of the business's day when reminders are queued, so none arrives at night.</summary>
    public string SendFrom { get; set; } = null!;
    public string SendUntil { get; set; } = null!;
}

public class CreditReminderTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Body { get; set; } = null!;
    /// <summary>The template approved in Meta, which reaches customers outside the 24 hours after their last message.</summary>
    public string? ProviderTemplateName { get; set; }
}

/// <summary>The reminders of what is overdue (Fase 26C): owners and admins, who decide how to ask for the money.</summary>
[RequiresTenantManager]
public record GetCreditRemindersQuery : IRequest<CreditRemindersDto>;

public class GetCreditRemindersQueryHandler : IRequestHandler<GetCreditRemindersQuery, CreditRemindersDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetCreditRemindersQueryHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<CreditRemindersDto> Handle(GetCreditRemindersQuery request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        var templates = await _context.MessageTemplates.AsNoTracking()
            .Where(t => t.Purpose == MessagePurpose.CreditReminder && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new CreditReminderTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Body = t.Body,
                ProviderTemplateName = t.ProviderTemplateName
            })
            .ToListAsync(cancellationToken);
        return new CreditRemindersDto
        {
            Enabled = settings?.CreditRemindersEnabled ?? false,
            EveryDays = settings?.CreditReminderEveryDays ?? TenantSettings.DefaultCreditReminderEveryDays,
            // A template retired after it was chosen sends the default text (CreditReminderPlanner): say so, and the screen
            // saves without it instead of sending back one the update refuses.
            TemplateId = templates.Any(t => t.Id == settings?.CreditReminderTemplateId) ? settings!.CreditReminderTemplateId : null,
            WhatsAppConnected = await CampaignRules.IsWhatsAppConnectedAsync(_context, _tenant.TenantId, cancellationToken),
            CreditTermDays = settings?.CreditTermDays,
            CustomersWithOwnTerm = await _context.Customers.CountAsync(c => c.CreditTermDays != null, cancellationToken),
            DefaultBody = MessagingDefaults.CreditReminderBody,
            Placeholders = MessageTemplateText.AllowedPlaceholders(MessagePurpose.CreditReminder).ToList(),
            Templates = templates,
            MinEveryDays = TenantSettings.MinCreditReminderEveryDays,
            MaxEveryDays = TenantSettings.MaxCreditReminderEveryDays,
            SendFrom = CreditReminderPlanner.QueueFrom.ToString("HH:mm"),
            SendUntil = CreditReminderPlanner.QueueUntil.ToString("HH:mm")
        };
    }
}

/// <summary>
/// Turns the reminders of what is overdue on or off (Fase 26C), every how many days and with which template: owners and
/// admins, with WhatsApp connected to turn them on. Recorded in the activity log.
/// </summary>
[RequiresTenantManager]
public record UpdateCreditRemindersCommand : IRequest<Result<CreditRemindersDto>>
{
    public bool Enabled { get; init; }
    public int EveryDays { get; init; } = TenantSettings.DefaultCreditReminderEveryDays;
    /// <summary>Empty uses the default text.</summary>
    public Guid? TemplateId { get; init; }
}

public class UpdateCreditRemindersCommandValidator : AbstractValidator<UpdateCreditRemindersCommand>
{
    public UpdateCreditRemindersCommandValidator()
    {
        RuleFor(x => x.EveryDays)
            .InclusiveBetween(TenantSettings.MinCreditReminderEveryDays, TenantSettings.MaxCreditReminderEveryDays)
            .WithMessage(TenantSettings.CreditReminderEveryDaysMessage);
    }
}

public class UpdateCreditRemindersCommandHandler : IRequestHandler<UpdateCreditRemindersCommand, Result<CreditRemindersDto>>
{
    public const string TemplateNotFoundMessage = "Elige una plantilla de recordatorio del fiado activa.";

    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ICurrentUserService _currentUser;
    private readonly IMediator _mediator;

    public UpdateCreditRemindersCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenant,
        ICurrentUserService currentUser,
        IMediator mediator)
    {
        _context = context;
        _tenant = tenant;
        _currentUser = currentUser;
        _mediator = mediator;
    }

    public async Task<Result<CreditRemindersDto>> Handle(UpdateCreditRemindersCommand request, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, cancellationToken);
        if (settings is null)
            return Result.Failure<CreditRemindersDto>("Configura primero tu negocio.");
        if (request.Enabled && !await CampaignRules.IsWhatsAppConnectedAsync(_context, _tenant.TenantId, cancellationToken))
            return Result.Failure<CreditRemindersDto>(MessagingDefaults.WhatsAppNotConfigured);
        if (request.TemplateId is { } templateId
            && !await _context.MessageTemplates.AnyAsync(
                t => t.Id == templateId && t.Purpose == MessagePurpose.CreditReminder && t.IsActive, cancellationToken))
            return Result.Failure<CreditRemindersDto>(TemplateNotFoundMessage);

        var previous = new { Enabled = settings.CreditRemindersEnabled, EveryDays = settings.CreditReminderEveryDays, TemplateId = settings.CreditReminderTemplateId };
        try
        {
            settings.UpdateCreditReminders(request.Enabled, request.EveryDays, request.TemplateId);
        }
        catch (DomainException refused)
        {
            return Result.Failure<CreditRemindersDto>(refused.Message);
        }

        var current = new { Enabled = settings.CreditRemindersEnabled, EveryDays = settings.CreditReminderEveryDays, TemplateId = settings.CreditReminderTemplateId };
        if (!previous.Equals(current))
        {
            // Messages the business sends its customers about their debts: worth recording who turned them on.
            _context.AuditLogs.Add(new AuditLog(
                tenantId: settings.TenantId,
                entityName: nameof(TenantSettings),
                entityId: settings.Id.ToString(),
                action: "CreditRemindersUpdated",
                performedBy: _currentUser.UserName ?? "system",
                metadata: JsonSerializer.Serialize(new { Previous = previous, New = current })));
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await _mediator.Send(new GetCreditRemindersQuery(), cancellationToken));
    }
}
