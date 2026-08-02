namespace Basil.Application.Services.Content;

/// <summary>
///     Stores the in-game main menu icon and its click-through URL on disk.
/// </summary>
/// <remarks>
///     The icon is a single image (<c>Data/MenuIcon.{ext}</c>), and the click-through URL is a text
///     file (<c>Data/MenuIconUrl.txt</c>), replacing the previously hardcoded menu icon and
///     click-through URL configuration. Deleting the icon file is what turns the menu icon off
///     entirely. The URL file has no switch of its own: a missing URL just falls back to a
///     hardcoded default while an icon is set.
/// </remarks>
public static class MenuIconService
{
	private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
	private static readonly string UrlFilePath = Path.Combine(DataDirectory, "MenuIconUrl.txt");

	/// <summary>
	///     Finds the current menu icon file.
	/// </summary>
	/// <returns>
	///	The path of the first <c>MenuIcon.*</c> file in the data folder, or <see langword="null" /> when none exists.
	/// </returns>
	public static string? FindIconPath()
	{
		return Directory.Exists(DataDirectory)
			? Directory.EnumerateFiles(DataDirectory, "MenuIcon.*").FirstOrDefault()
			: null;
	}

	/// <summary>
	///     Saves a new menu icon, replacing any existing one.
	/// </summary>
	/// <param name="content">The icon image content.</param>
	/// <param name="extension">The file extension to use, including the leading dot.</param>
	/// <param name="cancellationToken">A token that cancels the writing.</param>
	public static async Task SaveIconAsync(Stream content, string extension,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(DataDirectory);
		foreach (var existing in Directory.EnumerateFiles(DataDirectory, "MenuIcon.*"))
			File.Delete(existing);

		var path = Path.Combine(DataDirectory, $"MenuIcon{extension}");
		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
	}

	/// <summary>
	///     Deletes the current menu icon.
	/// </summary>
	/// <returns><see langword="true" /> if an icon was deleted; otherwise, <see langword="false" />.</returns>
	public static bool DeleteIcon()
	{
		var path = FindIconPath();
		if (path is null) return false;

		File.Delete(path);
		return true;
	}

	/// <summary>
	///     Reads the click-through URL.
	/// </summary>
	/// <returns>The URL with surrounding whitespace trimmed, or <see langword="null" /> when no URL file exists.</returns>
	public static string? ReadUrl()
	{
		return File.Exists(UrlFilePath) ? File.ReadAllText(UrlFilePath).Trim() : null;
	}

	/// <summary>
	///     Saves the click-through URL.
	/// </summary>
	/// <param name="url">The URL to store.</param>
	/// <param name="cancellationToken">A token that cancels the writing.</param>
	public static async Task SaveUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(DataDirectory);
		await File.WriteAllTextAsync(UrlFilePath, url, cancellationToken);
	}
}