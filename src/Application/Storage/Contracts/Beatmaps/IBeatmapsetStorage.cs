using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Stores the files of each beatmapset in its own folder and exports a set as an .osz archive.</summary>
public interface IBeatmapsetStorage
{
	/// <summary>Stores a set's files, replacing every file stored for the set.</summary>
	/// <param name="set">The beatmapset the files belong to.</param>
	/// <param name="files">The set's files; each must stay readable until the call returns.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>Either every file is stored or the set keeps the files it had.</remarks>
	/// <exception cref="InvalidDataException">
	///     A file names a place outside the set, or its content does not have the size the file declares.
	/// </exception>
	Task SaveAsync(Beatmapset set, IReadOnlyCollection<BeatmapsetFile> files,
		CancellationToken cancellationToken = default);

	/// <summary>Lists the files of a set.</summary>
	/// <param name="set">The beatmapset whose files to list.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The files' names relative to the set, with '/' between folders; empty when nothing is stored for the set.</returns>
	Task<IEnumerable<string>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Opens one file of a set.</summary>
	/// <param name="set">The beatmapset whose file to open.</param>
	/// <param name="name">The file's name relative to the set; case and '/' or '\\' do not matter.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, seekable; or <see langword="null" /> when the set has no such file.</returns>
	Task<Stream?> OpenAsync(Beatmapset set, string name, CancellationToken cancellationToken = default);

	/// <summary>Exports a set's stored files as an .osz archive.</summary>
	/// <param name="set">The beatmapset to open.</param>
	/// <param name="withVideo">Whether the archive includes the set's videos.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, seekable; or <see langword="null" /> when nothing is stored for the set.</returns>
	Task<Stream?> ExportAsync(Beatmapset set, bool withVideo, CancellationToken cancellationToken = default);

	/// <summary>Deletes a set's files; deleting a missing set does nothing.</summary>
	/// <param name="set">The beatmapset whose files to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}