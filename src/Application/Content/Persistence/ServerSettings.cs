using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Application.Content.Persistence;

/// <summary>The server-wide policy an administrator changes while the server runs, grouped in nested categories.</summary>
public sealed record ServerSettings
{
	/// <summary>Gets the general settings.</summary>
	public GeneralSettings General { get; init; } = new();

	/// <summary>Gets the areas that are frozen for everyone but their managers.</summary>
	/// <remarks>Beatmapset uploads are locked unless an administrator opens them.</remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value sets a bit no area defines.</exception>
	public Locks Locks
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = Locks.Beatmapsets;

	/// <summary>Gets the lifetimes of sessions and tokens.</summary>
	public SessionSettings Sessions { get; init; } = new();

	/// <summary>Gets the limits on how often things may be done.</summary>
	public RateLimitSettings RateLimits { get; init; } = new();

	/// <summary>Gets the size limits of uploads.</summary>
	public UploadSettings Uploads { get; init; } = new();

	/// <summary>Gets the beatmap settings.</summary>
	public BeatmapSettings Beatmaps { get; init; } = new();

	/// <summary>Gets the multiplayer settings.</summary>
	public MultiplayerSettings Multiplayer { get; init; } = new();

	/// <summary>Gets the settings of BasilBot.</summary>
	public BotSettings Bot { get; init; } = new();

	/// <summary>Gets the cross-origin settings of the HTTP API.</summary>
	public CorsSettings Cors { get; init; } = new();

	/// <summary>Gets the icon shown on the main menu.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
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

