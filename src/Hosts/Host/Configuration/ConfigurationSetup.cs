using Microsoft.Extensions.Options;

namespace Basil.Host.Configuration;

/// <summary>Reads the configuration files that sit beside the executable.</summary>
public static class ConfigurationSetup
{
	/// <summary>Gets the folder of the configuration files, the certificate and the templates.</summary>
	public static string Directory => Path.Combine(AppContext.BaseDirectory, "Configurations");

	/// <summary>Gets the folder of the templates, such as the wording of BasilBot's replies.</summary>
	public static string Templates => Path.Combine(Directory, "Templates");

	/// <summary>
	///     Adds <c>Configurations/appsettings.json</c> and <c>Configurations/appsettings.{Environment}.json</c>, which are read
	///     again when they change; the environment and the command line win over them.
	/// </summary>
	/// <param name="builder">The builder to add the files to.</param>
	/// <param name="args">The command-line arguments.</param>
	public static void Add(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration.AddJsonFile(Path.Combine(Directory, "appsettings.json"), true, true);
		builder.Configuration.AddJsonFile(Path.Combine(Directory, $"appsettings.{builder.Environment.EnvironmentName}.json"), true,
			true);
		// The builder added the environment before these files; adding it again puts it, and the command line, last.
		builder.Configuration.AddEnvironmentVariables();
		builder.Configuration.AddCommandLine(args);
	}

	/// <summary>
	///     Registers the options of a section that is read when the server starts: the value is bound once, so a change to
	///     the file waits for the next start.
	/// </summary>
	/// <typeparam name="T">The options type.</typeparam>
	/// <param name="services">The service collection.</param>
	/// <param name="section">The configuration section to bind.</param>
	/// <param name="complete">Completes the bound value, for example by resolving a relative path.</param>
	public static void AddAtStart<T>(IServiceCollection services, IConfiguration section, Action<T>? complete = null)
		where T : class
	{
		var value = section.Get<T>() ?? Activator.CreateInstance<T>();
		complete?.Invoke(value);
		services.AddSingleton<IOptions<T>>(Options.Create(value));
	}

	/// <summary>Gives a path that is relative to the executable the full path it names.</summary>
	/// <param name="path">The configured path.</param>
	/// <returns>The path itself when it is rooted; otherwise the path under the executable's folder.</returns>
	public static string Resolve(string path)
	{
		return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
	}
}
