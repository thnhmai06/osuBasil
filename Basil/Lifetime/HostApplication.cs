using Basil.Gateway.Limits;
using Basil.Configuration;
using Basil.Documentation;
using Basil.Host.Cli;
using Basil.Host.Cli.Socket;
using Basil.Host.Irc.Transport;
using Basil.Host.Rest;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Images;
using Basil.Host.Shared.Documentation;
using Basil.Infrastructure.Servers;
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
	// A one-off command logs to standard error, so its standard output holds only its result.
	public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null,
		bool logToStandardError = false)
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
		var drops = ServerLogging.Add(builder, mode, logToStandardError);
		TelemetrySetup.Add(builder);
		builder.Services.AddSingleton<ConfigurationWatcher>();
		builder.Services.AddHostedService(provider => provider.GetRequiredService<ConfigurationWatcher>());
		builder.Services.AddHttpClient();
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
		builder.Services.AddServersInfrastructureOptions(builder.Configuration);
		KestrelSetup.Configure(builder);

		var app = builder.Build();
		// Before any osu! type is used: the calculation stack is checked and made loadable from Data/Rulesets.
		app.Services.GetRequiredService<RulesetStore>().Initialize();
		var sampler = app.Services.GetRequiredService<RuntimeSampler>();
		sampler.LogsDropped = () => drops.Dropped;
		var load = app.Services.GetRequiredService<LoadSignal>();
		sampler.LoadMeasured = used => load.Report(used, 1);
		app.UseRequestMetrics();
		app.UseTrustedProxies();
		app.UseRestImages();
		app.UseStable();
		app.UseRest();
		return app;
	}
}