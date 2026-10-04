using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Messaging.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging.Templates;

public record GetMessageTemplatesQuery : IRequest<List<MessageTemplateDto>>
{
    public MessagePurpose? Purpose { get; init; }
    public bool IncludeInactive { get; init; } = true;
}

public class GetMessageTemplatesQueryHandler : IRequestHandler<GetMessageTemplatesQuery, List<MessageTemplateDto>>
{
    private readonly IApplicationDbContext _context;

    public GetMessageTemplatesQueryHandler(IApplicationDbContext context) => _context = context;

    public async Task<List<MessageTemplateDto>> Handle(GetMessageTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MessageTemplates.AsNoTracking();
        if (request.Purpose is { } purpose)
            query = query.Where(t => t.Purpose == purpose);
        if (!request.IncludeInactive)
            query = query.Where(t => t.IsActive);

        var templates = await query.OrderBy(t => t.Name).Take(MessageTemplateLimits.MaxPerBusiness).ToListAsync(cancellationToken);
        return templates.Select(t => t.ToDto()).ToList();
    }
}

public static class MessageTemplateLimits
{
    public const int MaxPerBusiness = 100;
}

public record CreateMessageTemplateCommand : IRequest<Result<MessageTemplateDto>>
{
    public string Name { get; init; } = string.Empty;
    public MessagePurpose Purpose { get; init; }
    public string Body { get; init; } = string.Empty;
    public string? ProviderTemplateName { get; init; }
    public string? LanguageCode { get; init; }
}

public class CreateMessageTemplateCommandValidator : AbstractValidator<CreateMessageTemplateCommand>
{
    public CreateMessageTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MessageTemplate.MaxNameLength);
        RuleFor(x => x.Purpose).IsInEnum();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(MessageTemplate.MaxBodyLength);
        RuleFor(x => x.ProviderTemplateName).MaximumLength(MessageTemplate.MaxProviderTemplateNameLength);
        RuleFor(x => x.LanguageCode).MaximumLength(MessageTemplate.MaxLanguageCodeLength);
    }
}

public class CreateMessageTemplateCommandHandler : IRequestHandler<CreateMessageTemplateCommand, Result<MessageTemplateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public CreateMessageTemplateCommandHandler(IApplicationDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<Result<MessageTemplateDto>> Handle(CreateMessageTemplateCommand request, CancellationToken cancellationToken)
    {
        if (await _context.MessageTemplates.CountAsync(cancellationToken) >= MessageTemplateLimits.MaxPerBusiness)
            return Result.Failure<MessageTemplateDto>($"Un negocio puede tener hasta {MessageTemplateLimits.MaxPerBusiness} plantillas.");

        MessageTemplate template;
        try
        {
            template = new MessageTemplate(
                _tenant.TenantId,
                request.Name,
                request.Purpose,
                request.Body,
                request.ProviderTemplateName,
                request.LanguageCode);
        }
        catch (DomainException ex)
        {
            return Result.Failure<MessageTemplateDto>(ex.Message);
        }

        _context.MessageTemplates.Add(template);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(template.ToDto());
    }
}

public record UpdateMessageTemplateCommand : IRequest<Result<MessageTemplateDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? ProviderTemplateName { get; init; }
    public string? LanguageCode { get; init; }
    public bool IsActive { get; init; } = true;
}

public class UpdateMessageTemplateCommandValidator : AbstractValidator<UpdateMessageTemplateCommand>
{
    public UpdateMessageTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(MessageTemplate.MaxNameLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(MessageTemplate.MaxBodyLength);
        RuleFor(x => x.ProviderTemplateName).MaximumLength(MessageTemplate.MaxProviderTemplateNameLength);
        RuleFor(x => x.LanguageCode).MaximumLength(MessageTemplate.MaxLanguageCodeLength);
    }
}

/// <summary>Messages already queued keep the text they were queued with.</summary>
public class UpdateMessageTemplateCommandHandler : IRequestHandler<UpdateMessageTemplateCommand, Result<MessageTemplateDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateMessageTemplateCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<MessageTemplateDto>> Handle(UpdateMessageTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _context.MessageTemplates.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
        if (template is null)
            return Result.Failure<MessageTemplateDto>("La plantilla no existe.");

        try
        {
            template.Update(request.Name, request.Body, request.ProviderTemplateName, request.LanguageCode);
        }
        catch (DomainException ex)
        {
            return Result.Failure<MessageTemplateDto>(ex.Message);
        }

        template.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(template.ToDto());
    }
}
