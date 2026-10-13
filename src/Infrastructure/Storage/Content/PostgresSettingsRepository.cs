using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Common;
using Basil.Infrastructure.Storage.Common.Database;
using Basil.Infrastructure.Storage.Common.Writing;
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
		var row = await reader.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<SettingsRow>(
			"""
			select motd, locked_creation, menu_icon_url, menu_icon_image, mirror_download_endpoint, mirror_search_endpoint
			from settings where id = 1
			"""), cancellationToken).ConfigureAwait(false);

		_settings = row is null
			? new ServerSettings()
			: new ServerSettings
			{
				Motd = row.Motd,
				LockedCreation = (CreationLocks)row.LockedCreation,
				MenuIcon = new MenuIcon { Url = Parse(row.MenuIconUrl), Image = ParseImage(row.MenuIconImage) },
				Mirror = new BeatmapMirror
				{
					DownloadEndpoint = Parse(row.MirrorDownloadEndpoint),
					SearchEndpoint = Parse(row.MirrorSearchEndpoint)
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
			LockedCreation = (int)settings.LockedCreation,
			MenuIconUrl = settings.MenuIcon.Url?.ToString(),
			MenuIconImage = settings.MenuIcon.Image?.ToString(),
			MirrorDownloadEndpoint = settings.Mirror.DownloadEndpoint?.ToString(),
			MirrorSearchEndpoint = settings.Mirror.SearchEndpoint?.ToString()
		};
		Interlocked.Exchange(ref _settings, settings);
		_ = writer.SaveAsync(Root.Server, (GetType(), 1), new WriteCommand(
			"""
			update settings set
				motd = @Motd,
				locked_creation = @LockedCreation,
				menu_icon_url = @MenuIconUrl,
				menu_icon_image = @MenuIconImage,
				mirror_download_endpoint = @MirrorDownloadEndpoint,
				mirror_search_endpoint = @MirrorSearchEndpoint
			where id = 1;
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
	private AssetReference? ParseImage(string? value)
	{
		if (value is null) return null;
		if (AssetReference.TryParse(value, null, out var image)) return image;

		logger.LogWarning("The menu icon image {Image} is not a valid asset reference and is not shown", value);
		return null;
	}

	/// <summary>A stored row of the <c>settings</c> table.</summary>
	private sealed class SettingsRow
	{
		public string? Motd { get; set; }
		public int LockedCreation { get; set; }
		public string? MenuIconUrl { get; set; }
		public string? MenuIconImage { get; set; }
		public string? MirrorDownloadEndpoint { get; set; }
		public string? MirrorSearchEndpoint { get; set; }
	}
}