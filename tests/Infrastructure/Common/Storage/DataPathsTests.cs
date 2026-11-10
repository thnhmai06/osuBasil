using Basil.Infrastructure.Common.Storage;
using Basil.Testing.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Common.Tests.Storage;

/// <summary>Contract tests of DataPaths: where every stored file lives and how the directories are created.</summary>
public sealed class DataPathsTests : IDisposable
{
	private readonly TempData _data = new(false);

	public void Dispose()
	{
		_data.Dispose();
	}

	[Fact]
	public void RelativePath_UsesRunDirectory()
	{
		var paths = new DataPaths(Options.Create(new StorageOptions { Location = "Data" }));

		Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Data")), paths.Root);
	}

	[Fact]
	public void AbsolutePath_UsedAsIs()
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
	public void EveryKind_HasDocumentedDirectory(string relative)
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
	public void ComputingPaths_CreatesNothing()
	{
		_ = _data.Paths.Replays.Path;

		Assert.False(Directory.Exists(_data.Root));
	}

	[Fact]
	public void CreateDirectories_CreatesEveryDirectory()
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
	public void CreateDirectoriesTwice_KeepsStoredFiles()
	{
		_data.Paths.CreateDirectories();
		File.WriteAllText(Path.Combine(_data.Paths.Faqs.Path, "kept.txt"), "kept");

		_data.Paths.CreateDirectories();

		Assert.Equal("kept", File.ReadAllText(Path.Combine(_data.Paths.Faqs.Path, "kept.txt")));
	}
}