	private static long Positive(long value)
	{
		return value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "The number must be positive.");
	}

	/// <summary>The general settings.</summary>
	public sealed record GeneralSettings
	{
		/// <summary>Gets the message of the day shown to users when they log in, or <see langword="null" /> for none.</summary>
		/// <exception cref="ArgumentException">The value contains the NUL character.</exception>
		public string? Motd
		{
			get;
			init => field = value?.ThrowIfHasNul();
		}
	}

	/// <summary>The lifetimes of sessions and tokens.</summary>
	public sealed record SessionSettings
	{
		/// <summary>Gets the lifetime of an HTTP API token; each use renews it.</summary>
		public ApiTokenSettings ApiToken { get; init; } = new();

		/// <summary>Gets how long a connection may send nothing before it counts as offline.</summary>
		public TimeSpan IdleWindow
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromSeconds(300);

		/// <summary>Gets how long ended sessions are kept.</summary>
		public TimeSpan RetentionPeriod
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromDays(90);
	}

	/// <summary>The lifetime of an HTTP API token.</summary>
	public sealed record ApiTokenSettings
	{
		/// <summary>Gets how long a token stays valid after its last use.</summary>
		public TimeSpan Lifetime
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromDays(7);
	}

	/// <summary>How many times something may happen in a window of time.</summary>
	public sealed record Limit
	{
		/// <summary>Creates a limit.</summary>
		/// <param name="count">How many times it may happen.</param>
		/// <param name="window">How long the count is measured over.</param>
		/// <exception cref="ArgumentOutOfRangeException">The count or the window is not positive.</exception>
		public Limit(int count, TimeSpan window)
		{
			Count = Positive(count);
			Window = Positive(window);
		}

		/// <summary>Gets how many times it may happen.</summary>
		public int Count { get; }

		/// <summary>Gets how long the count is measured over.</summary>
		public TimeSpan Window { get; }
	}

	/// <summary>The limits on how often things may be done.</summary>
	public sealed record RateLimitSettings
	{
		/// <summary>Gets the limit on failed login attempts.</summary>
		public LoginLimitSettings Login { get; init; } = new();

		/// <summary>Gets the limits on registrations.</summary>
		public RegistrationLimitSettings Registration { get; init; } = new();

		/// <summary>Gets the limit on open connections from one address.</summary>
		public ConnectionLimitSettings Connections { get; init; } = new();

		/// <summary>Gets the limits on HTTP API requests.</summary>
		public RestLimitSettings Rest { get; init; } = new();

		/// <summary>Gets the flood limit on chat messages.</summary>
		public Limit Chat { get; init; } = new(10, TimeSpan.FromSeconds(5));
	}

	/// <summary>The limit on failed login attempts.</summary>
	public sealed record LoginLimitSettings
	{
		/// <summary>Gets how many failed attempts are allowed within the window.</summary>
		public int MaxAttempts
		{
			get;
			init => field = Positive(value);
		} = 5;

		/// <summary>Gets how long failed attempts are counted over.</summary>
		public TimeSpan Window
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromMinutes(15);

		/// <summary>Gets how long a source is refused after too many failed attempts.</summary>
		public TimeSpan Lockout
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromMinutes(15);
	}

	/// <summary>The limits on registrations.</summary>
	public sealed record RegistrationLimitSettings
	{
		/// <summary>Gets the limit per IP address.</summary>
		public Limit PerIp { get; init; } = new(1, TimeSpan.FromHours(24));

		/// <summary>Gets the limit for the whole server.</summary>
		public Limit PerServer { get; init; } = new(100, TimeSpan.FromHours(1));
	}

	/// <summary>The limit on open connections from one address.</summary>
	public sealed record ConnectionLimitSettings
	{
		/// <summary>Gets how many connections one IP address may hold open at once.</summary>
		public int PerIp
		{
			get;
			init => field = Positive(value);
		} = 20;
	}

	/// <summary>The limits on HTTP API requests.</summary>
	public sealed record RestLimitSettings
	{
		/// <summary>Gets the limit on changes per user.</summary>
		public Limit Mutations { get; init; } = new(60, TimeSpan.FromMinutes(1));

		/// <summary>Gets the limits on requests without a token.</summary>
		public AnonymousLimitSettings Anonymous { get; init; } = new();
	}

	/// <summary>The limits on requests without a token.</summary>
	public sealed record AnonymousLimitSettings
	{
		/// <summary>Gets the limit per IP address.</summary>
		public Limit PerIp { get; init; } = new(120, TimeSpan.FromMinutes(1));

		/// <summary>Gets the limit per IP address for images.</summary>
		public Limit Assets { get; init; } = new(1200, TimeSpan.FromMinutes(1));
	}

	/// <summary>The size limits of uploads.</summary>
	public sealed record UploadSettings
	{
		/// <summary>Gets the largest beatmapset archive, in bytes.</summary>
		public long OszMaxSize
		{
			get;
			init => field = Positive(value);
		} = 100L * 1024 * 1024;

		/// <summary>Gets the largest screenshot, in bytes.</summary>
		public long ScreenshotMaxSize
		{
			get;
			init => field = Positive(value);
		} = 10L * 1024 * 1024;

		/// <summary>Gets the largest side of a screenshot, in pixels.</summary>
		public int ScreenshotMaxSide
		{
			get;
			init => field = Positive(value);
		} = 8192;

		/// <summary>Gets the largest avatar, in bytes.</summary>
		public long AvatarMaxSize
		{
			get;
			init => field = Positive(value);
		} = 5L * 1024 * 1024;

		/// <summary>Gets the largest side of an avatar, in pixels.</summary>
		public int AvatarMaxSide
		{
			get;
			init => field = Positive(value);
		} = 4096;
	}

	/// <summary>The beatmap settings.</summary>
	public sealed record BeatmapSettings
	{
		/// <summary>Gets the beatmap mirror that beatmapset downloads and beatmap searches are sent to.</summary>
		/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
		public BeatmapMirrorSettings Mirror
		{
			get;
			init => field = value ?? throw new ArgumentNullException(nameof(value));
		} = new();
	}

	/// <summary>The multiplayer settings.</summary>
	public sealed record MultiplayerSettings
	{
		/// <summary>Gets the settings of rounds.</summary>
		public RoundSettings Rounds { get; init; } = new();
	}

	/// <summary>The settings of rounds.</summary>
	public sealed record RoundSettings
	{
		/// <summary>Gets how long the scores of a finished round are waited for.</summary>
		public TimeSpan ScoreWait
		{
			get;
			init => field = Positive(value);
		} = TimeSpan.FromSeconds(10);
	}

	/// <summary>The settings of BasilBot.</summary>
	public sealed record BotSettings
	{
		/// <summary>Gets the text a chat message starts with to be a command.</summary>
		public string CommandPrefix
		{
			get;
			init => field = string.IsNullOrWhiteSpace(value)
				? throw new ArgumentException("The prefix must not be empty.", nameof(value))
				: value;
		} = "!";
	}

	/// <summary>The cross-origin settings of the HTTP API.</summary>
	public sealed record CorsSettings
	{
		/// <summary>Gets the origins that may call the HTTP API from a browser.</summary>
		public string[] AllowedOrigins { get; init; } = [];

		/// <inheritdoc />
		public bool Equals(CorsSettings? other)
		{
			return other is not null && AllowedOrigins.SequenceEqual(other.AllowedOrigins);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return AllowedOrigins.Length;
		}
	}

	/// <summary>The icon shown on the main menu: its image and the address it links to.</summary>
	public sealed record MenuIconSettings
	{
		/// <summary>Gets the address the icon links to, or <see langword="null" /> for none.</summary>
		/// <exception cref="ArgumentException">The value is not absolute with an http or https scheme.</exception>
		public Uri? Url
		{
			get;
			init => field = value?.ThrowIfHttp();
		}

		/// <summary>Gets the image of the icon, or <see langword="null" /> for none.</summary>
		public ContentReference? Image { get; init; }
	}

	/// <summary>The beatmap mirror the server sends beatmapset downloads and beatmap searches to.</summary>
	public sealed record BeatmapMirrorSettings
	{
		/// <summary>
		///     Gets the address beatmapset downloads are sent to, or <see langword="null" /> when the server has no
		///     download mirror.
		/// </summary>
		/// <exception cref="ArgumentException">The value is not absolute with an http or https scheme.</exception>
		public Uri? DownloadEndpoint
		{
			get;
			init => field = value?.ThrowIfHttp();
		}

		/// <summary>
		///     Gets the address beatmap searches are sent to, or <see langword="null" /> when the server has no search
		///     mirror.
		/// </summary>
		/// <exception cref="ArgumentException">The value is not absolute with an http or https scheme.</exception>
		public Uri? SearchEndpoint
		{
			get;
			init => field = value?.ThrowIfHttp();
		}
	}
}