namespace Basil.Application.Configuration;

/// <summary>
///     Configuration for the optional beatmap set download mirror.
/// </summary>
/// <remarks>
///     This server stores no local .osz files, so <c>/d/{set_id}</c> only works when an operator
///     points <see cref="DownloadEndpoint" /> at a mirror (local or otherwise); when it is unset,
///     the download is reported as unavailable rather than reaching out to the internet. Beatmap
///     search is never mirrored: it queries the local beatmap database instead of proxying a
///     mirror, since this server runs fully offline.
/// </remarks>
public sealed class MirrorOptions
{
	public const string SectionName = "Basil:Mirror";

	/// <summary>Gets or sets the URL of the mirror that serves .osz downloads for the <c>/d/{set_id}</c> endpoint.</summary>
	public string? DownloadEndpoint { get; init; }
}