namespace Basil.Domain.Content;

/// <summary>The server-wide settings an administrator changes while the server runs.</summary>
public sealed record ServerSettings
{
	/// <summary>Gets the message of the day shown to users when they log in, or <see langword="null" /> for none.</summary>
	public string? Motd { get; init; }

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