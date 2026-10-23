using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Files;
using Basil.Infrastructure.Persistence.Tests.Support;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Persistence.Tests.Files;

/// <summary>Contract tests of DataPaths: where every stored file lives and how the directories are created.</summary>
public sealed class DataPathsTests : IDisposable
{
	private readonly TempData _data = new(false);

	[Fact]
	public void A_relative_data_directory_is_resolved_against_the_directory_the_server_runs_from()
	{
		var paths = new DataPaths(Options.Create(new StorageOptions { DataDirectory = "Data" }));

		Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Data")), paths.Root);
	}

	[Fact]
	public void An_absolute_data_directory_is_used_as_it_is()
	{
		Assert.Equal(_data.Root, _data.Paths.Root);
	}

	[Theory]
	[InlineData("Beatmapsets")]
	[InlineData("Replays")]
	[InlineData("Avatars")]
	[InlineData("Menu/Banners")]
	[InlineData("Menu/Seasonals")]
	[InlineData("Menu/Icon")]
	[InlineData("Faqs")]
	[InlineData("Screenshots")]
	[InlineData("Cache")]
	public void Every_stored_kind_has_the_directory_the_documentation_names(string relative)
	{
		var storages = new[]
		{
			_data.Paths.Beatmapsets, _data.Paths.Replays, _data.Paths.Avatars, _data.Paths.MenuBanners,
			_data.Paths.MenuSeasonals, _data.Paths.MenuIcon, _data.Paths.Faqs, _data.Paths.Screenshots,
			_data.Paths.Cache
		};

		var storage = Assert.Single(storages, candidate => candidate.RelativePath == relative);
		Assert.Equal(Path.Combine(_data.Root, relative.Replace('/', Path.DirectorySeparatorChar)), storage.Path);
	}

	[Fact]
	public void Computing_the_paths_creates_nothing_on_disk()
	{
		_ = _data.Paths.Replays.Path;

		Assert.False(Directory.Exists(_data.Root));
	}

	[Fact]
	public void CreateDirectories_creates_the_data_directory_and_every_storage_directory()
	{
		_data.Paths.CreateDirectories();

		Assert.All(new[]
		{
			_data.Paths.Beatmapsets, _data.Paths.Replays, _data.Paths.Avatars, _data.Paths.MenuBanners,
			_data.Paths.MenuSeasonals, _data.Paths.MenuIcon, _data.Paths.Faqs, _data.Paths.Screenshots,
			_data.Paths.Cache
		}, storage => Assert.True(Directory.Exists(storage.Path), storage.RelativePath));
	}

	[Fact]
	public void CreateDirectories_twice_keeps_what_is_stored()
	{
		_data.Paths.CreateDirectories();
		File.WriteAllText(Path.Combine(_data.Paths.Faqs.Path, "kept.txt"), "kept");

		_data.Paths.CreateDirectories();

		Assert.Equal("kept", File.ReadAllText(Path.Combine(_data.Paths.Faqs.Path, "kept.txt")));
	}

	[Theory]
	[InlineData("Songs")]
	[InlineData("Beatmaps")]
	public void CreateDirectories_moves_beatmapsets_stored_under_a_former_directory(string former)
	{
		var formerDirectory = Path.Combine(_data.Root, former, "1 Artist - Title");
		Directory.CreateDirectory(formerDirectory);
		File.WriteAllText(Path.Combine(formerDirectory, "archive.osz"), "set");

		_data.Paths.CreateDirectories();

		Assert.Equal("set", File.ReadAllText(Path.Combine(_data.Paths.Beatmapsets.Path, "1 Artist - Title", "archive.osz")));
		Assert.False(Directory.Exists(Path.Combine(_data.Root, former)));
	}

	public void Dispose() => _data.Dispose();
}
