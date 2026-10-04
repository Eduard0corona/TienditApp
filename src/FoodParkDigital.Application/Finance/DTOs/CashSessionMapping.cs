using System.Text.Json;
using FoodParkDigital.Domain.Finance.Entities;

namespace FoodParkDigital.Application.Finance.DTOs;

internal static class CashSessionMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps a session whose transactions are loaded.</summary>
    public static CashSessionDto ToDto(CashSession session, Guid? currentUserId, string? locationName) => new()
    {
        Id = session.Id,
        OpenedAt = session.OpenedAt,
        OpenedBy = session.OpenedBy,
        OpenedByUserId = session.OpenedByUserId,
        IsMine = session.IsOwnedBy(currentUserId),
        InventoryLocationId = session.InventoryLocationId,
        InventoryLocationName = locationName,
        OpeningAmount = session.OpeningAmount,
        ClosedAt = session.ClosedAt,
        ClosedBy = session.ClosedBy,
        ClosingAmount = session.ClosingAmount,
        Status = session.Status,
        Notes = session.Notes,
        Inflows = session.Inflows,
        Outflows = session.Outflows,
        NonCashInflows = session.NonCashInflows,
        ExpectedBalance = session.ExpectedBalance,
        ExpectedClosingAmount = session.ExpectedClosingAmount,
        ClosingDifference = session.ClosingDifference,
        ClosingNotes = session.ClosingNotes,
        CashCount = ReadCashCount(session.CashCountJson)
    };

    public static string? WriteCashCount(IReadOnlyCollection<CashCountLineDto>? lines)
        => lines is null || lines.Count == 0 ? null : JsonSerializer.Serialize(lines, JsonOptions);

    public static List<CashCountLineDto> ReadCashCount(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<CashCountLineDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static Guid? ParseUserId(string? userId) => Guid.TryParse(userId, out var parsed) ? parsed : null;
}
