using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Users;
using Basil.Application.Persistence.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Startup;

/// <summary>
///     Prepares the runtime before the server serves anyone: opens the general channels, makes sure BasilBot's account
///     exists, brings the bot online and closes matches left unfinished.
/// </summary>
internal sealed class RuntimeStartup(
	IChannelRepository channelRepository,
	Server server,
	UserRegistry registry,
	Lobby lobby,
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

		await registry.EnsureBotAsync(cancellationToken);
		if (await registry.ConnectBotAsync(cancellationToken) is null)
			logger.LogWarning("BasilBot could not be brought online");
		else
			logger.LogInformation("BasilBot is online");

		var closedMatches = await lobby.CloseUnfinishedAsync(cancellationToken);
		logger.LogInformation("Closed {MatchCount} unfinished matches", closedMatches);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}