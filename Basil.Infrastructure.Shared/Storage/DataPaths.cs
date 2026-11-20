using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Shared.Storage;

public sealed class DataPaths
{
	public DataPaths(IOptions<StorageOptions> options)
	{
		Root = Path.GetFullPath(Path.IsPathRooted(options.Value.Location)
			? options.Value.Location
			: Path.Combine(AppContext.BaseDirectory, options.Value.Location));
		Beatmapsets = new DataStorage(Root, "Beatmapsets");
		Replays = new DataStorage(Root, "Replays");
		Avatars = new DataStorage(Root, "Avatars");
		MenuBanners = new DataStorage(Root, "Menu/Banners");
		MenuSeasonals = new DataStorage(Root, "Menu/Seasonals");
		MenuIcon = new DataStorage(Root, "Menu/Icon");
		Faqs = new DataStorage(Root, "Faqs");
		Screenshots = new DataStorage(Root, "Screenshots");
		Cache = new DataStorage(Root, "Cache");
	}

	public string Root { get; }

	public string Spool => Path.Combine(Root, "Spool");

	public string Quarantine => Path.Combine(Root, "Quarantine");

	public DataStorage Beatmapsets { get; }

	public DataStorage Replays { get; }

	public DataStorage Avatars { get; }

	public DataStorage MenuBanners { get; }

	public DataStorage MenuSeasonals { get; }

	public DataStorage MenuIcon { get; }

	public DataStorage Faqs { get; }

	public DataStorage Screenshots { get; }

	public DataStorage Cache { get; }

	public void CreateDirectories()
	{
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(Spool);
		Directory.CreateDirectory(Quarantine);
		foreach (var storage in new[]
		         {
			         Beatmapsets, Replays, Avatars, MenuBanners, MenuSeasonals, MenuIcon, Faqs, Screenshots, Cache
		         })
			Directory.CreateDirectory(storage.Path);
	}
}