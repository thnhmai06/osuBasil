using Basil.Application.Abstractions.Content;
using Basil.Application.Configurations;
using Basil.Domain.Content;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Content;

/// <summary>
///     Manages main-menu banner metadata and their uploaded image files.
/// </summary>
/// <remarks>
///     A banner's <see cref="MenuBanner.Source" /> is either a locally stored filename (under
///     <c>StorageOptions.MenuBannersPath</c>) or an external <c>http(s)</c> URL; this service keeps
///     both the database row and any uploaded file in sync.
/// </remarks>
public sealed class MenuBannerService(
	IMenuBannerRepository repository,
	IOptions<StorageOptions> storage,
	IOptions<ServerOptions> server)
{
	/// <summary>Gets whether a source value is an external URL rather than a locally stored filename.</summary>
	/// <param name="source">A <see cref="MenuBanner.Source" /> value.</param>
	public static bool IsExternalUrl(string source)
	{
		return source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
		       source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Resolves a banner's source to a full image URL, for use in public API responses.</summary>
	/// <param name="source">A <see cref="MenuBanner.Source" /> value.</param>
	public string ResolveSourceUrl(string source)
	{
		return IsExternalUrl(source) ? source : $"https://assets.{server.Value.Domain}/menu/banners/{source}";
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

	/// <summary>Creates a banner whose source is an external URL.</summary>
	/// <param name="source">The external image URL.</param>
	/// <param name="url">The click-through URL.</param>
	/// <param name="begins">The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound.</param>
	/// <param name="expires">The UTC instant the banner stops being current, or <see langword="null" /> for no upper bound.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public Task<MenuBanner> CreateAsync(string source, string url, DateTime? begins, DateTime? expires,
		CancellationToken cancellationToken = default)
	{
		return repository.CreateAsync(source, url, begins, expires, cancellationToken);
	}

	/// <summary>Creates a banner whose source is an uploaded file.</summary>
	/// <param name="content">The image content.</param>
	/// <param name="extension">The file extension to store it under, including the leading dot.</param>
	/// <param name="url">The click-through URL.</param>
	/// <param name="begins">The UTC instant the banner starts being current, or <see langword="null" /> for no lower bound.</param>
	/// <param name="expires">The UTC instant the banner stops being current, or <see langword="null" /> for no upper bound.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public async Task<MenuBanner> CreateFromUploadAsync(Stream content, string extension, string url,
		DateTime? begins, DateTime? expires, CancellationToken cancellationToken = default)
	{
		var fileName = await SaveUploadAsync(content, extension, cancellationToken);
		return await repository.CreateAsync(fileName, url, begins, expires, cancellationToken);
	}

	/// <summary>Replaces an existing banner's source with an uploaded file.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="content">The new image content.</param>
	/// <param name="extension">The file extension to store it under, including the leading dot.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The updated banner, or <see langword="null" /> when no such banner exists.</returns>
	public async Task<MenuBanner?> ReplaceImageAsync(int id, Stream content, string extension,
		CancellationToken cancellationToken = default)
	{
		var existing = await repository.FetchByIdAsync(id, cancellationToken);
		if (existing is null) return null;

		DeleteLocalFileIfAny(existing.Source);
		var fileName = await SaveUploadAsync(content, extension, cancellationToken);
		return await repository.UpdateAsync(id, fileName, null, null, null, cancellationToken);
	}

	/// <summary>Updates a banner's metadata fields, leaving any <see langword="null" /> argument unchanged.</summary>
	/// <remarks>
	///     Setting <paramref name="source" /> here always means an external URL; a local file already
	///     stored under this banner is deleted, since <c>Source</c> can only point at one form at a time.
	///     Because <see langword="null" /> means "leave unchanged", this cannot clear an existing
	///     <paramref name="begins" />/<paramref name="expires" /> bound back to unset (permanent) —
	///     delete and recreate the banner for that.
	/// </remarks>
	/// <param name="id">The banner's id.</param>
	/// <param name="source">The new external image URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="url">The new click-through URL, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="begins">The new start instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="expires">The new end instant, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The updated banner, or <see langword="null" /> when no such banner exists.</returns>
	public async Task<MenuBanner?> UpdateAsync(int id, string? source, string? url, DateTime? begins,
		DateTime? expires, CancellationToken cancellationToken = default)
	{
		if (source is not null)
		{
			var existing = await repository.FetchByIdAsync(id, cancellationToken);
			if (existing is null) return null;
			DeleteLocalFileIfAny(existing.Source);
		}

		return await repository.UpdateAsync(id, source, url, begins, expires, cancellationToken);
	}

	/// <summary>Deletes a banner and its uploaded file, if any.</summary>
	/// <param name="id">The banner's id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if a banner was deleted; otherwise, <see langword="false" />.</returns>
	public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
	{
		var existing = await repository.FetchByIdAsync(id, cancellationToken);
		if (existing is null) return false;

		DeleteLocalFileIfAny(existing.Source);
		return await repository.DeleteAsync(id, cancellationToken);
	}

	private async Task<string> SaveUploadAsync(Stream content, string extension, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(storage.Value.MenuBannersPath);
		var fileName = $"{Guid.NewGuid():N}{extension}";
		var path = Path.Combine(storage.Value.MenuBannersPath, fileName);
		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
		return fileName;
	}

	private void DeleteLocalFileIfAny(string source)
	{
		if (IsExternalUrl(source)) return;

		var path = Path.Combine(storage.Value.MenuBannersPath, source);
		if (File.Exists(path)) File.Delete(path);
	}
}