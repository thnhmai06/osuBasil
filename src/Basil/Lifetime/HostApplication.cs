using Basil.Configuration;
using Basil.Documentation;
using Basil.Host.Cli;
using Basil.Host.Cli.Socket;
using Basil.Host.Irc;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Images;
using Basil.Host.Shared.Documentation;
using Basil.Host.Stable;
using Basil.Host.Stable.Routes;
using Basil.Http;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Bot;
using Basil.Infrastructure.Shared;
using Basil.Infrastructure.Shared.Storage;
using Basil.Infrastructure.Diagnostics.Services;
using Basil.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp.Web.Caching;

namespace Basil.Lifetime;

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
		// Relative paths in the settings, such as the certificate of Kestrel, are relative to the executable.
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			Args = args, ContentRootPath = AppContext.BaseDirectory
		});
		ConfigurationSetup.Add(builder, args);
		var drops = ServerLogging.Add(builder, mode);
		TelemetrySetup.Add(builder);
		builder.Services.AddOptions<UpdateOptions>().Bind(builder.Configuration.GetSection("Basil:Updates"));
		builder.Services.AddSingleton<ConfigurationWatcher>();
		builder.Services.AddHostedService(provider => provider.GetRequiredService<ConfigurationWatcher>());
		builder.Services.AddHostedService<LanAdvertiser>();
		builder.Services.AddHostedService<UpdateChecker>();
		builder.Services.AddBasil();
		builder.Services.AddBotInfrastructureOptions(builder.Configuration);
		builder.Services.AddSingleton<IDocumentPage, BotReferencePage>();
		builder.Services.AddIrcListener(builder.Configuration);
		builder.Services.AddRestApi(builder.Configuration);
		builder.Services.AddRestImages();
		builder.Services.AddOptions<PhysicalFileSystemCacheOptions>()
			.Configure<IOptions<StorageOptions>>((cache, storage) =>
			{
				cache.CacheRootPath = Path.Combine(storage.Value.Location, "Cache");
				cache.CacheFolder = "imagesharp";
			});
		builder.Services.AddCli(mode,
			provider => ControlSocket.PathIn(
				provider.GetRequiredService<IOptions<StorageOptions>>().Value.Location),
			Path.Combine(AppContext.BaseDirectory, "Configurations"));
		builder.Services.AddSingleton<ShutdownSequence>();
		// Registered last, so that it stops first: the other services are told and the rooms closed while they still run.
		builder.Services.AddHostedService<GracefulShutdown>();
		builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimings.Host);
		configure?.Invoke(builder);

		// Read once, after the caller's changes, so a later edit of the files waits for the next start.
		ConfigurationSetup.AddAtStart<ServerOptions>(builder.Services,
			builder.Configuration.GetSection("Basil:Server"));
		builder.Services.AddSharedInfrastructureOptions(builder.Configuration);
		builder.Services.AddBeatmapsInfrastructureOptions(builder.Configuration);
		KestrelSetup.Configure(builder);

		var app = builder.Build();
		// Before any ruleset type is used: a staged version replaces the one shipped with Basil.
		app.Services.GetRequiredService<RulesetStore>().LoadStaged();
		app.Services.GetRequiredService<RuntimeSampler>().LogsDropped = () => drops.Dropped;
		app.UseRequestMetrics();
		app.UseTrustedProxies();
		if (KestrelSetup.UsesHttps(app.Configuration))
			app.UseHsts();
		if (KestrelSetup.UsesPublishedCertificate(app.Configuration))
			app.Logger.LogWarning(
				"HTTPS uses the certificate that ships with Basil, whose private key and password are public. " +
				"Replace it with your own certificate before exposing the server");
		app.UseRestImages();
		app.UseStable();
		app.UseRest();
		return app;
	}
}