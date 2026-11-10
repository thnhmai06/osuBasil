using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Content.Persistence;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Gets the beatmapsets uploaded to the server, used without a rights check.</summary>
	public ServerBeatmapsets Beatmapsets => new(this);
}

/// <summary>The beatmapsets uploaded to the server, used without a rights check.</summary>
public readonly struct ServerBeatmapsets
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerBeatmapsets(Server server)
	{
		_server = server;
	}

	/// <summary>Tells whether an area of the server is locked.</summary>
	/// <param name="area">The area.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the area is locked.</returns>
	internal async Task<bool> IsLockedAsync(Locks area, CancellationToken ct)
	{
		return (await _server.Context.Settings.GetAsync(ct)).Locks.HasFlag(area);
	}

	/// <summary>Imports an uploaded beatmapset archive as a new beatmapset.</summary>
	/// <param name="archive">The .osz archive, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>TooLarge when the archive exceeds the upload limit; otherwise Ok and the result of the import.</returns>
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