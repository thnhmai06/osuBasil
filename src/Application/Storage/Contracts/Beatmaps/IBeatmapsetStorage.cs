using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>Stores the files of beatmapsets and offers each set as an .osz archive.</summary>
public interface IBeatmapsetStorage
{
	/// <summary>Stores a set's files from an .osz archive, replacing the files stored for it.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="archive">The archive to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="InvalidDataException">The archive is not a readable .osz, names a file outside the set, or is too large.</exception>
	Task SaveAsync(Beatmapset set, Stream archive, CancellationToken cancellationToken = default);

	/// <summary>Lists the files of a set.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The files' names relative to the set, with '/' between folders; empty when nothing is stored for the set.</returns>
	Task<IEnumerable<string>> ListAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Opens one file of a set.</summary>
	/// <param name="set">The beatmapset whose file to open.</param>
	/// <param name="name">The file's name relative to the set; case and '/' or '\\' do not matter.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, seekable; or <see langword="null" /> when the set has no such file.</returns>
	Task<Stream?> OpenAsync(Beatmapset set, string name, CancellationToken cancellationToken = default);

	/// <summary>Opens a set as an .osz archive.</summary>
	/// <param name="set">The beatmapset to open.</param>
	/// <param name="withVideo">Whether the archive includes the set's videos.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, seekable; or <see langword="null" /> when nothing is stored for the set.</returns>
	Task<Stream?> OpenArchiveAsync(Beatmapset set, bool withVideo, CancellationToken cancellationToken = default);

	/// <summary>Deletes a set's files and archives; deleting a missing set does nothing.</summary>
	/// <param name="set">The beatmapset the archive belongs to.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);
}
