using Basil.Application.Abstractions.Settings;
using Basil.Application.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Manages the server's beatmap mirror endpoints: the download and search mirrors used when a
///     beatmap or search isn't available locally, stored as mutable server settings rather than a
///     config-file value that needs a restart to change.
/// </summary>
public sealed class MirrorService(
	ISettingsRepository settings,
	IOptions<MirrorOptions> configSeed,
	ILogger<MirrorService> logger)
{
	private const string DownloadEndpointSettingKey = "Mirror:DownloadEndpoint";
	private const string SearchEndpointSettingKey = "Mirror:SearchEndpoint";
	private const string SeededSettingKey = "Mirror:Seeded";

	/// <summary>Gets the currently configured download and search mirror endpoints.</summary>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	public async Task<MirrorEndpoints> GetAsync(CancellationToken cancellationToken = default)
	{
		var download = await settings.GetAsync(DownloadEndpointSettingKey, cancellationToken);
		var search = await settings.GetAsync(SearchEndpointSettingKey, cancellationToken);
		return new MirrorEndpoints(NullIfEmpty(download), NullIfEmpty(search));
	}

	/// <summary>
	///     Replaces both mirror endpoints. A <see langword="null" /> or empty value clears the
	///     corresponding endpoint.
	/// </summary>
	/// <param name="downloadEndpoint">The new download mirror endpoint, or <see langword="null" /> to clear it.</param>
	/// <param name="searchEndpoint">The new search mirror endpoint, or <see langword="null" /> to clear it.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	public async Task SetAsync(string? downloadEndpoint, string? searchEndpoint,
		CancellationToken cancellationToken = default)
	{
		await settings.SetAsync(DownloadEndpointSettingKey, NullIfEmpty(downloadEndpoint), cancellationToken);
		await settings.SetAsync(SearchEndpointSettingKey, NullIfEmpty(searchEndpoint), cancellationToken);
	}

	/// <summary>
	///     One-time upgrade path: the first time this ever runs, seeds the stored endpoints from
	///     <c>appsettings.json</c>'s legacy <see cref="MirrorOptions" /> section (if either is set), so
	///     an existing deployment's mirror keeps working after upgrading without an operator
	///     re-entering it. A no-op on every later startup, even if an operator has since cleared both
	///     endpoints through <see cref="SetAsync" /> -- otherwise a deliberate clear would silently come
	///     back on the next restart.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the reads and, if needed, the writes.</param>
	/// <remarks>
	///     <c>appsettings.json</c>'s <c>Basil:Mirror</c> section is never read again after this runs
	///     once; <see cref="GetAsync" /> is the only source of truth from then on.
	/// </remarks>
	public async Task SeedFromConfigIfUnsetAsync(CancellationToken cancellationToken = default)
	{
		var alreadySeeded = await settings.GetAsync(SeededSettingKey, cancellationToken);
		if (!string.IsNullOrEmpty(alreadySeeded)) return;

		var seed = configSeed.Value;
		if (seed.DownloadEndpoint is not null || seed.SearchEndpoint is not null)
		{
			await SetAsync(seed.DownloadEndpoint, seed.SearchEndpoint, cancellationToken);
			logger.LogInformation(
				"Seeded mirror settings from appsettings.json's Basil:Mirror section into the database " +
				"(one-time upgrade path). Manage mirror endpoints via PUT /settings/mirror from now on -- " +
				"Basil:Mirror in appsettings.json is no longer read.");
		}

		await settings.SetAsync(SeededSettingKey, "true", cancellationToken);
	}

	private static string? NullIfEmpty(string? value)
	{
		return string.IsNullOrEmpty(value) ? null : value;
	}
}

/// <summary>The server's configured beatmap mirror endpoints.</summary>
/// <param name="DownloadEndpoint">The mirror that serves `.osz` downloads, or <see langword="null" /> if unset.</param>
/// <param name="SearchEndpoint">The mirror that serves osu!direct search, or <see langword="null" /> if unset.</param>
public readonly record struct MirrorEndpoints(string? DownloadEndpoint, string? SearchEndpoint)
{
	/// <summary>Gets whether a missing local beatmap falls back to <see cref="DownloadEndpoint" />.</summary>
	public bool IsOnlineMode => !string.IsNullOrEmpty(DownloadEndpoint);

	/// <summary>Gets whether osu!direct search queries <see cref="SearchEndpoint" /> instead of local storage.</summary>
	public bool HasSearchMirror => !string.IsNullOrEmpty(SearchEndpoint);
}