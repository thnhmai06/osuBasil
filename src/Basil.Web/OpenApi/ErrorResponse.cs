// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.OpenApi;

/// <summary>The uniform error body for every non-2xx JSON response across the api. host.</summary>
/// <remarks>
///     Replaces the ad-hoc anonymous `{ error = "..." }` shapes previously returned for these
///     statuses, so error responses get a real declared schema that Scalar and generated client
///     SDKs can render and validate against.
/// </remarks>
/// <param name="Error">A human-readable description of the failure.</param>
public sealed record ErrorResponse(string Error);