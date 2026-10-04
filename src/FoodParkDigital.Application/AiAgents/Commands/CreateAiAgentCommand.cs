using AutoMapper;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Commands;

[RequiresPermission(TenantPermission.ManageAiAgent)]
public record CreateAiAgentCommand : IRequest<Result<AiAgentDto>>
{
    public string Name { get; init; } = null!;
    public string Role { get; init; } = null!;
    public string Personality { get; init; } = "Profesional y resolutivo";
    public string Language { get; init; } = "Espanol";
    public string Tone { get; init; } = "Amigable";
    public string? BusinessName { get; init; }
    public string? BusinessDescription { get; init; }
    public string? BusinessProductsAndServices { get; init; }
    public string WelcomeMessage { get; init; } = "Hola, soy tu asistente virtual. Te ayudo con productos, servicios y órdenes.";
    public string FallbackMessage { get; init; } = "No tengo esa información por ahora, pero puedo ayudarte con otra consulta.";
    public string ClosingMessage { get; init; } = "Gracias por contactarnos. Quedo atento para ayudarte nuevamente.";
    public bool AskForName { get; init; } = true;
    public bool AskForPhone { get; init; } = true;
    public bool AskForEmail { get; init; }
    public bool AskForAddress { get; init; }
    public string? CustomQuestions { get; init; }
    public string Model { get; init; } = "gpt-4.1-mini";
    public decimal Temperature { get; init; } = 0.4m;
    public int MaxTokens { get; init; } = 800;
    public bool UseConversationMemory { get; init; } = true;
    public bool StrictMode { get; init; }
}

public class CreateAiAgentCommandValidator : AbstractValidator<CreateAiAgentCommand>
{
    public CreateAiAgentCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Personality).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Language).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Tone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.BusinessName).MaximumLength(200);
        RuleFor(x => x.BusinessDescription).MaximumLength(2000);
        RuleFor(x => x.BusinessProductsAndServices).MaximumLength(4000);
        RuleFor(x => x.WelcomeMessage).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.FallbackMessage).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.ClosingMessage).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.CustomQuestions).MaximumLength(4000);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Temperature).InclusiveBetween(0m, 1m);
        RuleFor(x => x.MaxTokens).InclusiveBetween(100, 4000);
    }
}

public class CreateAiAgentCommandHandler : IRequestHandler<CreateAiAgentCommand, Result<AiAgentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public CreateAiAgentCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<AiAgentDto>> Handle(CreateAiAgentCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        var exists = await _context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == _tenantService.TenantId, cancellationToken);
        if (!exists)
            return Result.Failure<AiAgentDto>("No se encontró configuración del negocio para crear el agente.");

        var duplicatedName = await _context.AiAgents
            .AnyAsync(x => x.Name == name, cancellationToken);
        if (duplicatedName)
            return Result.Failure<AiAgentDto>("Ya existe un agente con ese nombre.");

        var agent = new AiAgent(_tenantService.TenantId, name, request.Role.Trim());
        agent.UpdateGeneral(
            name: name,
            role: request.Role.Trim(),
            personality: request.Personality.Trim(),
            language: request.Language.Trim(),
            tone: request.Tone.Trim(),
            businessName: TrimToNull(request.BusinessName),
            businessDescription: TrimToNull(request.BusinessDescription),
            businessProductsAndServices: TrimToNull(request.BusinessProductsAndServices));
        agent.UpdateMessages(
            welcomeMessage: request.WelcomeMessage.Trim(),
            fallbackMessage: request.FallbackMessage.Trim(),
            closingMessage: request.ClosingMessage.Trim());
        agent.UpdateDataCapture(
            askForName: request.AskForName,
            askForPhone: request.AskForPhone,
            askForEmail: request.AskForEmail,
            askForAddress: request.AskForAddress,
            customQuestions: TrimToNull(request.CustomQuestions));
        agent.UpdateAiConfiguration(
            model: request.Model.Trim(),
            temperature: request.Temperature,
            maxTokens: request.MaxTokens,
            useConversationMemory: request.UseConversationMemory,
            strictMode: request.StrictMode);

        _context.AiAgents.Add(agent);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(_mapper.Map<AiAgentDto>(agent));
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
