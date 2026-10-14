namespace Basil.Application.Storage.Contracts.Beatmaps;

/// <summary>One file of an upload: a file chosen alone, or a file inside an uploaded folder.</summary>
/// <param name="Path">
///     The file's path within the upload, its folders separated by <c>/</c>: its name for a file chosen alone, the
///     folder's name followed by the path inside it for a file of a folder.
/// </param>
/// <param name="Content">The file's content, read from its current position to its end; not disposed.</param>
public sealed record BeatmapsetUploadFile(string Path, Stream Content);

/// <summary>An upload held by the storage while its beatmapsets are taken in: the archive of each beatmapset found in it.</summary>
/// <remarks>
///     An .osz or .zip archive with difficulties at its top is one beatmapset; a folder with difficulties directly in it
///     is one beatmapset, packed into an archive; any other archive is unpacked and looked through like a folder. An
///     archive that cannot be opened is kept as one beatmapset, which then cannot be read. A difficulty uploaded on its
///     own, outside any archive or folder, belongs to no beatmapset and is rejected.
/// </remarks>
public abstract class BeatmapsetUpload
{
	/// <summary>Gets the archive of each beatmapset found in the upload, each named by its path within the upload.</summary>
	public abstract IReadOnlyList<BeatmapsetArchiveEntry> Archives { get; }

	/// <summary>Gets the path within the upload of each difficulty uploaded on its own, which belongs to no beatmapset.</summary>
	public abstract IReadOnlyList<string> Rejected { get; }
}
