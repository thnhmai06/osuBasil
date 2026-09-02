using Microsoft.AspNetCore.Routing.Constraints;

namespace Basil.Web.Routing;

/// <summary>
///     Route constraint for an id segment that matches any all-digit string, regardless of whether it
///     fits in <see cref="int" />.
/// </summary>
/// <remarks>
///     <see cref="IntRouteConstraint" /> (the built-in <c>:int</c> constraint) rejects a route segment
///     that overflows <see cref="int" /> as a non-match, which — when nothing else in the app matches
///     that path either — surfaces as a bare, empty 404 rather than a real error: routing fails before
///     any handler, or <c>EnvelopeMiddleware</c>, gets a chance to describe what went wrong. This
///     constraint only checks that the segment is numeric, deliberately not checking magnitude, so
///     routing still matches and control reaches the handler's own <see cref="int" /> parameter, whose
///     failed model bind the framework already turns into a proper <c>400 Bad Request</c>.
/// </remarks>
public sealed class NumericIdRouteConstraint : IRouteConstraint
{
	/// <summary>The route constraint's token, used as <c>{id:numericid}</c> in a route template.</summary>
	public const string Token = "numericid";

	/// <inheritdoc />
	public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values,
		RouteDirection routeDirection)
	{
		if (!values.TryGetValue(routeKey, out var value) || value is null) return false;
		var text = value.ToString();
		if (string.IsNullOrEmpty(text)) return false;

		// Accepts an optional leading '-' so a negative id still reaches the handler's own lookup
		// (which cleanly reports "not found") instead of falling out of routing entirely, matching
		// what the built-in :int constraint already let through.
		var digits = text[0] == '-' ? text[1..] : text;
		return digits.Length > 0 && digits.All(char.IsAsciiDigit);
	}
}