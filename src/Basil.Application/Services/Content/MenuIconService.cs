using Basil.Application.Abstractions.Settings;

namespace Basil.Application.Services.Content;

/// <summary>
///     Stores the in-game main menu icon and its click-through URL.
/// </summary>
public sealed class MenuIconService(ISettingsRepository settings)
{
	private const string PathKey = "MenuIcon:Path";
	private const string UrlKey = "MenuIcon:Url";

	private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");

	/// <summary>Gets whether a stored icon path is an external URL rather than a local file path.</summary>
	/// <param name="path">The stored <c>MenuIcon:Path</c> value.</param>
	public static bool IsExternalUrl(string path)
	{
		return path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Gets the current icon reference: a local file path, an external URL, or null if unset.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	public Task<string?> GetPathAsync(CancellationToken cancellationToken = default)
	{
		return settings.GetAsync(PathKey, cancellationToken);
	}

	/// <summary>
	///     Saves a new menu icon uploaded as image bytes, replacing any existing icon (local file or
	///     external URL).
	/// </summary>
	/// <param name="content">The icon image content.</param>
	/// <param name="extension">The file extension to use, including the leading dot.</param>
	/// <param name="cancellationToken">A token that cancels the writing.</param>
	public async Task SaveIconAsync(Stream content, string extension, CancellationToken cancellationToken = default)
	{
		await DeleteStalePhysicalFileAsync(cancellationToken);

		Directory.CreateDirectory(DataDirectory);
		var path = Path.Combine(DataDirectory, $"MenuIcon{extension}");
		await using (var fileStream = File.Create(path))
		{
			await content.CopyToAsync(fileStream, cancellationToken);
		}

		await settings.SetAsync(PathKey, path, cancellationToken);
	}

	/// <summary>
	///     Sets the menu icon to an externally hosted image, replacing any existing icon (local file
	///     or external URL).
	/// </summary>
	/// <param name="url">The external image URL.</param>
	/// <param name="cancellationToken">A token that cancels the writing.</param>
	public async Task SetExternalIconAsync(string url, CancellationToken cancellationToken = default)
	{
		await DeleteStalePhysicalFileAsync(cancellationToken);
		await settings.SetAsync(PathKey, url, cancellationToken);
	}

	/// <summary>Deletes the current menu icon, whether it is a local file or an external URL.</summary>
	/// <param name="cancellationToken">A token that cancels the deletion.</param>
	/// <returns><see langword="true" /> if an icon was deleted; otherwise, <see langword="false" />.</returns>
	public async Task<bool> DeleteIconAsync(CancellationToken cancellationToken = default)
	{
		var current = await settings.GetAsync(PathKey, cancellationToken);
		if (current is null) return false;

		if (!IsExternalUrl(current) && File.Exists(current)) File.Delete(current);
		await settings.SetAsync(PathKey, null, cancellationToken);
		return true;
	}

	/// <summary>Reads the click-through URL.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The stored URL, or <see langword="null" /> when none is set.</returns>
	public Task<string?> ReadUrlAsync(CancellationToken cancellationToken = default)
	{
		return settings.GetAsync(UrlKey, cancellationToken);
	}

	/// <summary>Saves the click-through URL.</summary>
	/// <param name="url">The URL to store.</param>
	/// <param name="cancellationToken">A token that cancels the writing.</param>
	public Task SaveUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		return settings.SetAsync(UrlKey, url, cancellationToken);
	}

	/// <summary>Deletes the physical file the current <see cref="PathKey" /> points to, if it points to one.</summary>
	private async Task DeleteStalePhysicalFileAsync(CancellationToken cancellationToken)
	{
		var current = await settings.GetAsync(PathKey, cancellationToken);
		if (current is not null && !IsExternalUrl(current) && File.Exists(current)) File.Delete(current);
	}
}