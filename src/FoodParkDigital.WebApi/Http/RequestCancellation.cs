using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FoodParkDigital.WebApi.Http;

/// <summary>
/// The <see cref="CancellationToken"/> every action receives and passes to its use case. A read (GET, HEAD) gets the
/// request's own: it stops when the client disconnects, so a list or a report nobody waits for frees the database. A
/// change (POST, PUT, PATCH, DELETE) gets none and finishes once it arrived: most use cases save in several steps (an
/// account and then its business, a new password and then the sessions it ends, a message sent and then stored), and
/// a page closed between two of them would leave half the change.
/// </summary>
public static class RequestCancellation
{
    /// <summary>Binds the token of every action through <see cref="For"/>, before the framework's own binder.</summary>
    public static void Apply(MvcOptions options)
        => options.ModelBinderProviders.Insert(0, new BinderProvider());

    /// <returns>The request's cancellation for a read; none for a change.</returns>
    public static CancellationToken For(HttpContext context)
        => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            ? context.RequestAborted
            : CancellationToken.None;

    internal sealed class BinderProvider : IModelBinderProvider
    {
        private static readonly Binder Instance = new();

        public IModelBinder? GetBinder(ModelBinderProviderContext context)
            => context.Metadata.ModelType == typeof(CancellationToken) ? Instance : null;
    }

    private sealed class Binder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            // Boxed once, as the framework's binder does: the validation state and the result share the instance.
            object token = For(bindingContext.HttpContext);
            bindingContext.ValidationState.Add(token, new ValidationStateEntry { SuppressValidation = true });
            bindingContext.Result = ModelBindingResult.Success(token);
            return Task.CompletedTask;
        }
    }
}
