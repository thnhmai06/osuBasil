namespace Basil.Server.Host;

internal static class ConfigurationSetup
{
	public static void Configure(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration.Sources.Clear();
		AddSources(builder.Configuration, builder.Environment.EnvironmentName, args);
	}

	/// <summary>
	///     Adds the server's configuration sources, in the order that decides which value wins.
	/// </summary>
	/// <remarks>
	///     Shared with the command-line commands, which need the same settings the server would read
	///     but have no web host to read them through.
	/// </remarks>
	/// <param name="configuration">The builder to add the sources to.</param>
	/// <param name="environmentName">The environment whose overlay file is applied, when present.</param>
	/// <param name="args">The process arguments.</param>
	public static void AddSources(IConfigurationBuilder configuration, string environmentName, string[] args)
	{
		configuration
			.AddJsonFile(Path.Combine("Data", "appsettings.json"), false, true)
			.AddJsonFile(Path.Combine("Data", $"appsettings.{environmentName}.json"), true, true)
			.AddCommandLine(args);
	}
}
