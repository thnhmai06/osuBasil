using Basil.Application.Servers.Objects;

namespace Basil.Host;

/// <summary>
///     Prepares the runtime before the server serves anyone: opens the general channels, makes sure BasilBot's account
///     exists, brings the bot online and closes matches left unfinished.
/// </summary>
internal sealed class RuntimeStartup(Server server, ILogger<RuntimeStartup> logger) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var recovery = await server.RecoverAsync(cancellationToken);
		logger.LogInformation("Opened {ChannelCount} general channels", recovery.OpenedChannels);
		if (recovery.BotOnline)
			logger.LogInformation("BasilBot is online");
		else
			logger.LogWarning("BasilBot could not be brought online");
		logger.LogInformation("Closed {MatchCount} unfinished matches", recovery.ClosedMatches);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}