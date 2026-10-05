using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace FoodParkDigital.WebApi.Errors;

/// <summary>
/// Requests that do not bind (a missing field, a number written as text, a body that is not JSON) answer in Spanish
/// like every other error: the binder's messages are replaced, the JSON reader's English messages (with .NET type
/// names) never reach the client, and the 400 of [ApiController] is ProblemDetails with <c>validation_failed</c>.
/// </summary>
public static partial class SpanishModelBinding
{
    public const string Title = "Revisa los datos.";
    public const string InvalidFormatMessage = "El valor no tiene el formato esperado.";
    public const string InvalidValueMessage = "El valor no es válido.";

    public static void Apply(DefaultModelBindingMessageProvider messages)
    {
        messages.SetMissingBindRequiredValueAccessor(name => $"Falta el valor de {name}.");
        messages.SetMissingKeyOrValueAccessor(() => "Falta un valor.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "Envía los datos de la solicitud.");
        messages.SetValueMustNotBeNullAccessor(value => $"El valor '{value}' no es válido.");
        messages.SetAttemptedValueIsInvalidAccessor((value, name) => $"El valor '{value}' no es válido para {name}.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        messages.SetUnknownValueIsInvalidAccessor(name => $"El valor de {name} no es válido.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => InvalidValueMessage);
        messages.SetValueIsInvalidAccessor(value => $"El valor '{value}' no es válido.");
        messages.SetValueMustBeANumberAccessor(name => $"{name} debe ser un número.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Escribe un número.");
    }

    /// <summary>The answer of [ApiController] when the model state is invalid.</summary>
    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var jsonFailed = context.ModelState.Any(entry => entry.Value?.Errors.Any(error => error.Exception is JsonException) == true);
        var bodyParameters = context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            // A body that did not read leaves its parameter empty too: the field that failed is the useful error.
            .Where(entry => !(jsonFailed && bodyParameters.Contains(entry.Key)))
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(Message).Distinct().ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = Title,
            Detail = errors.Values.SelectMany(messages => messages).FirstOrDefault(),
            Instance = context.HttpContext.Request.Path
        };
        ApiProblem.Complete(problem, context.HttpContext, ApiErrorCodes.ValidationFailed);
        return new BadRequestObjectResult(problem) { ContentTypes = { ApiProblem.ContentType } };
    }

    private static string Message(ModelError error)
    {
        if (string.IsNullOrEmpty(error.ErrorMessage))
            return error.Exception is JsonException ? InvalidFormatMessage : InvalidValueMessage;

        // [Required] (explicit, or implied by a non-nullable property) keeps the English text of DataAnnotations.
        var required = RequiredField().Match(error.ErrorMessage);
        return required.Success ? $"El campo {required.Groups[1].Value} es obligatorio." : error.ErrorMessage;
    }

    [GeneratedRegex(@"^The (.+) field is required\.$", RegexOptions.CultureInvariant)]
    private static partial Regex RequiredField();
}
