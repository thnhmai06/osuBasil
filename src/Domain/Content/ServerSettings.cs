using Basil.Domain.Utilities;

namespace Basil.Domain.Content;

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
		init
		{
			value.ThrowIfUndefined();
			field = value;
		}
	} = CreationLocks.Beatmapsets;

	/// <summary>Gets the icon shown on the main menu.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	public MenuIcon MenuIcon
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new();

	/// <summary>Gets the beatmap mirror that beatmapset downloads and beatmap searches are sent to.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	public BeatmapMirror Mirror
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	} = new();

	/// <summary>Returns an address unchanged, or throws when it is not absolute.</summary>
	/// <exception cref="ArgumentException">The address is not absolute.</exception>
	internal static Uri? RequireAbsolute(Uri? value)
	{
		return value is null || value.IsAbsoluteUri
			? value
			: throw new ArgumentException("The address must be absolute.", nameof(value));
	}
}

/// <summary>The icon shown on the main menu: its image and the address it links to.</summary>
public sealed record MenuIcon
{
	/// <summary>Gets the address the icon links to, or <see langword="null" /> for none.</summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? Url
	{
		get;
		init => field = ServerSettings.RequireAbsolute(value);
	}

	/// <summary>Gets the image of the icon, or <see langword="null" /> for none.</summary>
	public AssetReference? Image { get; init; }
}

/// <summary>The beatmap mirror the server sends beatmapset downloads and beatmap searches to.</summary>
public sealed record BeatmapMirror
{
	/// <summary>
	///     Gets the address beatmapset downloads are sent to, or <see langword="null" /> when the server has no
	///     download mirror.
	/// </summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? DownloadEndpoint
	{
		get;
		init => field = ServerSettings.RequireAbsolute(value);
	}

	/// <summary>
	///     Gets the address beatmap searches are sent to, or <see langword="null" /> when the server has no search
	///     mirror.
	/// </summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? SearchEndpoint
	{
		get;
		init => field = ServerSettings.RequireAbsolute(value);
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