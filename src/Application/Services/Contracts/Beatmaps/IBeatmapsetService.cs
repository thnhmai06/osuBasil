using Basil.Application.Services.Contracts.Events;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>Imports, deletes and keeps track of the beatmapsets the server has.</summary>
/// <remarks>
///     A set's archive in the beatmapset storage is the truth about the set; the server takes in what is placed there,
///     makes every file it serves from the archive when it indexes the set, and keeps its records in step with the
///     archives.
/// </remarks>
public interface IBeatmapsetService : IEventPublisher<BeatmapsetEvent>
{
	/// <summary>Imports every beatmapset found in an upload, adding or replacing the beatmapsets they belong to.</summary>
	/// <param name="files">
	///     The uploaded files: .osz archives of one beatmapset, .zip archives of several beatmapsets (as .osz archives or
	///     folders), or folders of beatmapsets.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of each beatmapset found, in the order they were found; empty when none was found.</returns>
	/// <remarks>
	///     <para>
	///         What each file holds decides what it is: an archive or folder with difficulties at its top is one
	///         beatmapset; any other archive or folder is looked through for beatmapsets.
	///     </para>
	///     <para>
	///         Every beatmapset that can be imported is stored at once, or none of them is. One that cannot be read,
	///         belongs to a locked beatmapset or repeats an earlier one of the upload is rejected; nothing of it is kept.
	///     </para>
	///     <para>
	///         A beatmapset is the stored one that already has a beatmap of it; otherwise the stored one with the id it
	///         declares; otherwise a new beatmapset, given the declared id when it has one. Difficulties the new version
	///         no longer has are removed.
	///     </para>
	/// </remarks>
	/// <exception cref="ArgumentException">A file's path is not a safe relative path.</exception>
	/// <exception cref="InvalidDataException">The upload unpacks to more files or bytes than one upload may hold.</exception>
	Task<IReadOnlyList<BeatmapsetImportResult>> ImportAsync(IReadOnlyList<BeatmapsetUploadFile> files,
		CancellationToken cancellationToken = default);

	/// <summary>Deletes a beatmapset with its beatmaps and files.</summary>
	/// <param name="set">The beatmapset to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the beatmapset was deleted; <see langword="false" /> when the beatmapset is locked.</returns>
	Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Takes in everything waiting in the storage and brings every beatmapset in step with its archive.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>
	///     First every entry is checked: an archive whose size and modification time match the stored ones is not hashed;
	///     any other is hashed, and one whose hash differs from the stored one, or that no stored set has, is indexed
	///     again, as are loose archives and folders of loose files. Then each of those is indexed and its files are made
	///     again, and every stored set whose folder or archive is gone, or whose archive cannot be read, is forgotten.
	///     Every change of the records is stored at once, after all of that. A set that needs no indexing but misses a
	///     file made from its archive gets that file made again. Run when the server starts, before it serves clients.
	/// </remarks>
	Task<BeatmapsetScanResult> ScanAsync(CancellationToken cancellationToken = default);

	/// <summary>Brings the server in step with one entry of the storage after it changed.</summary>
	/// <param name="entry">The entry as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>
	///     The entry's archive is hashed and indexed again when its hash differs from the stored one, even when the folder
	///     held no valid set before. A loose archive is moved into its set's folder; a folder of loose files is packed into
	///     an archive first. The beatmapset is the stored one that already has a beatmap of the archive; otherwise the
	///     stored one with the id the folder's name starts with; otherwise the stored one with the id the archive declares;
	///     otherwise a new one. A folder that is gone or whose archive is gone or unreadable forgets its set; its files
	///     are left as they are. A locked set is not changed. Scanning an unchanged entry again changes nothing.
	/// </remarks>
	Task<BeatmapsetScanResult> ScanAsync(BeatmapsetEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>How many beatmapsets a scan changed.</summary>
/// <param name="Imported">The beatmapsets taken in for the first time.</param>
/// <param name="Updated">The beatmapsets whose difficulties changed.</param>
/// <param name="Forgotten">The beatmapsets forgotten because nothing is stored for them.</param>
public sealed record BeatmapsetScanResult(int Imported, int Updated, int Forgotten);
