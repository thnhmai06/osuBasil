using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Dapper;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the server-wide settings.</summary>
internal sealed class SqliteSettingsRepository(Database database) : ISettingsRepository
{
	/// <inheritdoc />
	public async ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<SettingsRow>(
			"""
			SELECT Motd, LockedCreation, MenuIconUrl, MenuIconImage, MirrorDownloadEndpoint, MirrorSearchEndpoint
			FROM Settings WHERE Id = 1
			""");

		return row is null
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
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			UPDATE Settings SET
				Motd = @Motd,
				LockedCreation = @LockedCreation,
				MenuIconUrl = @MenuIconUrl,
				MenuIconImage = @MenuIconImage,
				MirrorDownloadEndpoint = @MirrorDownloadEndpoint,
				MirrorSearchEndpoint = @MirrorSearchEndpoint
			WHERE Id = 1;
			""",
			new
			{
				settings.Motd,
				LockedCreation = (long)settings.LockedCreation,
				MenuIconUrl = settings.MenuIconUrl?.ToString(),
				MenuIconImage = settings.MenuIconImage?.ToString(),
				MirrorDownloadEndpoint = settings.MirrorDownloadEndpoint?.ToString(),
				MirrorSearchEndpoint = settings.MirrorSearchEndpoint?.ToString()
			});
	}

	/// <summary>Reads an absolute address, or <see langword="null" /> when none is stored.</summary>
	private static Uri? Parse(string? value)
	{
		return value is null ? null : new Uri(value, UriKind.Absolute);
	}

	/// <summary>A stored row of the Settings table.</summary>
	private sealed class SettingsRow
	{
		public string? Motd { get; set; }
		public long LockedCreation { get; set; }
		public string? MenuIconUrl { get; set; }
		public string? MenuIconImage { get; set; }
		public string? MirrorDownloadEndpoint { get; set; }
		public string? MirrorSearchEndpoint { get; set; }
	}
}
