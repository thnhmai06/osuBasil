using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage;

/// <summary>Locates every file and directory the server stores.</summary>
/// <remarks>
///     A relative <see cref="StorageOptions.DataDirectory" /> is resolved against the directory the server runs from.
///     The paths are computed without reading or creating anything on disk.
/// </remarks>
internal sealed class DataPaths
{
	/// <summary>Creates the set of paths rooted at the configured data directory.</summary>
	/// <param name="options">The storage configuration.</param>
	public DataPaths(IOptions<StorageOptions> options)
	{
		var dataDirectory = options.Value.DataDirectory;
		Root = Path.GetFullPath(Path.IsPathRooted(dataDirectory)
			? dataDirectory
			: Path.Combine(AppContext.BaseDirectory, dataDirectory));

		Database = Path.Combine(Root, "Basil.db");
		Beatmapsets = Path.Combine(Root, "Beatmapsets");
		Imports = Path.Combine(Root, "Imports");
		Replays = Path.Combine(Root, "Replays");
		Avatars = Path.Combine(Root, "Avatars");
		MenuBanners = Path.Combine(Root, "Menu", "Banners");
		MenuSeasonals = Path.Combine(Root, "Menu", "Seasonals");
		MenuIcon = Path.Combine(Root, "Menu", "Icon");
		Faqs = Path.Combine(Root, "Faqs");
		Cache = Path.Combine(Root, "Cache");
	}

	/// <summary>Gets the directory that holds every stored file.</summary>
	public string Root { get; }

	/// <summary>Gets the path of the server's database.</summary>
	public string Database { get; }

	/// <summary>Gets the directory that holds the imported beatmapset archives.</summary>
	public string Beatmapsets { get; }

	/// <summary>Gets the directory watched for beatmapset archives to import.</summary>
	public string Imports { get; }

	/// <summary>Gets the directory that holds stored replays.</summary>
	public string Replays { get; }

	/// <summary>Gets the directory that holds user avatars.</summary>
	public string Avatars { get; }

	/// <summary>Gets the directory that holds menu banners.</summary>
	public string MenuBanners { get; }

	/// <summary>Gets the directory that holds seasonal menu backgrounds.</summary>
	public string MenuSeasonals { get; }

	/// <summary>Gets the directory that holds the main-menu icon.</summary>
	public string MenuIcon { get; }

	/// <summary>Gets the directory that holds the frequently asked questions.</summary>
	public string Faqs { get; }

	/// <summary>Gets the directory that holds files rebuilt from their sources.</summary>
	public string Cache { get; }

	/// <summary>Creates every directory the server stores files in.</summary>
	public void CreateDirectories()
	{
		foreach (var directory in new[]
		         {
			         Root, Beatmapsets, Imports, Replays, Avatars, MenuBanners, MenuSeasonals, MenuIcon, Faqs, Cache
		         })
			Directory.CreateDirectory(directory);
	}
}
