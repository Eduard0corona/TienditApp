using System.Text.Json;
using FoodParkDigital.WebApi.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Routing;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>A request that does not bind answers in Spanish, as ProblemDetails, without .NET's English messages.</summary>
public class SpanishModelBindingTests
{
    [Fact]
    public void BodyThatDoesNotRead_ShouldNameTheField_InSpanish_WithoutTypeNames()
    {
        var context = Context();
        context.ModelState.TryAddModelException("$.amount", new JsonException("The JSON value could not be converted to System.Decimal."));
        // A body that did not read leaves its parameter empty too.
        context.ModelState.AddModelError("command", "The command field is required.");

        var problem = Problem(SpanishModelBinding.InvalidModelStateResponse(context));

        Assert.Equal(SpanishModelBinding.InvalidFormatMessage, Assert.Single(problem.Errors["$.amount"]));
        Assert.False(problem.Errors.ContainsKey("command"));
        Assert.Equal(SpanishModelBinding.InvalidFormatMessage, problem.Detail);
        Assert.Equal("validation_failed", problem.Extensions["code"]);
        Assert.Equal(SpanishModelBinding.Title, problem.Title);
    }

    [Fact]
    public void MissingField_ShouldSayItIsRequired_InSpanish_AndKeepOurOwnMessages()
    {
        var context = Context();
        context.ModelState.AddModelError("Phone", "The Phone field is required.");
        context.ModelState.AddModelError("Quantity", "La cantidad debe ser mayor a cero.");

        var problem = Problem(SpanishModelBinding.InvalidModelStateResponse(context));

        Assert.Equal("El campo Phone es obligatorio.", Assert.Single(problem.Errors["Phone"]));
        Assert.Equal("La cantidad debe ser mayor a cero.", Assert.Single(problem.Errors["Quantity"]));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
    }

    [Fact]
    public void BinderMessages_ShouldBeSpanish()
    {
        var messages = new DefaultModelBindingMessageProvider();

        SpanishModelBinding.Apply(messages);

        Assert.Equal("El valor 'abc' no es válido para Id.", messages.AttemptedValueIsInvalidAccessor("abc", "Id"));
        Assert.Equal("Envía los datos de la solicitud.", messages.MissingRequestBodyRequiredValueAccessor());
        Assert.Equal("Cantidad debe ser un número.", messages.ValueMustBeANumberAccessor("Cantidad"));
    }

    private static ActionContext Context()
        => new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor
        {
            Parameters = [new() { Name = "command", BindingInfo = new BindingInfo { BindingSource = BindingSource.Body } }]
        });

    private static ValidationProblemDetails Problem(IActionResult result)
        => Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(result).Value);
}
