using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Content;
using Basil.Application.Configurations;
using Basil.Domain.Content;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Manages main-menu banner metadata and their uploaded image files.
/// </summary>
/// <remarks>
///     A banner's <see cref="MenuBanner.Image" /> is either a locally stored filename (under
///     <c>StorageOptions.MenuBannersPath</c>) or an external <c>http(s)</c> URL; this service keeps
///     both the database row and any uploaded file in sync.
/// </remarks>
public sealed partial class MenuBannerService(
	IMenuBannerRepository repository,
	IOptions<StorageOptions> storage,
	IOptions<ServerOptions> server)
{
	/// <summary>Gets whether an image value is an external URL rather than a locally stored filename.</summary>
	/// <param name="image">A <see cref="MenuBanner.Image" /> value.</param>
	public static bool IsExternalUrl(string image)
	{
		return image.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
		       image.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Resolves a banner's image to a full image URL, for use in public API responses.</summary>
	/// <param name="image">A <see cref="MenuBanner.Image" /> value.</param>
	public string ResolveImageUrl(string image)
	{
		return IsExternalUrl(image) ? image : $"https://assets.{server.Value.Domain}/menu/banners/{image}";
	}

	/// <summary>Fetches every stored banner.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task<IReadOnlyList<MenuBanner>> FetchAllAsync(CancellationToken cancellationToken = default)
	{
		return repository.FetchAllAsync(cancellationToken);
	}

	/// <summary>Fetches a single banner by id.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task<MenuBanner?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
	{
		return repository.FetchByIdAsync(id, cancellationToken);
	}

	/// <summary>Creates a banner whose image is an external URL.</summary>
	/// <param name="image">The external image URL.</param>
	/// <param name="url">The click-through URL.</param>
	/// <param name="begins">The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound.</param>
	/// <param name="expires">The UTC instant the banner stops being current, or <see langword="null" /> for no upper bound.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task<MenuBanner> CreateAsync(string image, string url, DateTime? begins, DateTime? expires,
		CancellationToken cancellationToken = default)
	{
		return repository.CreateAsync(image, url, begins, expires, cancellationToken);
	}

	/// <summary>Creates a banner whose image is an uploaded file.</summary>
	/// <param name="content">The image content.</param>
	/// <param name="originalFileName">The uploaded file's original name, kept as the stored filename.</param>
	/// <param name="url">The click-through URL.</param>
	/// <param name="begins">The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound.</param>
	/// <param name="expires">The UTC instant the banner stops being current, or <see langword="null" /> for no upper bound.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task<MenuBanner> CreateFromUploadAsync(Stream content, string originalFileName, string url,
		DateTime? begins, DateTime? expires, CancellationToken cancellationToken = default)
	{
		var fileName = await SaveUploadAsync(content, originalFileName, cancellationToken);
		return await repository.CreateAsync(fileName, url, begins, expires, cancellationToken);
	}

	/// <summary>Replaces an existing banner's image with an uploaded file, optionally updating other fields too.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="content">The new image content.</param>
	/// <param name="originalFileName">The uploaded file's original name, kept as the stored filename.</param>
	/// <param name="url">The new click-through URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="begins">The new start instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="expires">The new end instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The updated banner, or <see langword="null" /> when no such banner exists.</returns>
	public async Task<MenuBanner?> ReplaceImageAsync(int id, Stream content, string originalFileName,
		string? url = null, DateTime? begins = null, DateTime? expires = null,
		CancellationToken cancellationToken = default)
	{
		var existing = await repository.FetchByIdAsync(id, cancellationToken);
		if (existing is null) return null;

		DeleteLocalFileIfAny(existing.Image);
		var fileName = await SaveUploadAsync(content, originalFileName, cancellationToken);
		return await repository.UpdateAsync(id, fileName, url, begins, expires, cancellationToken);
	}

	/// <summary>Updates a banner's metadata fields, leaving any <see langword="null" /> argument unchanged.</summary>
	/// <remarks>
	///     Setting <paramref name="image" /> here always means an external URL; a local file already
	///     stored under this banner is deleted, since <c>Image</c> can only point at one form at a time.
	///     Because <see langword="null" /> means "leave unchanged", this cannot clear an existing
	///     <paramref name="begins" />/<paramref name="expires" /> bound back to unset (permanent) —
	///     delete and recreate the banner for that.
	/// </remarks>
	/// <param name="id">The banner's id.</param>
	/// <param name="image">The new external image URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="url">The new click-through URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="begins">The new start instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="expires">The new end instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The updated banner, or <see langword="null" /> when no such banner exists.</returns>
	public async Task<MenuBanner?> UpdateAsync(int id, string? image, string? url, DateTime? begins,
		DateTime? expires, CancellationToken cancellationToken = default)
	{
		if (image is not null)
		{
			var existing = await repository.FetchByIdAsync(id, cancellationToken);
			if (existing is null) return null;
			DeleteLocalFileIfAny(existing.Image);
		}

		return await repository.UpdateAsync(id, image, url, begins, expires, cancellationToken);
	}

	/// <summary>Deletes a banner and its uploaded file, if any.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if a banner was deleted; otherwise, <see langword="false" />.</returns>
	public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		var existing = await repository.FetchByIdAsync(id, cancellationToken);
		if (existing is null) return false;

		DeleteLocalFileIfAny(existing.Image);
		return await repository.DeleteAsync(id, cancellationToken);
	}

	/// <summary>
	///     Saves an upload under its original filename, appending a numeric suffix if that name is
	///     already taken by another banner's file. A trailing density suffix such as <c>@2x</c> is kept
	///     at the end of the name, with the numeric suffix inserted before it (e.g. <c>banner-1@2x.png</c>,
	///     not <c>banner@2x-1.png</c>), since the client only recognizes the density marker there.
	/// </summary>
	private async Task<string> SaveUploadAsync(Stream content, string originalFileName,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(storage.Value.MenuBannersPath);

		var safeName = Path.GetFileName(originalFileName);
		var baseName = Path.GetFileNameWithoutExtension(safeName);
		var extension = Path.GetExtension(safeName);

		var densityMatch = DensitySuffixRegex().Match(baseName);
		var namePart = densityMatch.Success ? baseName[..densityMatch.Index] : baseName;
		var densitySuffix = densityMatch.Success ? densityMatch.Value : "";

		var fileName = safeName;
		var path = Path.Combine(storage.Value.MenuBannersPath, fileName);
		for (var suffix = 1; File.Exists(path); suffix++)
		{
			fileName = $"{namePart}-{suffix}{densitySuffix}{extension}";
			path = Path.Combine(storage.Value.MenuBannersPath, fileName);
		}

		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return fileName;
	}

	private void DeleteLocalFileIfAny(string image)
	{
		if (IsExternalUrl(image)) return;

		var path = Path.Combine(storage.Value.MenuBannersPath, image);
		if (File.Exists(path)) File.Delete(path);
	}

	/// <summary>Matches a trailing pixel-density suffix such as <c>@2x</c> or <c>@1.5x</c>.</summary>
	[GeneratedRegex(@"@\d+(\.\d+)?x$", RegexOptions.IgnoreCase)]
	private static partial Regex DensitySuffixRegex();
}