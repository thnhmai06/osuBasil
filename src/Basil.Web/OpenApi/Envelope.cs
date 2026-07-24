namespace Basil.Web.OpenApi;

/// <summary>
///     The Enveloped Response Standard every JSON body on the `api.` host is wrapped in by
///     <see cref="Basil.Web.Middleware.EnvelopeMiddleware" /> — file downloads and SSE streams are the
///     only exceptions (their `Content-Type` never contains "json", so the middleware leaves them
///     untouched). <see cref="Meta" /> is populated only for a paginated list route (see
///     <see cref="Basil.Web.Routing.PagedResult{T}" />); every other route leaves it null.
/// </summary>
public sealed record Envelope<T>(
    bool Success,
    int Code,
    string Message,
    T? Data,
    PageMeta? Meta,
    IReadOnlyList<FieldError>? Errors,
    DateTimeOffset Timestamp);

/// <summary>Pagination metadata for a list route's envelope, derived from its `PagedResult{T}` body.</summary>
public sealed record PageMeta(int Page, int PageSize, int TotalRecords, int TotalPages);

/// <summary>One field-level validation failure, used only by the handful of routes with an unambiguous single bad field.</summary>
public sealed record FieldError(string? Field, string Message);
