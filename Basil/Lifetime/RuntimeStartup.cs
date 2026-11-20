using Basil.Application.Servers.Objects;

namespace Basil.Lifetime;

internal sealed class RuntimeStartup(Server server, ILogger<RuntimeStartup> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken ct)
	{
		var recovery = await server.RecoverAsync(ct);
		logger.LogInformation("Opened {ChannelCount} general channels", recovery.OpenedChannels);
		if (recovery.BotOnline)
			logger.LogInformation("BasilBot is online");
		else
			logger.LogWarning("BasilBot could not be brought online");
		logger.LogInformation("Closed {MatchCount} unfinished matches", recovery.ClosedMatches);
		logger.LogInformation("Closed {ConnectionCount} connections left open", recovery.ClosedConnections);
	}

	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}