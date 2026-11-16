namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// The MCP server with the customer's account (Fase 32E, ADR 0097) as the app publishes it: the resource (RFC 8707) every
/// code and token of an assistant is for. The API sets it from its own address, never from what an assistant sends.
/// </summary>
public interface IAssistantAccountServer
{
    /// <summary>Its canonical address, without a final slash («https://vecinomarkets.com/mcp/cuenta»).</summary>
    string Resource { get; }
}
