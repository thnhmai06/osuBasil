using Basil.Host.Bancho;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;

namespace Basil.Host;

/// <summary>Builds the server application, the same way for the executable and for tests that run it in process.</summary>
public static class HostApplication
{
	/// <summary>Creates the server application from the configuration.</summary>
	/// <param name="args">The command-line arguments, which can override the configuration.</param>
	/// <param name="configure">Changes the builder before the application is built; the tests use it to replace services.</param>
	/// <returns>The application, not started.</returns>
	public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
	{
		var builder = WebApplication.CreateBuilder(args);
		builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Basil:Server"));
		builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection("Basil:Storage"));
		builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection("Basil:Database"));
		builder.Services.AddBasil();
		configure?.Invoke(builder);

		var app = builder.Build();
		app.UseTrustedProxies();
		app.UseBancho();
		return app;
	}
}
