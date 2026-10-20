using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Servers;
using Basil.Application.Objects.Sessions;
using Basil.Application.Storage.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Startup;

/// <summary>
///     Prepares the runtime before the server serves anyone: opens the general channels, brings BasilBot online and
///     closes matches left unfinished.
/// </summary>
internal sealed class RuntimeStartup(
	IChannelRepository channelRepository,
	Server server,
	ISessionService sessions,
	IMatchService matches,
	ILogger<RuntimeStartup> logger) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var openedChannels = 0;
		foreach (var channel in await channelRepository.ListAsync(cancellationToken))
			if (server.OpenChannel(channel) is not null)
				openedChannels++;
		logger.LogInformation("Opened {ChannelCount} general channels", openedChannels);

		if (await sessions.Connections.ConnectBotAsync(cancellationToken) is null)
			logger.LogWarning("BasilBot's record is missing; the bot stays offline");
		else
			logger.LogInformation("BasilBot is online");

		var closedMatches = await matches.CloseUnfinishedAsync(cancellationToken);
		logger.LogInformation("Closed {MatchCount} unfinished matches", closedMatches);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
