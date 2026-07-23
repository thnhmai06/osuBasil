namespace Basil.Web.OpenApi;

/// <summary>Uniform error body for every non-2xx JSON response across the api. host, replacing ad-hoc anonymous `{ error = "..." }` shapes so responses get a real declared schema.</summary>
public sealed record ErrorResponse(string Error);
