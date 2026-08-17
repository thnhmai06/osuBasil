using Basil.Application.Configurations;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Stores seasonal background images on disk.
/// </summary>
/// <remarks>
///     All operations read and write the same <c>StorageOptions.MenuSeasonalsPath</c> folder, including
///     the osu! client's seasonal background request and the administrative file-management flows.
/// </remarks>
public sealed class MenuSeasonalService(IOptions<StorageOptions> storage)
{
	/// <summary>
	///     Identifies the outcome of a seasonal image creation.
	/// </summary>
	public enum CreateResult : byte
	{
		/// <summary>The image was stored.</summary>
		Created,

		/// <summary>An image with the same name already exists.</summary>
		AlreadyExists
	}

	/// <summary>
	///     Identifies the outcome of a seasonal image replacement.
	/// </summary>
	public enum ReplaceResult : byte
	{
		/// <summary>The image was replaced.</summary>
		Replaced,

		/// <summary>No image with the given name exists.</summary>
		NotFound
	}

	/// <summary>
	///     Lists the names of all stored seasonal images.
	/// </summary>
	/// <returns>The stored file names, ordered case-insensitively.</returns>
	/// <remarks>
	///     The storage folder is created on demand, so this always reports the current contents even
	///     before anything is uploaded.
	/// </remarks>
	public IReadOnlyList<string> ListFileNames()
	{
		Directory.CreateDirectory(storage.Value.MenuSeasonalsPath);
		return
		[
			.. Directory.EnumerateFiles(storage.Value.MenuSeasonalsPath)
				.Select(path => Path.GetFileName(path))
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		];
	}

	/// <summary>
	///     Resolves a file name to the path of a stored seasonal image.
	/// </summary>
	/// <param name="fileName">The stored file name.</param>
	/// <returns>The full path when the file exists, or <see langword="null" /> otherwise.</returns>
	/// <remarks>
	///     The name is reduced to its file-name component before combining, so a caller-supplied
	///     path cannot escape the storage folder.
	/// </remarks>
	public string? FindFilePath(string fileName)
	{
		var path = Path.Combine(storage.Value.MenuSeasonalsPath, Path.GetFileName(fileName));
		return File.Exists(path) ? path : null;
	}

	/// <summary>
	///     Stores a new seasonal image.
	/// </summary>
	/// <param name="fileName">The file name to store the image under.</param>
	/// <param name="content">The image content.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>A <see cref="CreateResult" /> describing the outcome.</returns>
	public async Task<CreateResult> CreateAsync(string fileName, Stream content,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(storage.Value.MenuSeasonalsPath);
		var path = Path.Combine(storage.Value.MenuSeasonalsPath, Path.GetFileName(fileName));
		if (File.Exists(path)) return CreateResult.AlreadyExists;

		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return CreateResult.Created;
	}

	/// <summary>
	///     Replaces an existing seasonal image.
	/// </summary>
	/// <param name="fileName">The file name of the image to replace.</param>
	/// <param name="content">The new image content.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>A <see cref="ReplaceResult" /> describing the outcome.</returns>
	public async Task<ReplaceResult> ReplaceAsync(string fileName, Stream content,
		CancellationToken cancellationToken = default)
	{
		var path = Path.Combine(storage.Value.MenuSeasonalsPath, Path.GetFileName(fileName));
		if (!File.Exists(path)) return ReplaceResult.NotFound;

		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return ReplaceResult.Replaced;
	}

	/// <summary>
	///     Deletes a seasonal image.
	/// </summary>
	/// <param name="fileName">The file name of the image to delete.</param>
	/// <returns><see langword="true" /> if the image was deleted; otherwise, <see langword="false" />.</returns>
	public bool Delete(string fileName)
	{
		var path = Path.Combine(storage.Value.MenuSeasonalsPath, Path.GetFileName(fileName));
		if (!File.Exists(path)) return false;

		File.Delete(path);
		return true;
	}
}