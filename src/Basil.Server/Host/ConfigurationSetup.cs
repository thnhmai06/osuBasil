namespace Basil.Server.Host;

/// <summary>Configures the host's configuration sources.</summary>
internal static class ConfigurationSetup
{
	/// <summary>
	///     Replaces the inherited configuration sources with exactly the three Basil intends:
	///     <c>appsettings.json</c>, an optional environment-specific overlay, and command-line args.
	/// </summary>
	/// <remarks>
	///     <c>WebApplication.CreateBuilder</c> installs a dozen sources of its own, including an
	///     unprefixed environment-variable provider -- application settings have one source of
	///     truth, so the inherited set is dropped entirely rather than relied on to lose a precedence
	///     race against sources added afterward. <c>appsettings.json</c> carries all Basil settings
	///     under a <c>Basil</c> section alongside standard ASP.NET Core config (<c>Logging</c>,
	///     <c>AllowedHosts</c>). It lives under <c>Data/</c> rather than next to the executable, so it
	///     moves along with the rest of the server's persistent state (see <c>StorageOptions</c>) when
	///     relocating a deployment -- one directory to copy instead of two.
	///     <c>builder.Environment</c> resolved <c>EnvironmentName</c> from
	///     <c>ASPNETCORE_ENVIRONMENT</c> while the builder was being constructed -- that is host
	///     configuration, which <c>docker-compose.yml</c> relies on, and clearing the source list here
	///     does not disturb it.
	/// </remarks>
	/// <param name="builder">The web application builder whose configuration is replaced.</param>
	/// <param name="args">The command-line arguments layered last, at the highest priority.</param>
	public static void Configure(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration.Sources.Clear();
		builder.Configuration
			.AddJsonFile(Path.Combine("Data", "appsettings.json"), false, true)
			.AddJsonFile(Path.Combine("Data", $"appsettings.{builder.Environment.EnvironmentName}.json"), true, true)
			.AddCommandLine(args);
	}
}