namespace Basil.Application.Configurations;

/// <summary>
///     The one-time upgrade seed for the beatmap mirror endpoints: an existing deployment's
///     <c>appsettings.json</c> value, copied into the database the first time the server starts with
///     this section present. See <see cref="Basil.Application.Services.Beatmaps.MirrorService" /> for
///     the live, mutable source of truth read on every request from then on.
/// </summary>
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
}