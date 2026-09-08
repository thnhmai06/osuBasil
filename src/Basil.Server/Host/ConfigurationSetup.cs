namespace Basil.Server.Host;

/// <summary>Configures the host's configuration sources.</summary>
internal static class ConfigurationSetup
{
	/// <summary>
	///     Re-layers configuration, adding environment-specific and command-line sources on top of appsettings.json.
	/// </summary>
	/// <remarks>
	///     <c>appsettings.json</c> carries all Basil settings under a <c>Basil</c> section alongside
	///     standard ASP.NET Core config (<c>Logging</c>, <c>AllowedHosts</c>). It lives under
	///     <c>Data/</c> rather than next to the executable, so it moves along with the rest of the
	///     server's persistent state (see <c>StorageOptions</c>) when relocating a deployment -- one
	///     directory to copy instead of two. <c>appsettings.{EnvironmentName}.json</c> and
	///     command-line args are layered on top for environment-specific overrides.
	/// </remarks>
	/// <param name="builder">The web application builder whose configuration is extended.</param>
	/// <param name="args">The command-line arguments layered last, at the highest priority.</param>
	public static void Configure(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration
			.AddJsonFile(Path.Combine("Data", "appsettings.json"), false, true)
			.AddJsonFile(Path.Combine("Data", $"appsettings.{builder.Environment.EnvironmentName}.json"), true, true)
			.AddCommandLine(args);
	}
}
