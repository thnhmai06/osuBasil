using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Domain;

/// <summary>The server-wide settings an administrator changes while the server runs.</summary>
public sealed record ServerSettings
{
	/// <summary>Gets the message of the day shown to users when they log in, or <see langword="null" /> for none.</summary>
	/// <exception cref="ArgumentException">The value contains the NUL character.</exception>
	public string? Motd
	{
		get;
		init => field = value?.ThrowIfHasNul();
	}

	/// <summary>Gets the kinds of thing only their managers may create; everyone else is refused while a kind is locked.</summary>
	/// <remarks>Beatmapset uploads are locked unless an administrator opens them.</remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value sets a bit no kind defines.</exception>
	public CreationLocks LockedCreation
	{
		get;
		init => field = value.ThrowIfUndefined();
	} = CreationLocks.Beatmapsets;

	/// <summary>Gets the icon shown on the main menu.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	public MenuIconSettings MenuIcon
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new();

	/// <summary>Gets the beatmap mirror that beatmapset downloads and beatmap searches are sent to.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	public BeatmapMirrorSettings BeatmapMirror
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new();

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

	/// <summary>The kinds of thing whose creation can be locked to their managers.</summary>
	[Flags]
	public enum CreationLocks : byte
	{
		/// <summary>Nothing is locked.</summary>
		None = 0,

		/// <summary>Only users who manage accounts create accounts; registration is refused.</summary>
		Accounts = 1 << 0,

		/// <summary>Only users who manage every room create rooms.</summary>
		Rooms = 1 << 1,

		/// <summary>Only users who manage beatmapsets upload them.</summary>
		Beatmapsets = 1 << 2
	}
}