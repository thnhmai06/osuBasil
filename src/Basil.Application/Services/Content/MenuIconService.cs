namespace Basil.Application.Services.Content;

/// <summary>
///     Filesystem-backed storage for the in-game main menu icon — a single image (`Data/MenuIcon.{ext}`)
///     plus its click-through URL (`Data/MenuIconUrl.txt`), replacing the old hardcoded
///     `ServerOptions.MenuIconPath`/`MenuOnclickUrl` config. Deleting the icon file is what turns the
///     menu icon off entirely (see the login-packet logic in
///     <see cref="Basil.Application.Services.Authentication.LoginService" />); the url file has
///     no such switch of its own — a missing url just falls back to a hardcoded default while an icon
///     is set.
/// </summary>
public sealed class MenuIconService
{
	private static readonly string DataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
	private static readonly string UrlFilePath = Path.Combine(DataDirectory, "MenuIconUrl.txt");

	public string? FindIconPath()
	{
		return Directory.Exists(DataDirectory)
			? Directory.EnumerateFiles(DataDirectory, "MenuIcon.*").FirstOrDefault()
			: null;
	}

	public async Task SaveIconAsync(Stream content, string extension, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(DataDirectory);
		foreach (var existing in Directory.EnumerateFiles(DataDirectory, "MenuIcon.*"))
			File.Delete(existing);

		var path = Path.Combine(DataDirectory, $"MenuIcon{extension}");
		await using var fileStream = File.Create(path);
		await content.CopyToAsync(fileStream, cancellationToken);
	}

	public bool DeleteIcon()
	{
		var path = FindIconPath();
		if (path is null) return false;

		File.Delete(path);
		return true;
	}

	public string? ReadUrl()
	{
		return File.Exists(UrlFilePath) ? File.ReadAllText(UrlFilePath).Trim() : null;
	}

	public async Task SaveUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(DataDirectory);
		await File.WriteAllTextAsync(UrlFilePath, url, cancellationToken);
	}
}