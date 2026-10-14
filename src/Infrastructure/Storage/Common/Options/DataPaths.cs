using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Storage.Common.Options;

/// <summary>Locates every file and directory the server stores.</summary>
/// <remarks>
///     A relative <see cref="StorageOptions.DataDirectory" /> is resolved against the directory the server runs from.
///     The paths are computed without reading or creating anything on disk.
/// </remarks>
internal sealed class DataPaths
{
	/// <summary>Locates the stored files under the configured data directory.</summary>
	/// <param name="options">The storage options naming the data directory.</param>
	public DataPaths(IOptions<StorageOptions> options)
	{
		Root = Path.GetFullPath(Path.IsPathRooted(options.Value.DataDirectory)
			? options.Value.DataDirectory
			: Path.Combine(AppContext.BaseDirectory, options.Value.DataDirectory));
		Beatmapsets = new DataStorage(Root, "Beatmapsets");
		Replays = new DataStorage(Root, "Replays");
		Avatars = new DataStorage(Root, "Avatars");
		MenuBanners = new DataStorage(Root, "Menu/Banners");
		MenuSeasonals = new DataStorage(Root, "Menu/Seasonals");
		MenuIcon = new DataStorage(Root, "Menu/Icon");
		Faqs = new DataStorage(Root, "Faqs");
		Screenshots = new DataStorage(Root, "Screenshots");
		Cache = new DataStorage(Root, "Cache");
		Staging = new DataStorage(Root, "Cache/Staging");
	}

	/// <summary>Gets the directory that holds every stored file.</summary>
	public string Root { get; }

	/// <summary>Gets the files of stored beatmapsets, one directory per set.</summary>
	public DataStorage Beatmapsets { get; }

	/// <summary>Gets the files of stored replays.</summary>
	public DataStorage Replays { get; }

	/// <summary>Gets the files of user avatars.</summary>
	public DataStorage Avatars { get; }

	/// <summary>Gets the files of menu banners.</summary>
	public DataStorage MenuBanners { get; }

	/// <summary>Gets the files of seasonal menu backgrounds.</summary>
	public DataStorage MenuSeasonals { get; }

	/// <summary>Gets the files of the main-menu icon.</summary>
	public DataStorage MenuIcon { get; }

	/// <summary>Gets the files of the frequently asked questions.</summary>
	public DataStorage Faqs { get; }

	/// <summary>Gets the screenshots players uploaded.</summary>
	public DataStorage Screenshots { get; }

	/// <summary>Gets the files rebuilt from their sources.</summary>
	public DataStorage Cache { get; }

	/// <summary>Gets the folder where a set's files are written before they replace its stored folder.</summary>
	public DataStorage Staging { get; }

	/// <summary>Creates every directory the server stores files in.</summary>
	/// <remarks>
	///     Beatmapsets stored under the former <c>Songs</c> directory, or under the older <c>Beatmaps</c> directory, are
	///     moved to <see cref="Beatmapsets" /> first.
	/// </remarks>
	public void CreateDirectories()
	{
		Directory.CreateDirectory(Root);
		if (!Directory.Exists(Beatmapsets.Path))
		{
			var formerSongs = Path.Combine(Root, "Songs");
			var formerBeatmaps = Path.Combine(Root, "Beatmaps");
			if (Directory.Exists(formerSongs)) Directory.Move(formerSongs, Beatmapsets.Path);
			else if (Directory.Exists(formerBeatmaps)) Directory.Move(formerBeatmaps, Beatmapsets.Path);
		}

		foreach (var storage in new[]
		         {
			         Beatmapsets, Replays, Avatars, MenuBanners, MenuSeasonals, MenuIcon, Faqs, Screenshots, Cache,
			         Staging
		         })
			Directory.CreateDirectory(storage.Path);
	}
}