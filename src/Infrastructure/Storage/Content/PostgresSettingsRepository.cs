using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the server-wide settings.</summary>
internal sealed class PostgresSettingsRepository(Database database, DatabaseWriter writer) : ISettingsRepository
{
	private ServerSettings? _settings;

	/// <inheritdoc />
	public async ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _settings) is { } cached)
			return cached;

		var row = await database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<SettingsRow>(
			"""
			select motd, locked_creation, menu_icon_url, menu_icon_image, mirror_download_endpoint, mirror_search_endpoint
			from settings where id = 1
			"""), cancellationToken);

		var loaded = row is null
			? new ServerSettings()
			: new ServerSettings
			{
				Motd = row.Motd,
				LockedCreation = (CreationLocks)row.LockedCreation,
				MenuIconUrl = Parse(row.MenuIconUrl),
				MenuIconImage = Parse(row.MenuIconImage),
				MirrorDownloadEndpoint = Parse(row.MirrorDownloadEndpoint),
				MirrorSearchEndpoint = Parse(row.MirrorSearchEndpoint)
			};
		return Interlocked.CompareExchange(ref _settings, loaded, null) ?? loaded;
	}

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		var parameters = new
		{
			settings.Motd,
			LockedCreation = (int)settings.LockedCreation,
			MenuIconUrl = settings.MenuIconUrl?.ToString(),
			MenuIconImage = settings.MenuIconImage?.ToString(),
			MirrorDownloadEndpoint = settings.MirrorDownloadEndpoint?.ToString(),
			MirrorSearchEndpoint = settings.MirrorSearchEndpoint?.ToString()
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
