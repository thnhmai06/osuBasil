using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>
///     Stores each beatmapset in its own folder, named <c>{id} {Artist} - {Title}</c> as osu! names its songs: the
///     set's archive, which is the truth about the set, and the files made from it.
/// </summary>
/// <remarks>
///     A set's folder holds its archive as it was given, the archive without video, and the files the server uses,
///     each kind in its own place (<see cref="BeatmapsetFileKind" />). Anyone may also place an .osz archive or a
///     folder of loose files in the storage by hand; such entries wait, listed by
///     <see cref="ListAsync(CancellationToken)" />, until the server takes them in.
/// </remarks>
public interface IBeatmapsetStorage
{
	/// <summary>Stores an upload apart from the stored beatmapsets and finds the beatmapsets in it.</summary>
	/// <param name="files">The uploaded files; uploading a folder uploads every file in it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The upload, whose archives become sets' archives through
	///     <see cref="SaveAsync(Beatmapset, BeatmapsetArchiveEntry, CancellationToken)" />.
	/// </returns>
	/// <remarks>
	///     An upload is neither listed nor watched. What is not taken in is removed by
	///     <see cref="DeleteAsync(BeatmapsetUpload, CancellationToken)" />, or when the server starts again.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     A path is empty, rooted, leads out of the upload or holds a character a file name may not have.
	/// </exception>
	/// <exception cref="InvalidDataException">The upload unpacks to more files or bytes than one upload may hold.</exception>
	Task<BeatmapsetUpload> SaveAsync(IReadOnlyList<BeatmapsetUploadFile> files,
		CancellationToken cancellationToken = default);

	/// <summary>Makes an archive lying in the storage the set's archive, moving it byte for byte.</summary>
	/// <param name="set">The beatmapset the archive holds.</param>
	/// <param name="archive">The archive: one placed by hand, one packed from loose files, one of an upload, or a set folder's own.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     The set's folder is named after the set, and another folder stored for the set is deleted. A set folder's own
	///     archive stays in place; the folder is only renamed when its name changed.
	/// </remarks>
	Task SaveAsync(Beatmapset set, BeatmapsetArchiveEntry archive, CancellationToken cancellationToken = default);

	/// <summary>Packs a folder of loose files into an archive that waits to be taken in.</summary>
	/// <param name="folder">The folder; its files are left as they are until the archive is taken in.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The packed archive, or <see langword="null" /> when the folder is gone, holds no files, or is a set's folder that
	///     lost its archive (it holds files made from one), which is never packed.
	/// </returns>
	/// <remarks>The loose files are removed once the packed archive is taken in by <see cref="SaveAsync(Beatmapset, BeatmapsetArchiveEntry, CancellationToken)" />.</remarks>
	Task<BeatmapsetArchiveEntry?> PackAsync(BeatmapsetFolderEntry folder,
		CancellationToken cancellationToken = default);

	/// <summary>Removes every file made from a set's archive, keeping the archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task ClearAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Stores one file made from a set's archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="kind">What the file is.</param>
	/// <param name="name">
	///     The plain file name to store it under, without folders: for a difficulty <c>{hash}.osu</c>; for an audio preview
	///     the name of the audio it is made from; ignored for the storyboard. A file with that name is replaced.
	/// </param>
	/// <param name="content">The content, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is not a plain file name.</exception>
	Task SaveAsync(Beatmapset set, BeatmapsetFileKind kind, string name, Stream content,
		CancellationToken cancellationToken = default);

	/// <summary>Makes the set's archive without video from its archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SaveArchiveWithoutVideoAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Determines whether a file made from a set's archive is stored.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="kind">What the file is.</param>
	/// <param name="name">The name it is stored under.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the file is stored.</returns>
	Task<bool> ExistsAsync(Beatmapset set, BeatmapsetFileKind kind, string name,
		CancellationToken cancellationToken = default);

	/// <summary>Opens one file made from a set's archive.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="kind">What the file is.</param>
	/// <param name="name">The name it is stored under; ignored for the storyboard.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The file's content, seekable; or <see langword="null" /> when it is not stored. Nothing is made when asked.</returns>
	Task<Stream?> OpenAsync(Beatmapset set, BeatmapsetFileKind kind, string name,
		CancellationToken cancellationToken = default);

	/// <summary>Opens a set's archive, with or without video.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="withVideo">Whether to open the archive as it was given, with its videos.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, seekable; or <see langword="null" /> when it is not stored.</returns>
	Task<Stream?> OpenArchiveAsync(Beatmapset set, bool withVideo, CancellationToken cancellationToken = default);

	/// <summary>Lists every entry in the storage: loose archives and every folder directly in it.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The entries; folders being written or deleted are left out.</returns>
	Task<IEnumerable<BeatmapsetEntry>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Watches the storage for entries that change, whoever changes them.</summary>
	/// <param name="cancellationToken">A token that stops watching.</param>
	/// <returns>
	///     Each entry that changed, as it is once its changes have settled; a change inside a folder (its archive above
	///     all) reports the folder, and a folder that is gone comes back with <see cref="BeatmapsetFolderEntry.Exists" />
	///     false. Watching starts when this is called; folders being written or deleted are left out.
	/// </returns>
	IAsyncEnumerable<BeatmapsetEntry> WatchAsync(CancellationToken cancellationToken);

	/// <summary>Deletes a set's folder with everything in it; deleting a missing set does nothing.</summary>
	/// <param name="set">The beatmapset whose folder to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The folder stops being the set's at once; its files are removed right after.</remarks>
	/// <exception cref="IOException">A file of the set is in use; the set keeps its folder.</exception>
	Task DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default);

	/// <summary>Removes what is left of an upload once its beatmapsets are taken in or rejected.</summary>
	/// <param name="upload">The upload.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(BeatmapsetUpload upload, CancellationToken cancellationToken = default);

	/// <summary>Removes an archive packed from loose files, once it is taken in or will not be; the loose files stay.</summary>
	/// <param name="packed">An archive returned by <see cref="PackAsync(BeatmapsetFolderEntry, CancellationToken)" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <exception cref="ArgumentException"><paramref name="packed" /> was not packed by this storage.</exception>
	Task DeleteAsync(BeatmapsetArchiveEntry packed, CancellationToken cancellationToken = default);
}

/// <summary>What a file made from a beatmapset's archive is.</summary>
public enum BeatmapsetFileKind : byte
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