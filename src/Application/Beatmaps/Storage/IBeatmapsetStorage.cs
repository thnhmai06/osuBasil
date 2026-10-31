using Basil.Domain.Beatmaps;
using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Storage;

/// <summary>
///     Stores each beatmapset in its own folder, named <c>{id} {Artist} - {Title}</c> as osu! names its songs: the
///     set's archive, which is the truth about the set, and the files made from it.
/// </summary>
/// <remarks>
///     A set's folder holds its archive as it was given, the archive without video, and the files the server uses,
///     each kind in its own place (<see cref="BeatmapsetFileType" />). Anyone may also place an .osz archive or a folder
///     of one beatmapset's files in the storage by hand; such sources wait, listed by
///     <see cref="ListAsync(CancellationToken)" />, until the server takes them in. Other files placed there are not
///     sources.
/// </remarks>
public interface IBeatmapsetStorage
{
	/// <summary>Stores an uploaded archive, byte for byte, as an archive waiting to be taken in.</summary>
	/// <param name="archive">The .osz archive of one beatmapset, read from its current position to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The stored archive, which becomes a set's archive through
	///     <see cref="SaveAsync(BeatmapsetRecord, BeatmapsetArchive, CancellationToken)" />.
	/// </returns>
	/// <remarks>
	///     It is neither listed nor watched. One that is not taken in is removed by
	///     <see cref="DeleteAsync(BeatmapsetArchive, CancellationToken)" />, or when the server starts again.
	/// </remarks>
	Task<BeatmapsetArchive> SaveAsync(Stream archive, CancellationToken cancellationToken = default);

	/// <summary>Makes an archive lying in the storage the set's archive, moving it byte for byte.</summary>
	/// <param name="set">The beatmapset the archive holds.</param>
	/// <param name="archive">The archive: one placed by hand, uploaded, packed from a folder, or a set folder's own.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     The set's folder is named after the set, and another folder stored for the set is deleted. A set folder's own
	///     archive stays in place; the folder is only renamed when its name changed.
	/// </remarks>
	Task SaveAsync(BeatmapsetRecord set, BeatmapsetArchive archive, CancellationToken cancellationToken = default);

	/// <summary>Packs a folder of loose files into an archive that waits to be taken in.</summary>
	/// <param name="folder">The folder; its files are left as they are until the archive is taken in.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The packed archive, or <see langword="null" /> when the folder is gone, holds no files, or is a set's folder that
	///     lost its archive (it holds files made from one), which is never packed.
	/// </returns>
	/// <remarks>
	///     The loose files are removed once the packed archive is taken in by
	///     <see cref="SaveAsync(BeatmapsetRecord, BeatmapsetArchive, CancellationToken)" />.
	/// </remarks>
	Task<BeatmapsetArchive?> PackAsync(BeatmapsetFolder folder,
		CancellationToken cancellationToken = default);

	/// <summary>Removes every file made from a set's archive, keeping the archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task ClearAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default);

	/// <summary>Stores one file made from a set's archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="name">
	///     The plain file name to store it under, without folders: for a difficulty <c>{hash}.osu</c>; for an audio preview
	///     the name of the audio it is made from; ignored for the storyboard. A file with that name is replaced.
	/// </param>
	/// <param name="content">The content, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a plain file name.</exception>
	Task SaveAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name, Stream content,
		CancellationToken cancellationToken = default);

	/// <summary>Makes the set's archive without video from its archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="videoFiles">
	///     The names, as they are in the archive, of the files that the set's difficulties declare as their video; the
	///     letter case of a name does not matter.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveArchiveWithoutVideoAsync(BeatmapsetRecord set, IReadOnlyCollection<string> videoFiles,
		CancellationToken cancellationToken = default);

	/// <summary>Determines whether a file made from a set's archive is stored.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="name">The name it is stored under.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the file is stored.</returns>
	Task<bool> ExistsAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken cancellationToken = default);

	/// <summary>Opens one file made from a set's archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="type">What the file is.</param>
	/// <param name="name">The name it is stored under; ignored for the storyboard.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, seekable; or <see langword="null" /> when it is not stored. Nothing is made when asked.</returns>
	Task<Stream?> OpenAsync(BeatmapsetRecord set, BeatmapsetFileType type, string name,
		CancellationToken cancellationToken = default);

	/// <summary>Opens a set's archive, with or without video.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="withVideo">Whether to open the archive as it was given, with its videos.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, seekable; or <see langword="null" /> when it is not stored.</returns>
	Task<Stream?> OpenArchiveAsync(BeatmapsetRecord set, bool withVideo, CancellationToken cancellationToken = default);

	/// <summary>Lists every source in the storage: the archives and folders directly in it.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The sources; folders being written or deleted are left out.</returns>
	Task<IEnumerable<BeatmapsetSource>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Watches the storage for sources that change, whoever changes them.</summary>
	/// <param name="cancellationToken">A token that stops watching.</param>
	/// <returns>
	///     Each source that changed, as it is once its changes have settled; a change inside a folder (its archive above
	///     all) reports the folder, and a folder that is gone comes back with <see cref="BeatmapsetFolder.Exists" />
	///     false. Watching starts when this is called; folders being written or deleted are left out.
	/// </returns>
	IAsyncEnumerable<BeatmapsetSource> WatchAsync(CancellationToken cancellationToken);

	/// <summary>Deletes a set's folder with everything in it; deleting a missing set does nothing.</summary>
	/// <param name="set">The beatmapset whose folder to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The folder stops being the set's at once; its files are removed right after.</remarks>
	/// <exception cref="IOException">A file of the set is in use; the set keeps its folder.</exception>
	Task DeleteAsync(BeatmapsetRecord set, CancellationToken cancellationToken = default);

	/// <summary>Removes an uploaded or packed archive, once it is taken in or will not be; a packed folder's files stay.</summary>
	/// <param name="archive">
	///     An archive returned by <see cref="SaveAsync(Stream, CancellationToken)" /> or
	///     <see cref="PackAsync(BeatmapsetFolder, CancellationToken)" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="ArgumentException"><paramref name="archive" /> was neither uploaded nor packed by this storage.</exception>
	Task DeleteAsync(BeatmapsetArchive archive, CancellationToken cancellationToken = default);
}

/// <summary>What a file made from a beatmapset's archive is.</summary>
public enum BeatmapsetFileType : byte
{
	/// <summary>A difficulty's .osu file, named after the hash of its content.</summary>
	Version,

	/// <summary>A background image.</summary>
	Background,

	/// <summary>An audio track.</summary>
	Audio,

	/// <summary>A short preview of an audio track, named after it.</summary>
	Preview,

	/// <summary>A video.</summary>
	Video,

	/// <summary>The set's storyboard.</summary>
	Storyboard
}