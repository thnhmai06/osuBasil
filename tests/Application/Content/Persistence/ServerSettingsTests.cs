using Basil.Application.Content.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Content.Tests.Persistence;

/// <summary>Verifies the validation of the settings an administrator changes while the server runs.</summary>
public class ServerSettingsTests
{
	public static TheoryData<Action> InvalidValues => new()
	{
		() => _ = new ServerSettings.RoundSettings { ScoreWait = TimeSpan.Zero },
		() => _ = new ServerSettings.RoundSettings { ScoreWait = TimeSpan.FromSeconds(-1) },
		() => _ = new ServerSettings.ApiTokenSettings { Lifetime = TimeSpan.Zero },
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
		() => _ = Limits(0),
		() => _ = Limits(window: TimeSpan.Zero),
		() => _ = Limits(elevatedMultiplier: 0),
		() => _ = Limits(interactive: 0),
		() => _ = Limits(listing: 0),
		() => _ = Limits(upload: 0),
		() => _ = Limits(live: 0),
		() => _ = Limits(busyRetryAfter: TimeSpan.Zero),
		() => _ = Limits(degraded: 0),
		() => _ = Limits(hardShed: 1.01),
		() => _ = Limits(degraded: 0.7, shed: 0.7),
		() => _ = Limits(shed: 0.9, hardShed: 0.9),
		() => _ = new ServerSettings.BotSettings { CommandPrefix = " " },
		() => _ = new ServerSettings.AccountSettings { DefaultPrivileges = (UserPrivileges)(1 << 7) }
	};

	private static ServerSettings.AdmissionLimitSettings Limits(int requests = 120, TimeSpan? window = null,
		int elevatedMultiplier = 4, int interactive = 256, int listing = 32, int upload = 8, int live = 1024,
		double degraded = 0.5, double shed = 0.7, double hardShed = 0.9, TimeSpan? busyRetryAfter = null)
	{
		return new ServerSettings.AdmissionLimitSettings(requests, window ?? TimeSpan.FromMinutes(1),
			elevatedMultiplier, interactive, listing, upload, live, degraded, shed, hardShed,
			busyRetryAfter ?? TimeSpan.FromSeconds(5));
	}

	[Fact]
	public void FileScheme_Throws()
	{
		Assert.Throws<ArgumentException>(() => new ServerSettings
		{
			MenuIcon = new ServerSettings.MenuIconSettings { Url = new Uri("file:///tmp/icon.png") }
		});
	}

	[Fact]
	public void FtpScheme_Throws()
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
	public void Relative_Throws()
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
	public void Http_IsAccepted()
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
		Assert.Equal(TimeSpan.FromSeconds(10), settings.Multiplayer.Rounds.ScoreWait);
		Assert.Equal(new ServerSettings.Limit(1, TimeSpan.FromHours(24)), settings.RateLimits.Registration.PerIp);
		Assert.Equal("!", settings.Bot.CommandPrefix);
		Assert.Equal(Locks.Beatmapsets, settings.Locks);
		Assert.Empty(settings.Cors.AllowedOrigins);
	}

	[Theory]
	[MemberData(nameof(InvalidValues))]
	public void ValueNotPositive_Refused(Action create)
	{
		Assert.ThrowsAny<ArgumentException>(create);
	}
}