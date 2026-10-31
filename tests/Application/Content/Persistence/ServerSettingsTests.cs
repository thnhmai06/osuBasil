using Basil.Application.Content.Persistence;

namespace Basil.Application.Content.Tests.Persistence;

/// <summary>Verifies the validation of the settings an administrator changes while the server runs.</summary>
public class ServerSettingsTests
{
	public static TheoryData<Action> InvalidValues => new()
	{
		() => _ = new ServerSettings.RoundSettings { ScoreWait = TimeSpan.Zero },
		() => _ = new ServerSettings.RoundSettings { ScoreWait = TimeSpan.FromSeconds(-1) },
		() => _ = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.Zero },
		() => _ = new ServerSettings.SessionSettings { IdleWindow = TimeSpan.Zero },
		() => _ = new ServerSettings.SessionSettings { RetentionPeriod = TimeSpan.FromDays(-1) },
		() => _ = new ServerSettings.LoginLimitSettings { MaxAttempts = 0 },
		() => _ = new ServerSettings.LoginLimitSettings { Window = TimeSpan.Zero },
		() => _ = new ServerSettings.LoginLimitSettings { Lockout = TimeSpan.Zero },
		() => _ = new ServerSettings.ConnectionLimitSettings { PerIp = 0 },
		() => _ = new ServerSettings.Limit(0, TimeSpan.FromSeconds(1)),
		() => _ = new ServerSettings.Limit(1, TimeSpan.Zero),
		() => _ = new ServerSettings.UploadSettings { OszMaxSize = 0 },
		() => _ = new ServerSettings.UploadSettings { ScreenshotMaxSize = -1 },
		() => _ = new ServerSettings.UploadSettings { ScreenshotMaxSide = 0 },
		() => _ = new ServerSettings.UploadSettings { AvatarMaxSize = 0 },
		() => _ = new ServerSettings.UploadSettings { AvatarMaxSide = 0 },
		() => _ = new ServerSettings.BotSettings { CommandPrefix = " " }
	};

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
			Beatmaps = new ServerSettings.BeatmapSettings
			{
				Mirror = new ServerSettings.BeatmapMirrorSettings { DownloadEndpoint = new Uri("ftp://example.test/d") }
			}
		});
	}

	[Fact]
	public void SearchEndpoint_Relative_Throws()
	{
		Assert.Throws<ArgumentException>(() => new ServerSettings
		{
			Beatmaps = new ServerSettings.BeatmapSettings
			{
				Mirror = new ServerSettings.BeatmapMirrorSettings
					{ SearchEndpoint = new Uri("/search", UriKind.Relative) }
			}
		});
	}

	[Fact]
	public void Endpoints_Http_IsAccepted()
	{
		var settings = new ServerSettings
		{
			MenuIcon = new ServerSettings.MenuIconSettings { Url = new Uri("https://example.test/icon.png") },
			Beatmaps = new ServerSettings.BeatmapSettings
			{
				Mirror = new ServerSettings.BeatmapMirrorSettings
				{
					DownloadEndpoint = new Uri("https://example.test/download"),
					SearchEndpoint = new Uri("https://example.test/search")
				}
			}
		};

		Assert.Equal(new Uri("https://example.test/download"), settings.Beatmaps.Mirror.DownloadEndpoint);
		Assert.Equal(new Uri("https://example.test/search"), settings.Beatmaps.Mirror.SearchEndpoint);
	}

	[Fact]
	public void Defaults_FollowTheSpecification()
	{
		var settings = new ServerSettings();

		Assert.Equal(TimeSpan.FromDays(7), settings.Sessions.ApiToken.Lifetime);
		Assert.Equal(TimeSpan.FromSeconds(300), settings.Sessions.IdleWindow);
		Assert.Equal(TimeSpan.FromSeconds(10), settings.Multiplayer.Rounds.ScoreWait);
		Assert.Equal(new ServerSettings.Limit(1, TimeSpan.FromHours(24)), settings.RateLimits.Registration.PerIp);
		Assert.Equal("!", settings.Bot.CommandPrefix);
		Assert.Equal(Locks.Beatmapsets, settings.Locks);
		Assert.Empty(settings.Cors.AllowedOrigins);
	}

	[Theory]
	[MemberData(nameof(InvalidValues))]
	public void AValueThatIsNotPositive_IsRefused(Action create)
	{
		Assert.ThrowsAny<ArgumentException>(create);
	}
}