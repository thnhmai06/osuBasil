using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage;

/// <summary>Locates every file and directory the server stores.</summary>
/// <remarks>
///     A relative <see cref="StorageOptions.DataDirectory" /> is resolved against the directory the server runs from.
///     The paths are computed without reading or creating anything on disk.
/// </remarks>
internal sealed class DataPaths(IOptions<StorageOptions> options)
{
	private readonly string root = Path.GetFullPath(Path.IsPathRooted(options.Value.DataDirectory)
		? options.Value.DataDirectory
		: Path.Combine(AppContext.BaseDirectory, options.Value.DataDirectory));

	/// <summary>Gets the directory that holds every stored file.</summary>
	public string Root => root;

	/// <summary>Gets the directory that holds the files of stored beatmapsets, one directory per set.</summary>
	public string Beatmaps => Path.Combine(root, "Beatmaps");

	/// <summary>Gets the directory that holds stored replays.</summary>
	public string Replays => Path.Combine(root, "Replays");

	/// <summary>Gets the directory that holds user avatars.</summary>
	public string Avatars => Path.Combine(root, "Avatars");

	/// <summary>Gets the directory that holds menu banners.</summary>
	public string MenuBanners => Path.Combine(root, "Menu", "Banners");

	/// <summary>Gets the directory that holds seasonal menu backgrounds.</summary>
	public string MenuSeasonals => Path.Combine(root, "Menu", "Seasonals");

	/// <summary>Gets the directory that holds the main-menu icon.</summary>
	public string MenuIcon => Path.Combine(root, "Menu", "Icon");

	/// <summary>Gets the directory that holds the frequently asked questions.</summary>
	public string Faqs => Path.Combine(root, "Faqs");

	/// <summary>Gets the directory that holds files rebuilt from their sources.</summary>
	public string Cache => Path.Combine(root, "Cache");

	/// <summary>Gets the directory that holds the .osz archives built from stored beatmapsets.</summary>
	public string BeatmapArchives => Path.Combine(Cache, "Beatmaps");

	/// <summary>Creates every directory the server stores files in.</summary>
	public void CreateDirectories()
	{
		foreach (var directory in new[]
		         {
			         Root, Beatmaps, Replays, Avatars, MenuBanners, MenuSeasonals, MenuIcon, Faqs, Cache,
			         BeatmapArchives
		         })
			Directory.CreateDirectory(directory);
	}
}
