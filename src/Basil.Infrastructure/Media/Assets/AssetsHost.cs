using Microsoft.AspNetCore.Http;

namespace Basil.Infrastructure.Media.Assets;

/// <summary>
///     Matches an incoming request's host against a set of allowed hostnames.
/// </summary>
/// <remarks>
///     ImageSharp.Web's <c>UseImageSharp()</c> middleware runs before endpoint routing splits
///     requests by host (<c>RequireHost</c>), so an <c>IImageProvider</c> that only checks the
///     request path could match a request meant for a different host group (e.g. a path that exists
///     both as a redirect on <c>api.</c> and as a real file on <c>assets.</c>). Every asset provider
///     calls this first to stay scoped to its own host group.
/// </remarks>
public static class AssetsHost
{
	/// <summary>Gets whether the request's host matches one of the given hostnames.</summary>
	/// <param name="context">The current HTTP context.</param>
	/// <param name="hosts">The allowed hostnames (e.g. <c>assets.example.com</c>).</param>
	public static bool Matches(HttpContext context, IReadOnlyCollection<string> hosts)
	{
		return hosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	///     Builds the <c>assets.</c> hostnames a provider should match, for the configured domain and
	///     the <c>ppy.sh</c> fallback — matching the pattern every other host group registers under in
	///     <c>BanchoHostGroups</c>.
	/// </summary>
	/// <param name="configuredDomain">The server's configured apex domain.</param>
	public static string[] AssetsHostsFor(string configuredDomain)
	{
		return new[] { "ppy.sh", configuredDomain }.Distinct()
			.Select(domain => $"assets.{domain}")
			.ToArray();
	}
}
