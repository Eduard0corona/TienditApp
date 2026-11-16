using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using FluentValidation;
using MediatR;

namespace FoodParkDigital.Application.OnlineMenu;

/// <summary>The order an assistant prepared from one before, and what of that one does not go, with why.</summary>
public sealed class PreparedAssistantReorderDto
{
    public PreparedAssistantOrderDto Order { get; init; } = null!;

    /// <summary>The products of the order before that do not go («Pan de muerto: ya no está en el menú»).</summary>
    public List<string> Left { get; init; } = new();
}

/// <summary>
/// «Pedir otra vez» from an AI assistant with the customer's account (Fase 32E, ADR 0097): an order prepared, as
/// <see cref="PrepareAssistantOrderCommand"/> prepares one, with the products of one of the account's orders that the menu
/// takes today. It reaches the business only when the customer sends it from its link. Only an order of the account itself.
/// </summary>
[RequiresAuthentication]
public record PrepareAssistantReorderCommand : IRequest<Result<PreparedAssistantReorderDto>>, ITransactionalRequest
{
    public Guid OrderId { get; init; }
    /// <summary>Where the customer wants it delivered, if they said: the store offers delivery there.</summary>
    public string? PostalCode { get; init; }
    /// <summary>The assistant, as it introduced itself.</summary>
    public string? Source { get; init; }
}

public class PrepareAssistantReorderCommandValidator : AbstractValidator<PrepareAssistantReorderCommand>
{
    public PrepareAssistantReorderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty().WithMessage("Usa el id de un pedido de mis_pedidos.");
        RuleFor(x => x.PostalCode)
            .Must(code => DeliveryZone.IsPostalCode(DeliveryZone.NormalizePostalCode(code)))
            .When(x => !string.IsNullOrWhiteSpace(x.PostalCode))
            .WithMessage(GetPublicDeliveryQuoteQueryValidator.PostalCodeMessage);
        RuleFor(x => x.Source).MaximumLength(AssistantOrder.MaxSourceLength);
    }
}

public class PrepareAssistantReorderCommandHandler : IRequestHandler<PrepareAssistantReorderCommand, Result<PreparedAssistantReorderDto>>
{
    public const string NotFoundMessage = "No encontré ese pedido en la cuenta del cliente: usa el id de mis_pedidos.";
    public const string NothingToRepeatMessage = "Ningún producto de ese pedido se puede pedir otra vez desde el asistente.";

    private readonly ISender _sender;

    public PrepareAssistantReorderCommandHandler(ISender sender)
    {
        _sender = sender;
    }

    public async Task<Result<PreparedAssistantReorderDto>> Handle(PrepareAssistantReorderCommand request, CancellationToken cancellationToken)
    {
        // The account's own order, as Mis pedidos reads it: another one is not found.
        var before = await _sender.Send(new GetMyOrderQuery(request.OrderId), cancellationToken);
        if (before is null)
            return Result.Failure<PreparedAssistantReorderDto>(NotFoundMessage);
        if (!before.CanOrderAgain)
            return Result.Failure<PreparedAssistantReorderDto>($"{before.BusinessName} no recibe pedidos en línea: el cliente puede pedirle directo.");

        var left = new List<string>();
        var asked = new List<(string Name, AssistantOrderItemInput Item)>();
        foreach (var item in before.Items.Where(item => item.Quantity > 0))
        {
            // A product sold by weight (1.5 kg) has no whole quantity for the cart of the store.
            if (item.Quantity != decimal.Truncate(item.Quantity))
            {
                left.Add($"{item.ProductName}: se vende por peso, pídelo en la página del negocio.");
                continue;
            }
            asked.Add((item.ProductName, new AssistantOrderItemInput
            {
                ProductId = item.ProductId,
                Quantity = (int)Math.Min(item.Quantity, AccountCart.MaxLineQuantity),
                OptionIds = item.OptionIds,
                Notes = item.Notes is { Length: > AccountCart.MaxLineNotesLength } notes ? notes[..AccountCart.MaxLineNotesLength] : item.Notes
            }));
        }
        if (asked.Count == 0)
            return Result.Failure<PreparedAssistantReorderDto>(NothingToRepeatMessage);
        foreach (var (name, _) in asked.Skip(AccountCart.MaxLines))
            left.Add($"{name}: un pedido lleva hasta {AccountCart.MaxLines} productos distintos.");
        asked = asked.Take(AccountCart.MaxLines).ToList();

        // Joins this request's transaction: prepared as any order of an assistant, with its limits and the prices of today.
        var prepared = await _sender.Send(new PrepareAssistantOrderCommand
        {
            Slug = before.BusinessSlug,
            Items = asked.Select(a => a.Item).ToList(),
            PostalCode = request.PostalCode,
            Source = request.Source
        }, cancellationToken);
        if (prepared.IsFailure)
            return Result.Failure<PreparedAssistantReorderDto>(prepared.Error!);

        left.AddRange(prepared.Value.Refused.Select(refused => $"{asked[refused.Position - 1].Name}: {refused.Reason}"));
        return Result.Success(new PreparedAssistantReorderDto { Order = prepared.Value, Left = left });
    }
}
