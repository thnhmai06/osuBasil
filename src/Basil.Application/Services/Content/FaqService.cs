using Basil.Application.Configurations;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Stores FAQ entries as text files on disk.
/// </summary>
/// <remarks>
///     Every consumer reads the same <c>StorageOptions.FaqsPath</c> folder of <c>.txt</c> files
///     through this one implementation rather than duplicating the file logic.
/// </remarks>
public sealed class FaqService(IOptions<StorageOptions> storage)
{
	/// <summary>
	///     Identifies the outcome of a FAQ entry creation.
	/// </summary>
	public enum CreateResult : byte
	{
		/// <summary>The entry was created.</summary>
		Created,

		/// <summary>An entry with the same name already exists.</summary>
		AlreadyExists,

		/// <summary>The entry name is not a safe file name.</summary>
		InvalidName
	}

	/// <summary>
	///     Identifies the outcome of a FAQ entry replacement.
	/// </summary>
	public enum ReplaceResult : byte
	{
		/// <summary>The entry was replaced.</summary>
		Replaced,

		/// <summary>No entry with the given name exists.</summary>
		NotFound,

		/// <summary>The entry name is not a safe file name.</summary>
		InvalidName
	}

	/// <summary>
	///     Lists the names of all stored FAQ entries, including ones nested in subdirectories.
	/// </summary>
	/// <returns>
	///     The entry names without their <c>.txt</c> extension, ordered case-insensitively, or an empty list when the
	///     folder does not exist. A nested entry's name joins its directory segments with <c>:</c> (e.g.
	///     <c>folder1:folder2:file3</c> for <c>folder1/folder2/file3.txt</c> on disk), the same
	///     separator <see cref="ReadEntryAsync" /> and the other entry-name parameters accept.
	/// </returns>
	public IReadOnlyList<string> ListEntries()
	{
		if (!Directory.Exists(storage.Value.FaqsPath)) return [];

		return
		[
			.. Directory.EnumerateFiles(storage.Value.FaqsPath, "*.txt", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(storage.Value.FaqsPath, path))
				.Select(relative => Path.ChangeExtension(relative, null).Replace(Path.DirectorySeparatorChar, ':'))
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		];
	}

	/// <summary>
	///     Reads a single FAQ entry.
	/// </summary>
	/// <param name="entry">The entry name without its <c>.txt</c> extension.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The entry content, or <see langword="null" /> when the entry does not exist.</returns>
	/// <remarks>
	///     The returned text joins the file's lines with <c>\n</c> and carries no trailing newline,
	///     independent of the file's own line-ending convention.
	/// </remarks>
	public async Task<string?> ReadEntryAsync(string entry, CancellationToken cancellationToken = default)
	{
		if (!IsSafeEntry(entry)) return null;

		var path = ResolvePath(storage.Value.FaqsPath, entry);
		if (!File.Exists(path)) return null;

		var lines = await File.ReadAllLinesAsync(path, cancellationToken);
		return string.Join('\n', lines);
	}

	/// <summary>
	///     Creates a new FAQ entry from a stream.
	/// </summary>
	/// <param name="entry">The entry name without its <c>.txt</c> extension.</param>
	/// <param name="content">The entry content to write.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>A <see cref="CreateResult" /> describing the outcome.</returns>
	public async Task<CreateResult> CreateEntryAsync(string entry, Stream content,
		CancellationToken cancellationToken = default)
	{
		if (!IsSafeEntry(entry)) return CreateResult.InvalidName;

		var path = ResolvePath(storage.Value.FaqsPath, entry);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		if (File.Exists(path)) return CreateResult.AlreadyExists;

		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return CreateResult.Created;
	}

	/// <summary>
	///     Replaces the content of an existing FAQ entry.
	/// </summary>
	/// <param name="entry">The entry name without its <c>.txt</c> extension.</param>
	/// <param name="content">The new entry content.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>A <see cref="ReplaceResult" /> describing the outcome.</returns>
	public async Task<ReplaceResult> ReplaceEntryAsync(string entry, Stream content,
		CancellationToken cancellationToken = default)
	{
		if (!IsSafeEntry(entry)) return ReplaceResult.InvalidName;

		var path = ResolvePath(storage.Value.FaqsPath, entry);
		if (!File.Exists(path)) return ReplaceResult.NotFound;

		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return ReplaceResult.Replaced;
	}

	/// <summary>
	///     Deletes a FAQ entry.
	/// </summary>
	/// <param name="entry">The entry name without its <c>.txt</c> extension.</param>
	/// <returns><see langword="true" /> if the entry was deleted; otherwise, <see langword="false" />.</returns>
	public bool DeleteEntry(string entry)
	{
		if (!IsSafeEntry(entry)) return false;

		var path = ResolvePath(storage.Value.FaqsPath, entry);
		if (!File.Exists(path)) return false;

		File.Delete(path);
		return true;
	}

	/// <summary>
	///     Validates an entry name as a safe file name, allowing <c>:</c>-separated directory nesting.
	/// </summary>
	/// <param name="entry">The proposed entry name.</param>
	/// <returns><see langword="true" /> if the name is safe to use as a file path; otherwise, <see langword="false" />.</returns>
	/// <remarks>
	///     Entry names behave like normal file names, so spaces and most punctuation are fine. A
	///     backslash is rejected, since the path separator differs between Windows and Linux
	///     deployments and a name such as <c>..\..\secret</c> must not survive either. A literal
	///     <c>..</c> is rejected outright as defense in depth against path traversal. Each
	///     <c>:</c>-separated segment (a nested directory or the final file name) must be non-empty,
	///     rejecting a leading/trailing/doubled separator (<c>:file</c>, <c>file:</c>, <c>a::b</c>).
	/// </remarks>
	private static bool IsSafeEntry(string entry)
	{
		if (entry.Length == 0 || entry.Contains('\\') || entry.Contains("..")) return false;
		return Array.TrueForAll(entry.Split(':'), segment => segment.Length > 0);
	}

	/// <summary>
	///     Resolves a validated entry name to its on-disk <c>.txt</c> file path, translating each
	///     <c>:</c>-separated segment into a real nested directory.
	/// </summary>
	/// <param name="faqsRoot">The FAQ storage root directory.</param>
	/// <param name="entry">The entry name, already validated by <see cref="IsSafeEntry" />.</param>
	/// <returns>The absolute path of the entry's <c>.txt</c> file.</returns>
	private static string ResolvePath(string faqsRoot, string entry)
	{
		var segments = entry.Split(':');
		segments[^1] += ".txt";
		return Path.Combine([faqsRoot, .. segments]);
	}
}