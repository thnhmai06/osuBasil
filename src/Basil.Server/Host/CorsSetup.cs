namespace Basil.Server.Host;

/// <summary>Registers the host's CORS policy.</summary>
internal static class CorsSetup
{
	/// <summary>The name of the CORS policy applied to the `api.` host.</summary>
	public const string PolicyName = "ApiCors";

	/// <summary>
	///     Registers a permissive CORS policy allowing any origin, method, and header.
	/// </summary>
	/// <remarks>
	///     Permissive by design: the api. host is meant to be called directly from arbitrary
	///     browser-based tooling (tournament overlays, dashboards, OBS browser sources). No
	///     credentials are ever sent (the admin key is a plain <c>Authorization</c> header, not a
	///     cookie), so <c>AllowAnyOrigin</c> is safe here.
	/// </remarks>
	/// <param name="builder">The web application builder whose CORS policy is registered.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		builder.Services.AddCors(options =>
			options.AddPolicy(PolicyName, policy => policy
				.AllowAnyOrigin()
				.AllowAnyMethod()
				.AllowAnyHeader()));
	}
}
