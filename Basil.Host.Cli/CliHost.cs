using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Sessions.Registry;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Shutdown;
using Basil.Host.Cli.Socket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;

namespace Basil.Host.Cli;

public static class CliHost
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddCliFeeds()
		{
			services.TryAddSingleton<ChatFeed>();
			return services;
		}

		public IServiceCollection AddCli(CliOptions mode,
			Func<IServiceProvider, string> socketPath,
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
			services.AddSingleton(new ThemeStore(configurationDirectory, mode.NoColor));
			services.AddSingleton<IThemeControl>(provider => provider.GetRequiredService<ThemeStore>());
			services.AddSingleton(provider => new ControlServer(provider.GetRequiredService<CommandRunner>(),
				socketPath(provider),
				provider.GetRequiredService<ILogger<ControlServer>>()));
			services.AddHostedService(provider => provider.GetRequiredService<ControlServer>());
			services.AddHostedService<ShutdownTicker>();
			services.AddSingleton(mode);
			services.AddSingleton<DashboardApp>(provider => new DashboardApp(provider,
				provider.GetRequiredService<DashboardModel>(),
				provider.GetRequiredService<DashboardSampler>(),
				provider.GetRequiredService<CommandRunner>(), provider.GetRequiredService<CommandSuggestions>(),
				provider.GetRequiredService<ShutdownController>(), provider.GetRequiredService<ThemeStore>(), mode,
				provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<DashboardApp>>()));
			services.AddHostedService<DisplayService>();
			return services;
		}
	}
}

internal sealed class LiveSuggestions(SessionRegistry registry, Lobby lobby, GeneralChannelRegistry channels,
	IThemeControl themes)
	: ISuggestionSource
{
	public IEnumerable<string> Ids(string resource)
	{
		return resource.ToLowerInvariant() switch
		{
			"users" => registry.Online.Values.Select(user => user.Record.Id.ToString()).Order(),
			"rooms" => lobby.Rooms.Keys.Order().Select(id => id.ToString()),
			"channels" => channels.All.Values.Select(channel => channel.Name).Order(),
			"theme" => themes.Names,
			_ => []
		};
	}
}

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

internal sealed class DisplayService(
	CliOptions setting,
	IServiceProvider services,
	ShutdownController shutdown,
	ILogger<DisplayService> logger,
	IHostApplicationLifetime lifetime) : IHostedService
{
	private readonly CancellationTokenSource _stopped = new();
	private Thread? _thread;

	public Task StartAsync(CancellationToken ct)
	{
		if (!setting.HasDashboard)
		{
			shutdown.Message += Log;
			return Task.CompletedTask;
		}

		var dashboard = services.GetRequiredService<DashboardApp>();
		// Any way the process ends (a forced exit or a signal too) gives the shell its screen back.
		AppDomain.CurrentDomain.ProcessExit += RestoreOnExit;
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
				RestoreTerminal();
				// Closing the dashboard window of a server that keeps running would leave nothing to control it.
				if (!_stopped.IsCancellationRequested) lifetime.StopApplication();
			}
		}) { IsBackground = true, Name = "dashboard" };
		_thread.Start();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken ct)
	{
		shutdown.Message -= Log;
		_stopped.Cancel();
		_thread?.Join(TimeSpan.FromSeconds(5));
		return Task.CompletedTask;
	}

	// Leaves the alternate screen and gives back the cursor, the mouse, pasting and the colours; safe to repeat.
	private const string RestoreSequence = "\e[?1049l\e[?25h\e[?1000l\e[?1002l\e[?1003l\e[?1006l\e[?2004l\e[0m";

	private static void RestoreOnExit(object? sender, EventArgs e)
	{
		RestoreTerminal();
	}

	private static void RestoreTerminal()
	{
		try
		{
			Console.Out.Write(RestoreSequence);
			Console.Out.Flush();
		}
		catch (IOException)
		{
			// The terminal is already gone; there is nothing to give back.
		}
	}

	private void Log(ShutdownMessage message)
	{
		if (message.Kind == ShutdownMessageKind.Warning) logger.LogWarning("{Message}", message.Text);
		else if (message.Kind == ShutdownMessageKind.Progress) logger.LogInformation("{Message}", message.Text);
	}
}