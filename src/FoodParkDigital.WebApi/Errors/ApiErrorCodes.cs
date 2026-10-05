namespace FoodParkDigital.WebApi.Errors;

/// <summary>
/// The <c>code</c> of every error response (<c>application/problem+json</c>): stable, in English and snake_case, so a
/// client decides by it and never by the wording of <c>title</c> or <c>detail</c>. Adding a code is compatible;
/// renaming or removing one breaks the clients.
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>400. A field is missing or invalid; <c>errors</c> lists the messages per field.</summary>
    public const string ValidationFailed = "validation_failed";
    /// <summary>400. The use case refused the request (an empty cart, a payment above the balance...).</summary>
    public const string BusinessRule = "business_rule";
    /// <summary>422. An invariant of the domain refused the change.</summary>
    public const string DomainRule = "domain_rule";
    /// <summary>401. No valid session.</summary>
    public const string Unauthorized = "unauthorized";
    /// <summary>403. The person may not do this.</summary>
    public const string Forbidden = "forbidden";
    /// <summary>403. The session has no business selected.</summary>
    public const string TenantRequired = "tenant_required";
    /// <summary>403. The person does not belong to the business (or the membership is disabled).</summary>
    public const string TenantAccessDenied = "tenant_access_denied";
    /// <summary>403. The business is suspended; <c>detail</c> says what to do.</summary>
    public const string TenantSuspended = "tenant_suspended";
    /// <summary>403. The module (kitchen, inventory...) is not enabled for the business.</summary>
    public const string ModuleDisabled = "module_disabled";
    /// <summary>404. The record does not exist or belongs to another business.</summary>
    public const string NotFound = "not_found";
    /// <summary>409. The request collides with the current data (a duplicate, a table already taken...).</summary>
    public const string Conflict = "conflict";
    /// <summary>409. Someone changed the record at the same time; reload and try again.</summary>
    public const string ConcurrencyConflict = "concurrency_conflict";
    /// <summary>409. The status changed meanwhile and the transition is no longer allowed.</summary>
    public const string InvalidStateTransition = "invalid_state_transition";
    /// <summary>429. Too many attempts; try again later.</summary>
    public const string TooManyRequests = "too_many_requests";
    /// <summary>500 and other server errors; <c>traceId</c> finds the log entry.</summary>
    public const string ServerError = "server_error";
    /// <summary>Any other 4xx.</summary>
    public const string BadRequest = "bad_request";

    /// <summary>The code of a status when nothing more specific is known.</summary>
    public static string ForStatus(int status) => status switch
    {
        400 => BusinessRule,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        422 => DomainRule,
        429 => TooManyRequests,
        >= 500 => ServerError,
        _ => BadRequest
    };
}
