using Microsoft.Extensions.Options;

namespace Basil.Configuration;

public static class ConfigurationSetup
{
	public static string Directory => Path.Combine(AppContext.BaseDirectory, "Data");

	public static void Add(WebApplicationBuilder builder, string[] args)
	{
		builder.Configuration.AddJsonFile(Path.Combine(Directory, "appsettings.json"), true, true);
		builder.Configuration.AddJsonFile(
			Path.Combine(Directory, $"appsettings.{builder.Environment.EnvironmentName}.json"), true,
			true);
		// The builder added the environment before these files; adding it again puts it, and the command line, last.
		builder.Configuration.AddEnvironmentVariables();
		builder.Configuration.AddCommandLine(args);
	}

	public static void AddAtStart<T>(IServiceCollection services, IConfiguration section, Action<T>? complete = null)
		where T : class
	{
		var value = section.Get<T>() ?? Activator.CreateInstance<T>();
		complete?.Invoke(value);
		services.AddSingleton(Options.Create(value));
	}

	public static string Resolve(string path)
	{
		return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
	}
}