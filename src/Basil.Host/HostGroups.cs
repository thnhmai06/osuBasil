namespace Basil.Host;

/// <summary>
///     The constructed route group for each host, returned by <see cref="HostGroups.Create" />.
/// </summary>
/// <param name="Bancho">The `c.`/`ce.`/`c4.`/`c5.`/`c6.` bancho protocol host.</param>
/// <param name="OsuWeb">The `osu.` osu! web endpoints host.</param>
/// <param name="BeatmapAssets">The `b.` beatmap assets host.</param>
/// <param name="Avatar">The `a.` user avatars host.</param>
/// <param name="Api">The `api.` Bancho REST API host.</param>
/// <param name="Assets">The `assets.` ImageSharp.Web-served media host.</param>
internal sealed record Hosts(
	RouteGroupBuilder Bancho,
	RouteGroupBuilder OsuWeb,
	RouteGroupBuilder BeatmapAssets,
	RouteGroupBuilder Avatar,
	RouteGroupBuilder Api,
	RouteGroupBuilder Assets);

/// <summary>
///     Registers the host groups for both the configured domain and the fallback
///     <c>ppy.sh</c> domain.
/// </summary>
/// <remarks>
///     The following hosts are registered:
///     <list type="bullet">
///         <item><c>c.</c>, <c>ce.</c>, <c>c4.</c>, <c>c5.</c>, and <c>c6.</c> for the bancho protocol.</item>
///         <item><c>osu.</c> for the osu! web endpoints.</item>
///         <item><c>b.</c> for beatmap assets.</item>
///         <item><c>a.</c> for user avatars.</item>
///         <item><c>api.</c> for the Bancho REST API.</item>
///         <item><c>assets.</c> for menu/beatmapset media served through ImageSharp.Web.</item>
///     </list>
/// </remarks>
internal static class HostGroups
{
	private static readonly string[] BanchoSubdomains = ["c", "ce", "c4", "c5", "c6"];

	private const string OsuWebSubdomain = "osu";
	private const string BeatmapAssetSubdomain = "b";
	private const string AvatarSubdomain = "a";
	private const string ApiSubdomain = "api";
	private const string AssetsSubdomain = "assets";

	/// <summary>Every host name the server answers on for the given domain, including the domain itself.</summary>
	/// <remarks>
	///     Derived from the same subdomain names <see cref="Create" /> builds its route groups from, so
	///     that anything advertising these names cannot drift from what the server actually serves.
	/// </remarks>
	/// <param name="domain">The configured domain.</param>
	/// <returns>The domain and each subdomain the server serves on it.</returns>
	internal static IReadOnlyList<string> HostNamesFor(string domain)
	{
		return
		[
			domain,
			.. BanchoSubdomains.Select(subdomain => $"{subdomain}.{domain}"),
			$"{OsuWebSubdomain}.{domain}",
			$"{BeatmapAssetSubdomain}.{domain}",
			$"{AvatarSubdomain}.{domain}",
			$"{ApiSubdomain}.{domain}",
			$"{AssetsSubdomain}.{domain}"
		];
	}

	/// <summary>
	///     Constructs the route groups for every host, for the configured domain and the
	///     fallback <c>ppy.sh</c> domain.
	/// </summary>
	/// <remarks>
	///     Construction only: no route is registered on any group here. Each returned group is a
	///     bare <see cref="RouteGroupBuilder" /> restricted to its host; the caller
	///     (<see cref="SliceRegistration.MapAll" />) is responsible for mapping every host's routes onto
	///     it, in the order each host expects.
	/// </remarks>
	/// <param name="app"> The web application to construct the host groups on. </param>
	/// <param name="configuredDomain"> The primary domain used to expose the host groups. </param>
	/// <returns>The constructed route group for each host.</returns>
	internal static Hosts Create(WebApplication app, string configuredDomain)
	{
		var domains = new[] { "ppy.sh", configuredDomain }.Distinct().ToArray();

		var banchoHosts = domains
			.SelectMany(domain => BanchoSubdomains.Select(subdomain => $"{subdomain}.{domain}"))
			.ToArray();
		var osuWebHosts = domains.Select(domain => $"{OsuWebSubdomain}.{domain}").ToArray();
		var beatmapAssetHosts = domains.Select(domain => $"{BeatmapAssetSubdomain}.{domain}").ToArray();
		var avatarHosts = domains.Select(domain => $"{AvatarSubdomain}.{domain}").ToArray();
		var apiHosts = domains.Select(domain => $"{ApiSubdomain}.{domain}").ToArray();
		var assetsHosts = domains.Select(domain => $"{AssetsSubdomain}.{domain}").ToArray();

		return new Hosts(
			app.MapGroup("/").RequireHost(banchoHosts),
			app.MapGroup("/").RequireHost(osuWebHosts),
			app.MapGroup("/").RequireHost(beatmapAssetHosts),
			app.MapGroup("/").RequireHost(avatarHosts),
			app.MapGroup("/").RequireHost(apiHosts),
			app.MapGroup("/").RequireHost(assetsHosts));
	}
}
