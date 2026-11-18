using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Shared.Settings;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	public ServerBeatmapsets Beatmapsets => new(this);
}

public readonly struct ServerBeatmapsets
{
	private readonly Server _server;

	internal ServerBeatmapsets(Server server)
	{
		_server = server;
	}

	internal async Task<bool> IsLockedAsync(Locks area, CancellationToken ct)
	{
		return (await _server.Context.Settings.GetAsync(ct)).Locks.HasFlag(area);
	}

	internal async Task<(ServerOutcome Outcome, BeatmapsetImportResult? Import)> UploadAsync(Stream archive,
		CancellationToken ct)
	{
		var limit = (await _server.Context.Settings.GetAsync(ct)).Uploads.OszMaxSize;
		try
		{
			return (ServerOutcome.Ok,
				await _server.Context.Beatmapsets.ImportAsync(new CappedStream(archive, limit), false, ct));
		}
		catch (ArchiveTooLargeException)
		{
			return (ServerOutcome.TooLarge, null);
		}
	}
}