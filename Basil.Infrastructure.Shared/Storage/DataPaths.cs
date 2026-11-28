namespace Basil.Infrastructure.Shared.Storage;

public sealed class DataPaths
{
	public DataPaths()
	{
		// Stored files always live in Data beside the executable; what is rebuilt or transient sits beside it.
		var install = AppContext.BaseDirectory;
		Root = Path.Combine(install, "Data");
		Beatmapsets = new DataStorage(Root, "Beatmapsets");
		Replays = new DataStorage(Root, "Replays");
		Avatars = new DataStorage(Root, "Avatars");
		MenuBanners = new DataStorage(Root, "Menu/Banners");
		MenuSeasonals = new DataStorage(Root, "Menu/Seasonals");
		MenuIcon = new DataStorage(Root, "Menu/Icon");
		Faqs = new DataStorage(Root, "Faqs");
		Screenshots = new DataStorage(Root, "Screenshots");
		Cache = new DataStorage(install, "Cache");
		Logs = Path.Combine(install, "Logs");
		Temp = Path.Combine(install, "Temp");
	}

	public string Root { get; }

	public string Logs { get; }

	public string Temp { get; }

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
		Directory.CreateDirectory(Logs);
		Directory.CreateDirectory(Temp);
		foreach (var storage in new[]
		         {
			         Beatmapsets, Replays, Avatars, MenuBanners, MenuSeasonals, MenuIcon, Faqs, Screenshots, Cache
		         })
			Directory.CreateDirectory(storage.Path);
	}
}