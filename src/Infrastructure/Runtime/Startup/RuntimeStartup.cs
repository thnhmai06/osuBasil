using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Infrastructure.Runtime.Beatmaps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Runtime.Startup;

internal sealed class RuntimeStartup(
	IChannelRepository channelRepository,
	IChannelService channels,
	IMatchService matches,
	IBeatmapsetService beatmapsets,
	IOptions<BeatmapImportOptions> options,
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

		var scannedBeatmapsets = await beatmapsets.ScanAsync(cancellationToken);
		logger.LogInformation("Forgot {BeatmapsetCount} beatmapsets whose archive is gone.", scannedBeatmapsets);

		var directory = options.Value.Directory;
		Directory.CreateDirectory(directory);
		var imported = 0;
		foreach (var path in Directory.EnumerateFiles(directory, "*.osz", SearchOption.TopDirectoryOnly))
		{
			var status = await BeatmapArchiveImporter.ImportAsync(path, beatmapsets, logger, cancellationToken);
			if (status == BeatmapArchiveImporter.ImportStatus.Imported) imported++;
		}
		logger.LogInformation("Imported {ArchiveCount} waiting beatmap archives.", imported);
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
