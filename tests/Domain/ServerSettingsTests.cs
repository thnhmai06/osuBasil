namespace Basil.Domain.Tests;

/// <summary>Verifies the validation of the settings an administrator changes while the server runs.</summary>
public class ServerSettingsTests
{
	[Fact]
	public void MenuIconUrl_FileScheme_Throws()
	{
		Assert.Throws<ArgumentException>(() => new ServerSettings
		{
			MenuIcon = new ServerSettings.MenuIconSettings { Url = new Uri("file:///tmp/icon.png") }
		});
	}

	[Fact]
	public void DownloadEndpoint_FtpScheme_Throws()
	{
		Assert.Throws<ArgumentException>(() => new ServerSettings
		{
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings { DownloadEndpoint = new Uri("ftp://example.test/d") }
		});
	}

	[Fact]
	public void SearchEndpoint_Relative_Throws()
	{
		Assert.Throws<ArgumentException>(() => new ServerSettings
		{
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				SearchEndpoint = new Uri("/search", UriKind.Relative)
			}
		});
	}

	[Fact]
	public void Endpoints_Http_IsAccepted()
	{
		var settings = new ServerSettings
		{
			MenuIcon = new ServerSettings.MenuIconSettings { Url = new Uri("https://example.test/icon.png") },
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = new Uri("https://example.test/download"),
				SearchEndpoint = new Uri("https://example.test/search")
			}
		};

		Assert.Equal(new Uri("https://example.test/download"), settings.BeatmapMirror.DownloadEndpoint);
		Assert.Equal(new Uri("https://example.test/search"), settings.BeatmapMirror.SearchEndpoint);
	}
}