using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Content.Persistence;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Tells whether an area of the server is locked.</summary>
	/// <param name="area">The area.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the area is locked.</returns>
	internal async Task<bool> IsLockedAsync(Locks area, CancellationToken ct)
	{
		return (await Context.Settings.GetAsync(ct)).Locks.HasFlag(area);
	}

	/// <summary>Imports an uploaded beatmapset archive as a new beatmapset.</summary>
	/// <param name="archive">The .osz archive, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>TooLarge when the archive exceeds the upload limit; otherwise Ok and the result of the import.</returns>
	internal async Task<(ServerOutcome Outcome, BeatmapsetImportResult? Import)> UploadBeatmapsetAsync(Stream archive,
		CancellationToken ct)
	{
		var limit = (await Context.Settings.GetAsync(ct)).Uploads.OszMaxSize;
		try
		{
			return (ServerOutcome.Ok,
				await Context.Beatmapsets.ImportAsync(new CappedStream(archive, limit), false, ct));
		}
		catch (ArchiveTooLargeException)
		{
			return (ServerOutcome.TooLarge, null);
		}
	}
}