using Basil.Domain.Utilities;

namespace Basil.Domain.Content;

/// <summary>The server-wide settings an administrator changes while the server runs.</summary>
public sealed record ServerSettings
{
	/// <summary>Gets the message of the day shown to users when they log in, or <see langword="null" /> for none.</summary>
	/// <exception cref="ArgumentException">The value contains the NUL character.</exception>
	public string? Motd { get; init => field = value?.ThrowIfHasNul(); }

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

	/// <summary>Gets the address the main-menu icon links to, or <see langword="null" /> for none.</summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? MenuIconUrl
	{
		get;
		init => field = RequireAbsolute(value);
	}

	/// <summary>
	///     Gets the address of a main-menu icon image hosted elsewhere, or <see langword="null" /> when the server
	///     stores the icon itself or has none.
	/// </summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? MenuIconImage
	{
		get;
		init => field = RequireAbsolute(value);
	}

	/// <summary>
	///     Gets the address beatmapset downloads are sent to, or <see langword="null" /> when the server has no
	///     download mirror.
	/// </summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? MirrorDownloadEndpoint
	{
		get;
		init => field = RequireAbsolute(value);
	}

	/// <summary>
	///     Gets the address beatmap searches are sent to, or <see langword="null" /> when the server has no search
	///     mirror.
	/// </summary>
	/// <exception cref="ArgumentException">The value is not an absolute address.</exception>
	public Uri? MirrorSearchEndpoint
	{
		get;
		init => field = RequireAbsolute(value);
	}

	private static Uri? RequireAbsolute(Uri? value)
	{
		return value is null || value.IsAbsoluteUri
			? value
			: throw new ArgumentException("The address must be absolute.", nameof(value));
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
