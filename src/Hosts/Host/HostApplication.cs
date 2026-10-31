using Basil.Bot;
using Basil.Bot.Replies;
using Basil.Host.Bancho;
using Basil.Host.Cli;
using Basil.Host.Cli.Socket;
using Basil.Host.Configuration;
using Basil.Host.Irc;
using Basil.Host.Lifetime;
using Basil.Host.Logging;
using Basil.Host.Rest;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Images;
using Basil.Host.Shared.Diagnostics;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Storage;
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
		var mode = CliModes.Choose(args, Console.IsInputRedirected, Console.IsOutputRedirected,
			CliModes.TerminalSupportsColour());
		args = CliModes.Strip(args);
		var builder = WebApplication.CreateBuilder(args);
		ConfigurationSetup.Add(builder, args);
		var drops = ServerLogging.Add(builder, mode);
		TelemetrySetup.Add(builder);
		builder.Services.AddOptions<IrcOptions>().Configure<IOptions<ServerOptions>>((irc, server) =>
		{
			irc.Port = server.Value.Ports.Irc;
			irc.Domain = server.Value.Domain;
		});
		builder.Services.AddOptions<RestOptions>().Configure<IOptions<ServerOptions>>((rest, server) =>
			rest.Domain = server.Value.Domain);
		builder.Services.AddOptions<RepliesOptions>().Bind(builder.Configuration.GetSection("Basil:Bot:Replies"))
			.PostConfigure(replies => replies.Path = ConfigurationSetup.Resolve(replies.Path));
		builder.Services.AddOptions<UpdateOptions>().Bind(builder.Configuration.GetSection("Basil:Updates"));
		builder.Services.AddSingleton<ConfigurationWatcher>();
		builder.Services.AddHostedService(provider => provider.GetRequiredService<ConfigurationWatcher>());
		builder.Services.AddHostedService<LanAdvertiser>();
		builder.Services.AddHostedService<UpdateChecker>();
		builder.Services.AddBasil();
		builder.Services.AddBot();
		builder.Services.AddIrcListener();
		builder.Services.AddRestApi();
		builder.Services.AddRestImages();
		builder.Services.AddOptions<PhysicalFileSystemCacheOptions>()
			.Configure<IOptions<StorageOptions>>((cache, storage) =>
			{
				cache.CacheRootPath = Path.Combine(storage.Value.DataDirectory, "Cache");
				cache.CacheFolder = "imagesharp";
			});
		builder.Services.AddSingleton<ICacheCleaner, CacheCleaner>();
		builder.Services.AddCli(mode,
			provider => ControlSocket.PathIn(
				provider.GetRequiredService<IOptions<StorageOptions>>().Value.DataDirectory),
			Path.Combine(AppContext.BaseDirectory, "Configurations"));
		builder.Services.AddSingleton<ShutdownSequence>();
		// Registered last, so that it stops first: the other services are told and the rooms closed while they still run.
		builder.Services.AddHostedService<GracefulShutdown>();
		builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimings.Host);
		configure?.Invoke(builder);

		// Read once, after the caller's changes, so a later edit of the files waits for the next start.
		ConfigurationSetup.AddAtStart<ServerOptions>(builder.Services,
			builder.Configuration.GetSection("Basil:Server"));
		ConfigurationSetup.AddAtStart<StorageOptions>(builder.Services,
			builder.Configuration.GetSection("Basil:Storage"),
			storage => storage.DataDirectory = ConfigurationSetup.Resolve(storage.DataDirectory ?? "Data"));
		ConfigurationSetup.AddAtStart<DatabaseOptions>(builder.Services,
			builder.Configuration.GetSection("Basil:Database"));
		KestrelSetup.Configure(builder);

		var app = builder.Build();
		app.Services.GetRequiredService<RuntimeSampler>().LogsDropped = () => drops.Dropped;
		app.UseRequestMetrics();
		app.UseTrustedProxies();
		app.UseRestImages();
		app.UseBancho();
		app.UseRest();
		return app;
	}
}