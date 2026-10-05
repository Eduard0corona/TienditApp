using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Commands;

[RequiresPermission(TenantPermission.ManageAiAgent)]
public record UpdateAiAgentCommand : IRequest<Result<AiAgentDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Role { get; init; } = null!;
    public string Personality { get; init; } = null!;
    public string Language { get; init; } = null!;
    public string Tone { get; init; } = null!;
    public string? BusinessName { get; init; }
    public string? BusinessDescription { get; init; }
    public string? BusinessProductsAndServices { get; init; }
    public string WelcomeMessage { get; init; } = null!;
    public string FallbackMessage { get; init; } = null!;
    public string ClosingMessage { get; init; } = null!;
    public bool AskForName { get; init; }
    public bool AskForPhone { get; init; }
    public bool AskForEmail { get; init; }
    public bool AskForAddress { get; init; }
    public string? CustomQuestions { get; init; }
    public string Model { get; init; } = null!;
    public decimal Temperature { get; init; }
    public int MaxTokens { get; init; }
    public bool UseConversationMemory { get; init; }
    public bool StrictMode { get; init; }
}

public class UpdateAiAgentCommandValidator : AbstractValidator<UpdateAiAgentCommand>
{
    public UpdateAiAgentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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

public class UpdateAiAgentCommandHandler : IRequestHandler<UpdateAiAgentCommand, Result<AiAgentDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdateAiAgentCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<AiAgentDto>> Handle(UpdateAiAgentCommand request, CancellationToken cancellationToken)
    {
        var agent = await _context.AiAgents
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AiAgent", request.Id);

        var name = request.Name.Trim();
        var duplicatedName = await _context.AiAgents
            .AnyAsync(x => x.Id != request.Id && x.Name == name, cancellationToken);
        if (duplicatedName)
            return Result.Failure<AiAgentDto>("Ya existe un agente con ese nombre.");

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

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(agent.ToDto());
    }

    private static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
