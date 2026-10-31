using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Cli.Shutdown;
using Basil.Host.Cli.Socket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;

namespace Basil.Host.Cli;

/// <summary>Registers the console of the server: the commands, the control socket, the dashboard and the Ctrl+C rules.</summary>
public static class CliHost
{
	/// <summary>Adds what the event handlers of the console need, so a container that handles events is complete without the rest of it.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddCliFeeds(this IServiceCollection services)
	{
		services.TryAddSingleton<ChatFeed>();
		return services;
	}

	/// <summary>Adds the console.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="mode">How the server shows itself.</param>
	/// <param name="socketPath">Finds the path of the control socket once the options are bound.</param>
	/// <param name="configurationDirectory">The folder where the dashboard saves its theme.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddCli(this IServiceCollection services, CliMode mode, Func<IServiceProvider, string> socketPath,
		string configurationDirectory)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton(CommandCatalogFactory.Create());
		services.AddSingleton(new ViewFormatter(TimeZoneInfo.Local));
		services.AddSingleton<CommandRunner>();
		services.AddSingleton<ISuggestionSource, LiveSuggestions>();
		services.AddSingleton<CommandSuggestions>();
		services.AddSingleton(provider => new CtrlCMachine(provider.GetRequiredService<TimeProvider>(), mode));
		services.AddSingleton<ShutdownController>();
		services.AddSingleton<IServerControl>(provider => provider.GetRequiredService<ShutdownController>());
		services.AddSingleton<IHostLifetime, CliLifetime>();
		services.AddSingleton<LogBuffer>();
		services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<LogBuffer>());
		services.AddCliFeeds();
		services.AddSingleton(provider => new DashboardModel
		{
			Log = provider.GetRequiredService<LogBuffer>(), Chat = provider.GetRequiredService<ChatFeed>()
		});
		services.AddSingleton<DashboardSampler>();
		services.AddSingleton<DashboardRenderer>();
		services.AddSingleton(new ThemeStore(configurationDirectory));
		services.AddSingleton<IThemeControl>(provider => provider.GetRequiredService<ThemeStore>());
		services.AddSingleton(provider => new ControlServer(provider.GetRequiredService<CommandRunner>(), socketPath(provider),
			provider.GetRequiredService<ILogger<ControlServer>>()));
		services.AddHostedService(provider => provider.GetRequiredService<ControlServer>());
		services.AddHostedService<ShutdownTicker>();
		services.AddSingleton(new CliModeSetting(mode));
		services.AddSingleton<DashboardApp>(provider => new DashboardApp(provider.GetRequiredService<DashboardModel>(),
			provider.GetRequiredService<DashboardRenderer>(), provider.GetRequiredService<DashboardSampler>(),
			provider.GetRequiredService<CommandRunner>(), provider.GetRequiredService<CommandSuggestions>(),
			provider.GetRequiredService<ShutdownController>(), provider.GetRequiredService<ThemeStore>(), mode,
			provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<DashboardApp>>()));
		services.AddHostedService<DisplayService>();
		return services;
	}
}

/// <summary>The display mode the server runs in.</summary>
/// <param name="Mode">The mode.</param>
public sealed record CliModeSetting(CliMode Mode);

/// <summary>Lists the ids that exist, so the command box can complete them.</summary>
internal sealed class LiveSuggestions(UserRegistry registry, Lobby lobby, GeneralChannelRegistry channels) : ISuggestionSource
{
	public IEnumerable<string> Ids(string resource)
	{
		return resource.ToLowerInvariant() switch
		{
			"users" => registry.Online.Values.Select(user => user.Record.Id.ToString()).Order(),
			"rooms" => lobby.Rooms.Keys.Order().Select(id => id.ToString()),
			"channels" => channels.All.Values.Select(channel => channel.Name).Order(),
			_ => []
		};
	}
}

/// <summary>Lets time pass for the Ctrl+C rules once a second.</summary>
internal sealed class ShutdownTicker(ShutdownController controller, TimeProvider clock) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken)) controller.Tick();
		}
		catch (OperationCanceledException)
		{
		}
	}
}

/// <summary>
///     Shows the server in the terminal: the dashboard in the full and no-console modes, and the messages of the Ctrl+C
///     rules as plain log lines in the no-gui mode.
/// </summary>
internal sealed class DisplayService(CliModeSetting setting, IServiceProvider services, ShutdownController shutdown,
	ILogger<DisplayService> logger, IHostApplicationLifetime lifetime) : IHostedService
{
	private readonly CancellationTokenSource _stopped = new();
	private Thread? _thread;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (setting.Mode == CliMode.NoGui)
		{
			shutdown.Message += Log;
			return Task.CompletedTask;
		}

		var dashboard = services.GetRequiredService<DashboardApp>();
		_thread = new Thread(() =>
		{
			try
			{
				dashboard.Run(Terminal.Instance, _stopped.Token);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "The dashboard stopped");
			}
			finally
			{
				// Closing the dashboard window of a server that keeps running would leave nothing to control it.
				if (!_stopped.IsCancellationRequested) lifetime.StopApplication();
			}
		}) { IsBackground = true, Name = "dashboard" };
		_thread.Start();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		shutdown.Message -= Log;
		_stopped.Cancel();
		_thread?.Join(TimeSpan.FromSeconds(5));
		return Task.CompletedTask;
	}

	private void Log(ShutdownMessage message)
	{
		if (message.Kind == ShutdownMessageKind.Warning) logger.LogWarning("{Message}", message.Text);
		else if (message.Kind == ShutdownMessageKind.Progress) logger.LogInformation("{Message}", message.Text);
	}
}
