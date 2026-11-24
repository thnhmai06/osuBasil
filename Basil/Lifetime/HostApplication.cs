using Basil.Configuration;
using Basil.Documentation;
using Basil.Host.Cli;
using Basil.Host.Cli.Socket;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Images;
using Basil.Host.Shared.Documentation;
using Basil.Host.Shared.Info;
using Basil.Host.Stable.Routes;
using Basil.Http;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Rulesets.Storage;
using Basil.Infrastructure.Bot;
using Basil.Infrastructure.Diagnostics.Services;
using Basil.Infrastructure.Shared;
using Basil.Infrastructure.Shared.Storage;
using Basil.Logging;

namespace Basil.Lifetime;

public static class HostApplication
{
	public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
	{
		var mode = CliModes.Choose(args, Console.IsInputRedirected, Console.IsOutputRedirected,
			CliModes.TermIsDumb(), CliModes.NoColorIsSet());
		args = CliModes.Strip(args);
		// Before the settings are read: the shipped files are in place, and the layout is the one this program knows.
		ShippedFiles.CopyMissing();
		DataLayout.Ensure();
		// Relative paths in the settings are relative to the executable.
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
		builder.Services.AddHttpClient();
		builder.Services.AddSingleton<UpdateChecker>();
		builder.Services.AddHostedService(provider => provider.GetRequiredService<UpdateChecker>());
		builder.Services.AddSingleton<IUpdateStatus>(provider => provider.GetRequiredService<UpdateChecker>());
		builder.Services.AddBasil();
		builder.Services.AddSingleton<IDocumentPage, BotReferencePage>();
		builder.Services.AddIrcListener(builder.Configuration);
		builder.Services.AddRestApi(builder.Configuration);
		builder.Services.AddCli(mode,
			provider => ControlSocket.PathIn(
				provider.GetRequiredService<DataPaths>().Temp),
			ConfigurationSetup.Directory);
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
		app.UseRestImages();
		app.UseStable();
		app.UseRest();
		return app;
	}
}