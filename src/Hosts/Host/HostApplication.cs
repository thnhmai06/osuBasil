using Basil.Bot;
using Basil.Bot.Replies;
using Basil.Host.Bancho;
using Basil.Host.Cli;
using Basil.Host.Cli.Socket;
using Basil.Host.Irc;
using Basil.Host.Rest;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Images;
using Basil.Host.Shared.Diagnostics;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Caching;

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
		var mode = CliModes.Choose(args, Console.IsInputRedirected, Console.IsOutputRedirected, CliModes.TerminalSupportsColour());
		args = CliModes.Strip(args);
		var builder = WebApplication.CreateBuilder(args);
		builder.Logging.ClearProviders();
		if (mode == CliMode.NoGui)
			builder.Logging.AddSimpleConsole(options =>
			{
				options.ColorBehavior = LoggerColorBehavior.Disabled;
				options.SingleLine = true;
				options.TimestampFormat = "HH:mm:ss ";
			});
		builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("Basil:Server"));
		builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection("Basil:Storage"));
		builder.Services.AddOptions<DatabaseOptions>().Bind(builder.Configuration.GetSection("Basil:Database"));
		builder.Services.AddOptions<IrcOptions>().Configure<IOptions<ServerOptions>>((irc, server) =>
		{
			irc.Port = server.Value.Ports.Irc;
			irc.Domain = server.Value.Domain;
		});
		builder.Services.AddOptions<RestOptions>().Configure<IOptions<ServerOptions>>((rest, server) =>
			rest.Domain = server.Value.Domain);
		builder.Services.AddOptions<RepliesOptions>().Bind(builder.Configuration.GetSection("Basil:Bot:Replies"));
		builder.Services.AddBasil();
		builder.Services.AddBot();
		builder.Services.AddIrcListener();
		builder.Services.AddRestApi();
		builder.Services.AddRestImages();
		builder.Services.AddOptions<PhysicalFileSystemCacheOptions>().Configure<IOptions<StorageOptions>>((cache, storage) =>
		{
			cache.CacheRootPath = Path.Combine(storage.Value.DataDirectory, "Cache");
			cache.CacheFolder = "imagesharp";
		});
		builder.Services.AddSingleton<ICacheCleaner, CacheCleaner>();
		builder.Services.AddCli(mode, provider => ControlSocket.PathIn(provider.GetRequiredService<IOptions<StorageOptions>>().Value.DataDirectory),
			Path.Combine(AppContext.BaseDirectory, "Configurations"));
		configure?.Invoke(builder);

		var app = builder.Build();
		app.UseRequestMetrics();
		app.UseTrustedProxies();
		app.UseRestImages();
		app.UseBancho();
		app.UseRest();
		return app;
	}
}
