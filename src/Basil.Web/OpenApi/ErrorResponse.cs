namespace Basil.Web.OpenApi;

/// <summary>
///     The uniform error body for every non-2xx JSON response across the api. host. Replaces the
///     ad-hoc anonymous `{ error = "..." }` shapes so responses get a real declared schema.
/// </summary>
/// <param name="Error">A human-readable description of the failure.</param>
public sealed record ErrorResponse(string Error);