using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Reviews;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Mcp;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// TienditApp's MCP server (Fase 32B, ADR 0096), through its HTTP endpoint as an AI assistant calls it: JSON-RPC over
/// Streamable HTTP, stateless and anonymous. Its tools send the store's requests to a fake mediator, as the store pages'
/// tests do, and answer Markdown.
/// </summary>
public sealed class McpServerTests : IClassFixture<PublicPagesTests.PagesApi>
{
    private static readonly Guid Latte = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
    private static readonly Guid Grande = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly string Token = new string('A', 40) + "b_c";

    private readonly PublicPagesTests.PagesApi _api;
    private int _id;

    public McpServerTests(PublicPagesTests.PagesApi api)
    {
        _api = api;
        _api.Sender.Reset();
    }

    [Fact]
    public async Task AnAssistant_ShouldSeeTheServer_AndItsFiveTools_WithoutAnAccount()
    {
        var initialized = await CallAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "prueba", ["version"] = "1.0" }
        });
        Assert.Equal(McpServer.Name, (string?)initialized["serverInfo"]?["name"]);
        Assert.Contains("nunca pidas datos de tarjeta", (string?)initialized["instructions"]);

        var tools = (await CallAsync("tools/list", new JsonObject()))["tools"]!.AsArray();
        Assert.Equal(
            ["buscar_negocios", "cotizar_envio", "preparar_pedido", "ver_menu", "ver_pedido"],
            tools.Select(tool => (string)tool!["name"]!).Order());
        // Only preparing an order writes; nothing deletes.
        var prepare = tools.Single(tool => (string?)tool!["name"] == "preparar_pedido")!;
        Assert.False((bool?)prepare["annotations"]?["readOnlyHint"]);
        Assert.False((bool?)prepare["annotations"]?["destructiveHint"]);
        Assert.All(tools.Where(tool => (string?)tool!["name"] != "preparar_pedido"),
            tool => Assert.True((bool?)tool!["annotations"]?["readOnlyHint"]));
        // Nothing of it reads the database.
        Assert.Empty(_api.Sender.Requests);
    }

    [Fact]
    public async Task SearchBusinesses_ShouldListThem_WithTheirSlug_AndWhetherTheyDeliverToThePostalCode()
    {
        _api.Sender.Directory = new PublicDirectoryDto
        {
            Businesses = new PaginatedList<PublicDirectoryBusinessDto>(
            [
                new PublicDirectoryBusinessDto
                {
                    Name = "Café # Luna", Slug = "cafe-luna", Category = BusinessCategory.Food, City = "Puebla",
                    AcceptsOrders = true, DeliveryAvailable = true, StatusLabel = "Abierto", StatusDetail = "cierra a las 20:00",
                    DeliversToPostalCode = true, DeliveryFee = 30m, MinimumOrder = 100m,
                    Rating = new RatingSummaryDto { Count = 12, Average = 4.6m }, Description = "Café de olla"
                },
                new PublicDirectoryBusinessDto
                {
                    Name = "Tienda Sol", Slug = "tienda-sol", Category = BusinessCategory.Groceries, OrdersPaused = true,
                    DeliversToPostalCode = false
                }
            ], 2, 1, StoreTools.PageSize)
        };

        var text = await ToolAsync("buscar_negocios", new JsonObject { ["texto"] = "café", ["codigo_postal"] = "72000", ["categoria"] = "food" });

        var query = Assert.IsType<GetPublicDirectoryQuery>(Assert.Single(_api.Sender.Requests));
        Assert.Equal(BusinessCategory.Food, query.Category);
        Assert.Equal("café", query.Search);
        Assert.Equal("72000", query.PostalCode);
        Assert.Equal(StoreTools.PageSize, query.PageSize);
        Assert.Contains("## Café \\# Luna\n", text);
        Assert.Contains("- slug: `cafe-luna`\n", text);
        Assert.Contains("- Página: https://app.tienditapp.mx/m/cafe-luna\n", text);
        Assert.Contains("- Ahora: Abierto, cierra a las 20:00\n", text);
        Assert.Contains("- Pedidos en línea: sí, para recoger o a domicilio\n", text);
        Assert.Contains("- Entrega en el código postal: sí, envío $30.00, pedido mínimo $100.00\n", text);
        Assert.Contains("- Calificación: 4.6 de 5 (12 reseñas)\n", text);
        Assert.Contains("- Pedidos en línea: sí, pero ahora no recibe\n", text);
        Assert.Contains("- Entrega en el código postal: no\n", text);
    }

    [Fact]
    public async Task SearchBusinesses_ShouldSay_ACategoryThatDoesNotExist_WithoutReadingAnything()
    {
        var error = await ToolErrorAsync("buscar_negocios", new JsonObject { ["categoria"] = "Pizza" });

        Assert.StartsWith("Esa categoría no existe. Usa una de: Food,", error);
        Assert.Empty(_api.Sender.Requests);
    }

    [Fact]
    public async Task TheMenu_ShouldSayToPrepareTheOrderWithTheTool_OrToOrderInThePage_WhenTheBusinessTurnedItOff()
    {
        _api.Sender.Menu = Menu(assistantOrders: true);
        var text = await ToolAsync("ver_menu", new JsonObject { ["slug"] = " cafe-luna " });

        Assert.Equal("cafe-luna", Assert.IsType<GetPublicMenuQuery>(Assert.Single(_api.Sender.Requests)).Slug);
        Assert.StartsWith("# Café Luna · menú en línea\n\n", text);
        Assert.Contains("## Cómo pedir\n\nCon la herramienta `preparar_pedido`, el slug `cafe-luna` y los ids de abajo:", text);
        Assert.DoesNotContain("?agregar=", text);
        Assert.Contains($"- id: `{Latte}`\n", text);

        _api.Sender.Menu = Menu(assistantOrders: false);
        var off = await ToolAsync("ver_menu", new JsonObject { ["slug"] = "cafe-luna" });
        Assert.Contains("Este negocio no recibe pedidos desde asistentes de IA: el cliente puede pedir en su página, "
            + "https://app.tienditapp.mx/m/cafe-luna.\n", off);
        Assert.DoesNotContain("preparar_pedido", off);
    }

    [Fact]
    public async Task TheMenu_ShouldSay_AStoreThatIsNotThere()
    {
        var error = await ToolErrorAsync("ver_menu", new JsonObject { ["slug"] = "no-existe" });

        Assert.Equal(AssistantOrdersMessages.NotFound, error);
    }

    [Fact]
    public async Task QuoteDelivery_ShouldSayTheFee_TheMinimum_AndFromWhenItIsFree()
    {
        _api.Sender.DeliveryQuote = new PublicDeliveryQuoteDto
        {
            PostalCode = "72000", Covered = true, Fee = 35m, MinimumOrder = 150m, FreeDeliveryFrom = 400m
        };

        var text = await ToolAsync("cotizar_envio", new JsonObject { ["slug"] = "cafe-luna", ["codigo_postal"] = "72000" });

        var query = Assert.IsType<GetPublicDeliveryQuoteQuery>(Assert.Single(_api.Sender.Requests));
        Assert.Equal(("cafe-luna", "72000"), (query.Slug, query.PostalCode));
        Assert.Equal("Entrega en el código postal 72000: envío $35.00, pedido mínimo $150.00, gratis desde $400.00.\n", text);
    }

    [Fact]
    public async Task PrepareOrder_ShouldSendTheProducts_AndAnswerTheLink_WhatItCosts_AndWhatWasNotAdded()
    {
        _api.Sender.Prepared = Result.Success(new PreparedAssistantOrderDto
        {
            Token = Token,
            BusinessName = "Café Luna",
            Slug = "cafe-luna",
            Lines = [new PreparedAssistantOrderLineDto(Latte, "Latte", ["Grande"], 2, 67m, 134m, "sin azúcar")],
            Refused = [new RefusedAssistantOrderLineDto(2, Guid.NewGuid(), "Ese producto no está en el menú.")],
            ItemsTotal = 134m,
            PricesIncludeTax = true,
            Delivery = new PublicDeliveryQuoteDto { PostalCode = "72000", Covered = true, Fee = 0m },
            TakesOrdersNow = true,
            ExpiresAt = new DateTimeOffset(2026, 11, 16, 18, 30, 0, TimeSpan.Zero)
        });

        var text = await ToolAsync("preparar_pedido", new JsonObject
        {
            ["slug"] = "cafe-luna",
            ["productos"] = new JsonArray(
                new JsonObject { ["id"] = Latte.ToString(), ["cantidad"] = 2, ["opciones"] = new JsonArray(Grande.ToString()), ["nota"] = "sin azúcar" },
                new JsonObject { ["id"] = Guid.NewGuid().ToString() }),
            ["codigo_postal"] = "72000"
        });

        var command = Assert.IsType<PrepareAssistantOrderCommand>(Assert.Single(_api.Sender.Requests));
        Assert.Equal("cafe-luna", command.Slug);
        Assert.Equal("72000", command.PostalCode);
        Assert.Equal(2, command.Items.Count);
        Assert.Equal((Latte, 2, "sin azúcar"), (command.Items[0].ProductId, command.Items[0].Quantity, command.Items[0].Notes));
        Assert.Equal([Grande], command.Items[0].OptionIds);
        // Without a quantity, one.
        Assert.Equal(1, command.Items[1].Quantity);
        // Who prepared it, as its client introduces itself.
        Assert.Equal("prueba-mcp/1.0", command.Source);

        Assert.StartsWith("# Pedido preparado en Café Luna\n\nTodavía no se envía.", text);
        Assert.Contains($"Enlace: https://app.tienditapp.mx/m/cafe-luna#pedido={Token}\n", text);
        Assert.Contains("Vence el 16 de noviembre, 18:30 (UTC).", text);
        Assert.Contains("- 2 × Latte (Grande) · $134.00 · nota: sin azúcar\n", text);
        Assert.Contains("Productos: $134.00 con IVA incluido.\n", text);
        Assert.Contains("## No se agregó\n\n- El producto 2: Ese producto no está en el menú.\n", text);
        Assert.Contains("Entrega en el código postal 72000: envío gratis.\n", text);
        Assert.DoesNotContain("Ahora no recibe", text);
    }

    [Fact]
    public async Task PrepareOrder_ShouldSay_ANotIdOfTheMenu_OrWhyTheStoreRefusedIt()
    {
        var notAnId = await ToolErrorAsync("preparar_pedido", new JsonObject
        {
            ["slug"] = "cafe-luna",
            ["productos"] = new JsonArray(new JsonObject { ["id"] = "latte" })
        });
        Assert.Equal("El producto 1 no tiene un id del menú. Usa los ids que da ver_menu.", notAnId);
        Assert.Empty(_api.Sender.Requests);

        _api.Sender.Prepared = Result.Failure<PreparedAssistantOrderDto>(PrepareAssistantOrderCommandHandler.TurnedOffMessage);
        var refused = await ToolErrorAsync("preparar_pedido", new JsonObject
        {
            ["slug"] = "cafe-luna",
            ["productos"] = new JsonArray(new JsonObject { ["id"] = Latte.ToString() })
        });
        Assert.Equal(PrepareAssistantOrderCommandHandler.TurnedOffMessage, refused);
    }

    [Fact]
    public async Task PrepareOrder_ShouldSay_ThatTheLinkKeepsIt_WhileTheStoreTakesNoOrders()
    {
        _api.Sender.Prepared = Result.Success(new PreparedAssistantOrderDto
        {
            Token = Token, BusinessName = "Café Luna", Slug = "cafe-luna",
            Lines = [new PreparedAssistantOrderLineDto(Latte, "Latte", [], 1, 55m, 55m, null)],
            ItemsTotal = 63.8m, PricesIncludeTax = false, DeliveryOnly = true,
            TakesOrdersNow = false, NotNowReason = "Cerrado: abre mañana a las 9:00.",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        });

        var text = await ToolAsync("preparar_pedido", new JsonObject
        {
            ["slug"] = "cafe-luna",
            ["productos"] = new JsonArray(new JsonObject { ["id"] = Latte.ToString(), ["cantidad"] = 1 })
        });

        Assert.Contains("Productos: $63.80 con IVA.\n", text);
        Assert.Contains("Este negocio solo entrega a domicilio: el cliente escribe su dirección en la página.\n", text);
        Assert.Contains("Ahora no recibe pedidos (Cerrado: abre mañana a las 9:00): el enlace guarda el pedido", text);
    }

    [Theory]
    [InlineData("https://app.tienditapp.mx/m/cafe-luna#pedido=")]
    [InlineData("")]
    public async Task SeeOrder_ShouldTakeTheWholeLinkOrItsSecret_AndSayWhetherItWasSent(string prefix)
    {
        _api.Sender.AssistantOrder = new AssistantOrderDto
        {
            Slug = "cafe-luna", BusinessName = "Café Luna", State = AssistantOrderState.Sent,
            OrderNumber = "ORD-0042", OrderStatus = "En preparación", ExpiresAt = DateTimeOffset.UtcNow
        };

        var text = await ToolAsync("ver_pedido", new JsonObject { ["enlace"] = prefix + Token });

        Assert.Equal(Token, Assert.IsType<GetAssistantOrderQuery>(Assert.Single(_api.Sender.Requests)).Token);
        Assert.Equal("# Pedido en Café Luna\n\nEl cliente lo envió. Folio: ORD-0042. Estado: En preparación. "
            + "El cliente sigue su pedido con el enlace que le dio la página al enviarlo.\n", text);
    }

    [Fact]
    public async Task SeeOrder_ShouldSay_ItIsWaiting_ItExpired_OrItIsNotThere()
    {
        _api.Sender.AssistantOrder = new AssistantOrderDto
        {
            Slug = "cafe-luna", BusinessName = "Café Luna", State = AssistantOrderState.Open,
            ExpiresAt = new DateTimeOffset(2026, 11, 16, 9, 5, 0, TimeSpan.Zero)
        };
        Assert.Contains("Todavía no lo envía. El enlace funciona hasta el 16 de noviembre, 09:05 (UTC).",
            await ToolAsync("ver_pedido", new JsonObject { ["enlace"] = Token }));

        _api.Sender.AssistantOrder = new AssistantOrderDto
        {
            Slug = "cafe-luna", BusinessName = "Café Luna", State = AssistantOrderState.Expired, ExpiresAt = DateTimeOffset.UtcNow
        };
        Assert.Contains("Venció sin que el cliente lo enviara.", await ToolAsync("ver_pedido", new JsonObject { ["enlace"] = Token }));

        _api.Sender.AssistantOrder = null;
        Assert.Equal("No encontramos ese pedido. Revisa el enlace que dio preparar_pedido.",
            await ToolErrorAsync("ver_pedido", new JsonObject { ["enlace"] = Token }));
    }

    [Fact]
    public async Task ATool_ShouldNotSayWhatFailed_WhenTheDatabaseDoes()
    {
        _api.Sender.Failure = new InvalidOperationException("Sin base: Server=sql-prod;Password=secreto");

        var error = await ToolErrorAsync("ver_menu", new JsonObject { ["slug"] = "cafe-luna" });

        // Only the generic error: nothing of what failed.
        Assert.Equal(".", error);
    }

    [Fact]
    public async Task TheServer_ShouldNotBeInTheApiContract()
    {
        using var response = await _api.Http.GetAsync("/swagger/v1/swagger.json");
        var contract = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("\"/mcp\"", contract);
    }

    private static PublicMenuDto Menu(bool assistantOrders) => new()
    {
        BusinessName = "Café Luna",
        Slug = "cafe-luna",
        Category = BusinessCategory.Food,
        PricesIncludeTax = true,
        AcceptsOrders = true,
        AssistantOrders = assistantOrders,
        Categories =
        [
            new PublicMenuCategoryDto
            {
                Id = Guid.NewGuid(),
                Name = "Bebidas",
                Products = [new PublicMenuProductDto { Id = Latte, Name = "Latte", Price = 55m, AvailableNow = true }]
            }
        ]
    };

    private async Task<string> ToolAsync(string name, JsonObject arguments)
    {
        var result = await CallAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });
        Assert.True((bool?)result["isError"] is null or false, result.ToJsonString());
        return (string)result["content"]![0]!["text"]!;
    }

    /// <summary>
    /// What a tool told the assistant went wrong: the SDK says which tool, then the message the tool gave (an
    /// McpException); any other failure is only its generic error, without a message.
    /// </summary>
    private async Task<string> ToolErrorAsync(string name, JsonObject arguments)
    {
        var result = await CallAsync("tools/call", new JsonObject { ["name"] = name, ["arguments"] = arguments });
        Assert.True((bool?)result["isError"]);
        var text = (string)result["content"]![0]!["text"]!;
        var prefix = $"An error occurred invoking '{name}'";
        Assert.StartsWith(prefix, text);
        return text[prefix.Length..].TrimStart(':', ' ');
    }

    /// <summary>One JSON-RPC request to the server, as an MCP client sends it over Streamable HTTP; its result.</summary>
    private async Task<JsonNode> CallAsync(string method, JsonObject parameters)
    {
        var body = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = ++_id, ["method"] = method, ["params"] = parameters };
        using var request = new HttpRequestMessage(HttpMethod.Post, McpServer.Path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.UserAgent.ParseAdd("prueba-mcp/1.0");
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        using var response = await _api.Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {raw}");

        // Streamable HTTP answers JSON or one server-sent event with it.
        var json = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
            ? string.Join("\n", raw.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal)).Select(line => line[5..].Trim()))
            : raw;
        var message = JsonNode.Parse(json)!;
        Assert.Null(message["error"]);
        return message["result"]!;
    }
}
