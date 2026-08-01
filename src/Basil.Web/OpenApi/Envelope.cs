namespace Basil.Web.OpenApi;

/// <summary>
///     The Enveloped Response Standard every JSON body on the `api.` host is wrapped in by
///     <see cref="Basil.Web.Middleware.EnvelopeMiddleware" />, file downloads and SSE streams are the
///     only exceptions (their `Content-Type` never contains "json", so the middleware leaves them
///     untouched). <see cref="Meta" /> is populated only for a paginated list route (see
///     <see cref="Basil.Web.Routing.PagedResult{T}" />); every other route leaves it null.
/// </summary>
/// <typeparam name="T">The type of the response payload carried in <see cref="Data" />.</typeparam>
/// <param name="Success">A value that indicates whether the response status indicates success.</param>
/// <param name="Code">The HTTP status code of the response.</param>
/// <param name="Message">A human-readable summary of the operation's outcome.</param>
/// <param name="Data">The response payload, or <see langword="null" /> for error and SSE responses.</param>
/// <param name="Meta">Pagination metadata for a paginated list response, otherwise <see langword="null" />.</param>
/// <param name="Errors">Field-level validation failures, or <see langword="null" />.</param>
/// <param name="Timestamp">The UTC time at which the envelope was produced.</param>
public sealed record Envelope<T>(
	bool Success,
	int Code,
	string Message,
	T? Data,
	PageMeta? Meta,
	IReadOnlyList<FieldError>? Errors,
	DateTimeOffset Timestamp);

/// <summary>
///     Pagination metadata for a list route's envelope, derived from its `PagedResult{T}` body.
/// </summary>
/// <param name="Page">The requested page number.</param>
/// <param name="PageSize">The number of records per page.</param>
/// <param name="TotalRecords">The total number of records matching the request.</param>
/// <param name="TotalPages">The total number of pages given the total records and page size.</param>
public sealed record PageMeta(int Page, int PageSize, int TotalRecords, int TotalPages);

/// <summary>
///     One field-level validation failure, used only by the handful of routes with an unambiguous
///     single bad field.
/// </summary>
/// <param name="Field">
///     The name of the field that failed validation, or <see langword="null" /> when no single field
///     applies.
/// </param>
/// <param name="Message">A human-readable description of the validation failure.</param>
public sealed record FieldError(string? Field, string Message);