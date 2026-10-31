using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Objects;

/// <summary>Imports and keeps track of the beatmapsets the server has.</summary>
/// <remarks>
///     A set's archive in the beatmapset storage is the truth about the set; the server takes in what is placed there,
///     makes every file it serves from the archive when it indexes the set, and keeps its records in step with the
///     archives.
/// </remarks>
public sealed class Beatmapsets
{
	private readonly BeatmapsetContext _context;
	private readonly BeatmapsetImporter _importer;
	private readonly BeatmapsetScanner _scanner;

	internal Beatmapsets(BeatmapsetContext context, BeatmapsetImporter importer, BeatmapsetScanner scanner)
	{
		_context = context;
		_importer = importer;
		_scanner = scanner;
	}

	/// <summary>Gets the beatmapset object through which a stored set's behaviour is reached.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <returns>The beatmapset.</returns>
	/// <remarks>This system operation needs no rights because the server itself calls it.</remarks>
	public Beatmapset Of(BeatmapsetRecord set)
	{
		return new Beatmapset(set, _context);
	}

	/// <summary>Imports an uploaded beatmapset archive, adding or replacing the beatmapset it belongs to.</summary>
	/// <param name="archive">The .osz archive of one beatmapset, read from its current position; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import.</returns>
	/// <remarks>
	///     <para>This system operation needs no rights because the server itself calls it.</para>
	///     <para>
	///         An archive that cannot be read, or that belongs to a locked beatmapset, is rejected and nothing of it is
	///         kept. The beatmapset and its beatmaps are stored at once, or not at all.
	///     </para>
	///     <para>
	///         The beatmapset is the stored one that already has a beatmap of the archive; otherwise the stored one with
	///         the id the archive declares; otherwise a new beatmapset, given the declared id when it has one.
	///         An archive that declares no beatmapset id, or an id that is zero or negative, is a new beatmapset with an
	///         id the server gives it. Difficulties the new version no longer has are removed.
	///     </para>
	/// </remarks>
	public Task<BeatmapsetImportResult> ImportAsync(Stream archive, CancellationToken cancellationToken = default)
	{
		return ImportAsync(archive, true, cancellationToken);
	}

	/// <summary>Imports an uploaded beatmapset archive.</summary>
	/// <param name="archive">The .osz archive of one beatmapset, read from its current position; not disposed.</param>
	/// <param name="replaceExisting">
	///     Whether an archive of a beatmapset the server already has replaces it; otherwise it is rejected with
	///     <see cref="BeatmapsetImportFailure.AlreadyExists" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import.</returns>
	/// <remarks>This system operation needs no rights; the caller that serves a user checks them.</remarks>
	public async Task<BeatmapsetImportResult> ImportAsync(Stream archive, bool replaceExisting,
		CancellationToken cancellationToken = default)
	{
		var (result, imported) = await _importer.ImportAsync(archive, replaceExisting, cancellationToken);
		if (imported is not null)
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));
		return result;
	}

	/// <summary>Takes in everything waiting in the storage and brings every beatmapset in step with its archive.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it.
	///     First every entry is checked: an archive whose size and modification time match the stored ones is not hashed;
	///     any other is hashed, and one whose hash differs from the stored one, or that no stored set has, is indexed
	///     again, as are loose archives and folders of loose files. Then each of those is indexed and its files are made
	///     again, and every stored set whose folder or archive is gone, or whose archive cannot be read, is forgotten.
	///     Every change of the records is stored at once, after all of that. A set that needs no indexing but misses a
	///     file made from its archive gets that file made again. Run when the server starts, before it serves clients.
	/// </remarks>
	public async Task<BeatmapsetScanResult> ScanAsync(CancellationToken cancellationToken = default)
	{
		var outcome = await _scanner.ScanAsync(cancellationToken);
		Publish(outcome);
		return outcome.Result;
	}

	/// <summary>Brings the server in step with one entry of the storage after it changed.</summary>
	/// <param name="entry">The entry as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>
	///     This system operation needs no rights because the server itself calls it.
	///     The entry's archive is hashed and indexed again when its hash differs from the stored one, even when the folder
	///     held no valid set before. A loose archive is moved into its set's folder; a folder of loose files is packed into
	///     an archive first. The beatmapset is the stored one that already has a beatmap of the archive; otherwise the
	///     stored one with the id the folder's name starts with; otherwise the stored one with the id the archive declares;
	///     otherwise a new one. A folder that is gone or whose archive is gone or unreadable forgets its set; its files
	///     are left as they are. A locked set is not changed. Scanning an unchanged entry again changes nothing.
	/// </remarks>
	public async Task<BeatmapsetScanResult> ScanAsync(BeatmapsetSource entry,
		CancellationToken cancellationToken = default)
	{
		var outcome = await _scanner.ScanAsync(entry, cancellationToken);
		Publish(outcome);
		return outcome.Result;
	}

	private void Publish(BeatmapsetScanOutcome outcome)
	{
		foreach (var imported in outcome.Imported)
			_context.Events.Publish(new BeatmapsetImported(imported.Set, imported.Beatmaps));

		foreach (var deleted in outcome.Deleted)
			_context.Events.Publish(new BeatmapsetDeleted(deleted));
	}
}