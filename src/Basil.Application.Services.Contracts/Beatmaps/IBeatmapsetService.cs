using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>Imports, deletes and keeps track of the beatmapsets the server has.</summary>
public interface IBeatmapsetService : IEventPublisher<BeatmapsetEvent>
{
	/// <summary>Imports a beatmapset archive, adding or replacing the beatmapset it belongs to.</summary>
	/// <param name="by">
	///     The connection uploading it: a user with <see cref="Permissions.TournamentManageBeatmaps" />, or, while uploads
	///     are not locked (<see cref="Basil.Domain.Content.CreationLocks.Beatmapsets" />), a user with
	///     <see cref="Permissions.PlayerUploadBeatmapsets" />, who may only add new beatmapsets.
	/// </param>
	/// <param name="archive">The archive to import.</param>
	/// <param name="beatmapsetId">
	///     The beatmapset id the archive is known under, for example from its file name, or
	///     <see langword="null" /> when unknown.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import.</returns>
	/// <remarks>
	///     The beatmapset is the one that already has a beatmap of the archive; otherwise the one with
	///     <paramref name="beatmapsetId" /> when it exists; otherwise the id the archive declares;
	///     otherwise a new local id of at least 1 000 000 000. Difficulties the new version no longer has are removed.
	///     A locked beatmapset is not changed.
	/// </remarks>
	Task<BeatmapsetImportResult> ImportAsync(Connection by, Stream archive, int? beatmapsetId = null,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset with its beatmaps and archive.</summary>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the beatmapset was deleted; <see langword="false" /> when the beatmapset is locked.</returns>
	Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Forgets every beatmapset whose archive is no longer stored.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were forgotten.</returns>
	/// <remarks>Run by the host when it starts.</remarks>
	Task<int> ScanAsync(CancellationToken cancellationToken = default);
}