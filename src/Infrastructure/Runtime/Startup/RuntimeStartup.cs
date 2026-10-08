using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Chat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Startup;

/// <summary>Prepares the runtime before the server serves anyone: opens the general channels and closes matches left unfinished.</summary>
internal sealed class RuntimeStartup(
	IChannelRepository channelRepository,
	IChannelService channels,
	IMatchService matches,
	ILogger<RuntimeStartup> logger) : IHostedService
{
	/// <inheritdoc />
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var openedChannels = 0;
		foreach (var channel in await channelRepository.ListAsync(cancellationToken))
			if (channels.Open(channel) is not null) openedChannels++;
		logger.LogInformation("Opened {ChannelCount} general channels.", openedChannels);

		var closedMatches = await matches.CloseUnfinishedAsync(cancellationToken);
		logger.LogInformation("Closed {MatchCount} unfinished matches.", closedMatches);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
