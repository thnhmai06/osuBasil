namespace Basil.Application.Configurations;

/// <summary>
///     Configuration for the optional beatmap mirror, controlling assets/downloads that are missing
///     from local storage.
/// </summary>
/// <remarks>
///     Local storage is always tried first, regardless of whether a mirror is configured. When
///     <see cref="DownloadEndpoint" /> is unset (the default), a beatmapset missing locally is
///     reported as unavailable rather than reaching out to the internet. When it is set, a missing
///     beatmapset with a genuine ppy id falls back to the mirror instead.
/// </remarks>
public sealed class MirrorOptions
{
	public const string SectionName = "Basil:Mirror";

	/// <summary>Gets or sets the URL of the mirror that serves .osz downloads for the <c>/d/{set_id}</c> endpoint.</summary>
	public string? DownloadEndpoint { get; init; }

	/// <summary>
	///     Gets or sets the URL of the mirror API that serves osu!direct search results, independent
	///     of <see cref="DownloadEndpoint" />.
	/// </summary>
	public string? SearchEndpoint { get; init; }

	/// <summary>
	///     Gets a value that indicates whether the server runs in online mirror mode: local storage
	///     is still tried first everywhere, but a beatmapset missing locally falls back to the
	///     mirror instead of reporting unavailable.
	/// </summary>
	public bool IsOnlineMode => !string.IsNullOrEmpty(DownloadEndpoint);

	/// <summary>
	///     Gets a value that indicates whether osu!direct search should query <see cref="SearchEndpoint" />
	///     instead of the local beatmap database.
	/// </summary>
	public bool HasSearchMirror => !string.IsNullOrEmpty(SearchEndpoint);
}