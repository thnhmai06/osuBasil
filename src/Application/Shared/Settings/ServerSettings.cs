using System.Text.Json.Serialization;
using Basil.Domain.Content;

using Basil.Domain.Users;
using Basil.Domain.Game.Users;
using Basil.Shared.Utilities;

namespace Basil.Application.Shared.Settings;

public sealed record ServerSettings
{
	public GeneralSettings General { get; init; } = new();

	public Locks Locks
	{
		get;
		init => field = value.ThrowIfInvalid();
	} = Locks.Beatmapsets;

	public AccountSettings Accounts { get; init; } = new();

	public SessionSettings Sessions { get; init; } = new();

	public RateLimitSettings RateLimits { get; init; } = new();

	public UploadSettings Uploads { get; init; } = new();

	public BeatmapSettings Beatmaps { get; init; } = new();

	public MultiplayerSettings Multiplayer { get; init; } = new();

	public BotSettings Bot { get; init; } = new();

	public CorsSettings Cors { get; init; } = new();

	public MenuIconSettings MenuIcon
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new();

	private static TimeSpan Positive(TimeSpan value)
	{
		return value > TimeSpan.Zero
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The duration must be positive.");
	}

	private static int Positive(int value)
	{
		return value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The number must be positive.");
	}

	private static double Fraction(double value)
	{
		return value is > 0 and <= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The load must be above 0 and at most 1.");
	}

	private static long Positive(long value)
	{
		return value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The number must be positive.");
	}

	public sealed record GeneralSettings
	{
		public string? Motd
		{
			get;
			init => field = value?.ThrowIfHasNul();
		}
	}

	public sealed record AccountSettings
	{
		public UserPrivileges DefaultPrivileges
		{
			get;
			init => field = value.ThrowIfInvalid();
		} = UserPrivilegesExtensions.Default;
	}

	public sealed record SessionSettings
	{
		public TimeSpan RetentionPeriod
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromDays(90);
	}

	public sealed record Limit
	{
		public Limit(int count, TimeSpan window)
		{
			Count = Positive(count);
			Window = Positive(window);
		}

		public int Count { get; }

		public TimeSpan Window { get; }
	}

	public sealed record RateLimitSettings
	{
		public LoginLimitSettings Login { get; init; } = new();

		public RegistrationLimitSettings Registration { get; init; } = new();

		public ConnectionLimitSettings Connections { get; init; } = new();

		public RestLimitSettings Rest { get; init; } = new();

		public Limit Chat { get; init; } = new(10, TimeSpan.FromSeconds(5));

		public AdmissionLimitSettings Admission { get; init; } = new();
	}

	public sealed record AdmissionLimitSettings
	{
		[JsonConstructor]
		public AdmissionLimitSettings(int requests, TimeSpan window, int elevatedMultiplier,
			int interactiveConcurrency, int listingConcurrency, int uploadConcurrency, int liveConcurrency,
			double degradedAbove, double shedAbove, double hardShedAbove, TimeSpan busyRetryAfter,
			int assetConcurrency = 64, int assetRequests = 1200)
		{
			Requests = Positive(requests);
			Window = Positive(window);
			ElevatedMultiplier = Positive(elevatedMultiplier);
			InteractiveConcurrency = Positive(interactiveConcurrency);
			ListingConcurrency = Positive(listingConcurrency);
			UploadConcurrency = Positive(uploadConcurrency);
			LiveConcurrency = Positive(liveConcurrency);
			DegradedAbove = Fraction(degradedAbove);
			ShedAbove = Fraction(shedAbove);
			HardShedAbove = Fraction(hardShedAbove);
			BusyRetryAfter = Positive(busyRetryAfter);
			AssetConcurrency = Positive(assetConcurrency);
			AssetRequests = Positive(assetRequests);
			if (!(DegradedAbove < ShedAbove && ShedAbove < HardShedAbove))
				throw new ArgumentOutOfRangeException(nameof(shedAbove), shedAbove,
					"The loads must be ordered degraded < shed < hard shed.");
		}

		public AdmissionLimitSettings() : this(120, TimeSpan.FromMinutes(1), 4, 256, 32, 8, 1024, 0.5, 0.7, 0.9,
			TimeSpan.FromSeconds(5))
		{
		}

		public int Requests { get; }

		public TimeSpan Window { get; }

		public int ElevatedMultiplier { get; }

		public int InteractiveConcurrency { get; }

		public int ListingConcurrency { get; }

		public int UploadConcurrency { get; }

		public int LiveConcurrency { get; }

		public double DegradedAbove { get; }

		public double ShedAbove { get; }

		public double HardShedAbove { get; }

		public TimeSpan BusyRetryAfter { get; }

		public int AssetConcurrency { get; }

		public int AssetRequests { get; }
	}

	public sealed record LoginLimitSettings
	{
		public int MaxAttempts
		{
			get;
			init => field = Positive(value);
		} = 5;

		public TimeSpan Window
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromMinutes(15);

		public TimeSpan Lockout
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromMinutes(15);
	}

	public sealed record RegistrationLimitSettings
	{
		public Limit PerIp { get; init; } = new(1, TimeSpan.FromHours(24));

		public Limit PerServer { get; init; } = new(100, TimeSpan.FromHours(1));
	}

	public sealed record ConnectionLimitSettings
	{
		public int PerIp
		{
			get;
			init => field = Positive(value);
		} = 20;
	}

	public sealed record RestLimitSettings
	{
		public Limit Mutations { get; init; } = new(60, TimeSpan.FromMinutes(1));

		public AnonymousLimitSettings Anonymous { get; init; } = new();
	}

	public sealed record AnonymousLimitSettings
	{
		public Limit PerIp { get; init; } = new(120, TimeSpan.FromMinutes(1));
	}

	public sealed record UploadSettings
	{
		public long OszMaxSize
		{
			get;
			init => field = Positive(value);
		} = 100L * 1024 * 1024;

		public long ScreenshotMaxSize
		{
			get;
			init => field = Positive(value);
		} = 10L * 1024 * 1024;

		public int ScreenshotMaxSide
		{
			get;
			init => field = Positive(value);
		} = 8192;

		public long AvatarMaxSize
		{
			get;
			init => field = Positive(value);
		} = 5L * 1024 * 1024;

		public int AvatarMaxSide
		{
			get;
			init => field = Positive(value);
		} = 4096;
	}

	public sealed record BeatmapSettings
	{
		public TimeSpan PerformanceTimeout
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromSeconds(30);
	}

	public sealed record MultiplayerSettings
	{
		public RoundSettings Rounds { get; init; } = new();
	}

	public sealed record RoundSettings
	{
		public TimeSpan ScoreWait
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromSeconds(10);
	}

	public sealed record BotSettings
	{
		public string CommandPrefix
		{
			get;
			init => field = string.IsNullOrWhiteSpace(value)
				? throw new ArgumentException("The prefix must not be empty.", nameof(value))
				: value;
		} = "!";
	}

	public sealed record CorsSettings
	{
		public string[] AllowedOrigins { get; init; } = [];

		public bool Equals(CorsSettings? other)
		{
			return other is not null && AllowedOrigins.SequenceEqual(other.AllowedOrigins);
		}

		public override int GetHashCode()
		{
			return AllowedOrigins.Length;
		}
	}

	public sealed record MenuIconSettings
	{
		public Uri? Url
		{
			get;
			init => field = value?.ThrowIfHttp();
		}

		public ContentReference? Image { get; init; }
	}
}