using System.Globalization;
using Basil.Application.Storage.Content;
using Basil.Domain;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Postgres;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the server-wide settings.</summary>
internal sealed class PostgresSettingsRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	ILogger<PostgresSettingsRepository> logger)
	: ISettingsRepository, IResident
{
	private ServerSettings? _settings;

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync(connection => connection.QueryAsync<(string Name, string? Value)>(
			"select name, value from settings"), cancellationToken).ConfigureAwait(false);
		var values = rows.ToDictionary(row => row.Name, row => row.Value, StringComparer.Ordinal);

		// A setting without a row keeps its default.
		var defaults = new ServerSettings();
		_settings = new ServerSettings
		{
			Motd = values.GetValueOrDefault("motd"),
			LockedCreation = values.GetValueOrDefault("locked_creation") is { } locks
				? (ServerSettings.CreationLocks)int.Parse(locks, CultureInfo.InvariantCulture)
				: defaults.LockedCreation,
			MenuIcon = new ServerSettings.MenuIconSettings
			{
				Url = Parse(values.GetValueOrDefault("menu_icon_url")),
				Image = ParseImage(values.GetValueOrDefault("menu_icon_image"))
			},
			BeatmapMirror = new ServerSettings.BeatmapMirrorSettings
			{
				DownloadEndpoint = Parse(values.GetValueOrDefault("mirror_download_endpoint")),
				SearchEndpoint = Parse(values.GetValueOrDefault("mirror_search_endpoint"))
			}
		};
	}

	/// <inheritdoc />
	public ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Volatile.Read(ref _settings)
		                            ?? throw new InvalidOperationException(
			                            "Server settings are read before the storage has started."));
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		var parameters = new
		{
			settings.Motd,
			LockedCreation = ((int)settings.LockedCreation).ToString(CultureInfo.InvariantCulture),
			MenuIconUrl = settings.MenuIcon.Url?.ToString(),
			MenuIconImage = settings.MenuIcon.Image?.ToString(),
			MirrorDownloadEndpoint = settings.BeatmapMirror.DownloadEndpoint?.ToString(),
			MirrorSearchEndpoint = settings.BeatmapMirror.SearchEndpoint?.ToString()
		};
		Interlocked.Exchange(ref _settings, settings);
		_ = writer.SaveAsync(Root.Server, (GetType(), 1), new WriteCommand(
			"""
			insert into settings (name, value)
			values ('motd', @Motd),
				   ('locked_creation', @LockedCreation),
				   ('menu_icon_url', @MenuIconUrl),
				   ('menu_icon_image', @MenuIconImage),
				   ('mirror_download_endpoint', @MirrorDownloadEndpoint),
				   ('mirror_search_endpoint', @MirrorSearchEndpoint)
			on conflict (name) do update set value = excluded.value;
			""",
			parameters));
		return Task.CompletedTask;
	}

	/// <summary>Reads an absolute address, or <see langword="null" /> when none is stored.</summary>
	private static Uri? Parse(string? value)
	{
		return value is null ? null : new Uri(value, UriKind.Absolute);
	}

	/// <summary>Reads the stored menu icon image, or <see langword="null" /> when none or an invalid one is stored.</summary>
	private ContentReference? ParseImage(string? value)
	{
		if (value is null) return null;
		if (ContentReference.TryParse(value, null, out var image)) return image;

		logger.LogWarning("The menu icon image {Image} is not a valid asset reference and is not shown", value);
		return null;
	}
}